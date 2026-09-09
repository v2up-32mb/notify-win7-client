import ADMIN_HTML from "./admin.html";
import SPEC_TEXT from "./api_spec.json";
const SPEC = JSON.parse(SPEC_TEXT);

function specMarkdown() {
  const L = [];
  L.push("# " + SPEC.name + " API（v" + SPEC.version + "）");
  L.push("");
  L.push(SPEC.description);
  L.push("");
  L.push("Base: " + SPEC.base_urls.cloud + "（本地 " + SPEC.base_urls.local + "）");
  L.push("");
  L.push("## 认证");
  L.push(SPEC.auth.summary);
  SPEC.auth.schemes.forEach((s) => {
    L.push("- " + s.kind + "（" + s.prefix + "开头）获取：" + s.how + "；能做：" + s.can.join("、"));
  });
  L.push("强制规则：" + SPEC.auth.enforcement);
  L.push("");
  L.push("## 消息与投递");
  L.push(SPEC.message.targeting);
  L.push("可见性：" + SPEC.message.visibility);
  L.push("轮询：" + SPEC.polling);
  L.push("");
  L.push("## 接口");
  SPEC.endpoints.forEach((e) => {
    L.push("### " + e.method + " " + e.path);
    L.push("- 认证：" + e.auth + "；" + e.summary);
    if (e.body) L.push("- Body 例：" + JSON.stringify(e.body));
    L.push("- 返回例：" + (typeof e.response === "string" ? e.response : JSON.stringify(e.response)));
  });
  L.push("");
  L.push("## 调用示例（把 {{BASE}} 换成服务地址，{{PRODUCER_TOKEN}} / {{CLIENT_SECRET}} 换成配发的口令）");
  SPEC.examples.forEach((x) => {
    L.push("### " + x.title + "（" + x.lang + "）");
    L.push("```" + x.lang);
    L.push(x.code);
    L.push("```");
  });
  return L.join("\n");
}

/**
 * NotifyCenter S端 — Cloudflare Workers + KV（含认证与定向投递）
 *
 * 认证模型（零配置渐进式）：
 *   - 管理员口令 ADMIN_TOKEN（wrangler.toml [vars] 明文填写）：只能用于 /api/admin/* 配发身份
 *   - 生产者令牌 ps_xxx：只能 POST /api/messages 发消息，可随时在后台查看/编辑/删除
 *   - 客户端密钥 cs_xxx：只能 GET /api/messages 拉取，可随时在后台查看/复制 config.json
 *   - KV 里存 sha256 用于校验，同时保存明文 token 供管理后台展示（与 ADMIN_TOKEN 同一信任级别）
 *   - 还没配发过任何 producer/client 时保持开放模式（老客户端/老发送脚本零改造可跑）；
 *     一旦配发了 producer 则发送必须认证，一旦配发了 client 则拉取必须认证
 *
 * 投递范围（消息体 to 字段）：
 *   { all:true } = 广播；{ groups:["运维","财务"] } = 组；{ clients:["pc-01"] } = 点对点
 *   可组合。客户端看到消息 ⇔ 广播 / 自己所在组命中 / 自己被点名。组归属由 S 端绑定，
 *   客户端自己声称的不算数（防冒领）。
 *
 * KV 布局 (namespace: NOTIFY_KV):
 *   msg:<id>            单条消息 JSON（含 to，expirationTtl 7天）
 *   index               全部 id 数组（旧→新，上限 500）
 *   auth:<sha256>       令牌记录 {kind:"client"|"producer", id, groups?}
 *   clients             已配发客户端表 {<id>: {groups:[], created, tokens:[], secret}}
 *   producers           生产者注册表 {name: {created, hash, token}}
 *
 * 编辑语义（PUT /api/admin/clients、PUT /api/admin/groups）：
 *   只改客户端归属与令牌绑定，历史消息的 to 不追改（后续投递按新值计算）。
 *
 * 历史清理：wrangler.toml 里配了每天北京时间 04:00 的 Cron，触发 scheduled()，
 *   删掉昨天及更早的消息（只留今天，keepDays=0）；无 ts 的脏数据视为最旧一并清理。
 *   定时/手动清理结果记在 cleanup:last（概况页可见）。KV 本身另有 7 天 TTL 兜底。
 */

const INDEX_KEY = "index";
const MAX_INDEX = 500;
const MSG_TTL = 7 * 24 * 3600;
const DEFAULT_LIMIT = 20;

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, DELETE, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, Authorization",
};

function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: Object.assign({ "Content-Type": "application/json; charset=utf-8" }, CORS),
  });
}

function makeId() {
  return Date.now() + "-" + Math.random().toString(16).slice(2, 10);
}

function randomToken(prefix) {
  const bytes = new Uint8Array(12);
  crypto.getRandomValues(bytes);
  return prefix + "_" + [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
}

async function sha256hex(s) {
  const d = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(s));
  return [...new Uint8Array(d)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function safeEqual(a, b) {
  try {
    if (typeof a !== "string" || typeof b !== "string" || a.length !== b.length) return false;
    if (crypto.subtle && crypto.subtle.timingSafeEqual) {
      const ab = new TextEncoder().encode(a);
      const bb = new TextEncoder().encode(b);
      return crypto.subtle.timingSafeEqual(ab, bb);
    }
  } catch (e) {
    return false;
  }
  return a === b;
}

function bearerToken(request) {
  const h = request.headers.get("Authorization") || "";
  const m = h.match(/^Bearer\s+(.+)$/i);
  return m ? m[1].trim() : "";
}

async function kvGetJson(env, key, fallback) {
  try {
    const raw = await env.NOTIFY_KV.get(key);
    if (!raw) return fallback;
    return JSON.parse(raw);
  } catch (e) {
    return fallback;
  }
}

/** 解析调用者身份：{kind:"anon"|"admin"|"producer"|"client", id, groups} */
async function resolveCaller(request, env) {
  const token = bearerToken(request);
  if (env.ADMIN_TOKEN && token && safeEqual(token, env.ADMIN_TOKEN)) {
    return { kind: "admin", id: "admin", groups: [] };
  }
  if (token) {
    const rec = await kvGetJson(env, "auth:" + (await sha256hex(token)), null);
    if (rec && (rec.kind === "client" || rec.kind === "producer")) {
      return { kind: rec.kind, id: rec.id, groups: rec.groups || [] };
    }
  }
  return { kind: "anon", id: "", groups: [] };
}

function normalizeTargets(body) {
  // 新写法 to_groups/to_clients/to_all；兼容老写法 target
  let groups = [];
  let clients = [];
  let all = false;
  if (body.to_all === true) all = true;
  if (Array.isArray(body.to_groups)) groups = body.to_groups.map(String).filter(Boolean);
  if (Array.isArray(body.to_clients)) clients = body.to_clients.map(String).filter(Boolean);
  if (typeof body.to_groups === "string" && body.to_groups) groups = body.to_groups.split(",").map((s) => s.trim()).filter(Boolean);
  if (typeof body.to_clients === "string" && body.to_clients) clients = body.to_clients.split(",").map((s) => s.trim()).filter(Boolean);
  if (!all && groups.length === 0 && clients.length === 0) {
    const legacy = String(body.target || "all");
    if (legacy === "all") all = true;
    else groups = [legacy];
  }
  if (!all && groups.length === 0 && clients.length === 0) all = true; // 兜底广播
  return { all, groups: groups.slice(0, 20), clients: clients.slice(0, 50) };
}

/** 这条消息 caller 能不能看 */
function canSee(msg, caller) {
  const to = msg.to || { all: true };
  if (to.all) return true;
  if (caller.kind === "admin") return true;
  if (caller.kind !== "client") return false;
  if ((to.groups || []).some((g) => caller.groups.includes(g))) return true;
  if ((to.clients || []).includes(caller.id)) return true;
  return false;
}

async function readIndex(env) {
  return kvGetJson(env, INDEX_KEY, []);
}

/** 生产者注册表 {name: {created, hash, token}}；兼容老形状 ["name"...]（token 未知，需轮换补全） */
async function readProducers(env) {
  const raw = await kvGetJson(env, "producers", {});
  if (Array.isArray(raw)) {
    const obj = {};
    raw.forEach((n) => { obj[String(n)] = { created: 0, hash: null, token: null }; });
    return obj;
  }
  return raw && typeof raw === "object" ? raw : {};
}

function producerCount(producers) {
  return Object.keys(producers).length;
}

async function loadMessages(env, ids) {
  const out = await Promise.all(
    ids.map(async (id) => {
      try {
        const raw = await env.NOTIFY_KV.get("msg:" + id);
        return raw ? JSON.parse(raw) : null;
      } catch (e) {
        return null;
      }
    })
  );
  return out.filter(Boolean);
}

/* 北京时间(UTC+8，无夏令时)按天清理历史消息 */
const BEIJING_OFFSET_MS = 8 * 3600 * 1000;
const DAY_MS = 24 * 3600 * 1000;

/** ts 所在北京时间那天的 00:00（UTC 毫秒） */
function beijingDayStart(ts) {
  const day = Math.floor((ts + BEIJING_OFFSET_MS) / DAY_MS);
  return day * DAY_MS - BEIJING_OFFSET_MS;
}

function beijingDateStr(ts) {
  const d = new Date(ts + BEIJING_OFFSET_MS);
  const p = (n) => String(n).padStart(2, "0");
  return d.getUTCFullYear() + "-" + p(d.getUTCMonth() + 1) + "-" + p(d.getUTCDate());
}

/**
 * 清理历史消息：删除 ts < cutoff 的消息（cutoff = 北京时间今天 00:00 - keepDays 天）。
 * keepDays=0 即删掉昨天及更早的消息（只留今天）；无 ts 的脏数据视为最旧一并清理。
 * 同时剔除 index 里已无正文的孤儿 id。dryRun 只统计不删除；非 dryRun 把结果写入 cleanup:last。
 */
async function cleanupHistory(env, options) {
  let keepDays = options && options.keepDays != null ? parseInt(options.keepDays, 10) : 0;
  if (isNaN(keepDays)) keepDays = 0;
  keepDays = Math.max(0, Math.min(30, keepDays));
  const dryRun = !!(options && options.dryRun);
  const now = Date.now();
  const cutoff = beijingDayStart(now) - keepDays * DAY_MS;
  const idx = await readIndex(env);
  const msgs = await loadMessages(env, idx);
  const byId = new Map(msgs.map((m) => [m.id, m]));
  const keep = [];
  let deleted = 0;
  let orphans = 0;
  const byDate = {};
  for (const id of idx) {
    const m = byId.get(id);
    if (!m) { orphans++; continue; } // 孤儿 id：正文已无（过期或脏数据），直接从 index 剔除
    const ts = typeof m.ts === "number" ? m.ts : 0;
    if (ts < cutoff) {
      if (!dryRun) {
        try { await env.NOTIFY_KV.delete("msg:" + id); } catch (e) { /* 忽略单条失败 */ }
      }
      deleted++;
      const ds = beijingDateStr(ts);
      byDate[ds] = (byDate[ds] || 0) + 1;
    } else {
      keep.push(id);
    }
  }
  const result = {
    ok: true,
    dry_run: dryRun,
    cutoff,
    cutoff_beijing: beijingDateStr(cutoff) + " 00:00",
    keep_days: keepDays,
    checked: idx.length,
    deleted,
    orphans,
    kept: keep.length,
    by_date: byDate,
    at: now,
  };
  if (!dryRun) {
    await env.NOTIFY_KV.put(INDEX_KEY, JSON.stringify(keep.slice(-MAX_INDEX)));
    try {
      await env.NOTIFY_KV.put("cleanup:last", JSON.stringify({
        at: now, cutoff, keep_days: keepDays, deleted, orphans, kept: keep.length,
      }));
    } catch (e) { /* 忽略 */ }
  }
  return result;
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const path = url.pathname;

    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: CORS });
    }

    // 管理后台 UI（浏览器打开 /admin，输入 ADMIN_TOKEN 在线管理）
    if (path === "/admin" && request.method === "GET") {
      return new Response(ADMIN_HTML, {
        status: 200,
        headers: { "Content-Type": "text/html; charset=utf-8" },
      });
    }

    // 自描述 API：机器读 JSON，人/AI 聊天读 Markdown
    if (path === "/api/spec" && request.method === "GET") {
      return json(SPEC);
    }
    if (path === "/api/spec.md" && request.method === "GET") {
      return new Response(specMarkdown(), {
        status: 200,
        headers: Object.assign({ "Content-Type": "text/markdown; charset=utf-8" }, CORS),
      });
    }

    if (path === "/" || path === "/health") {      const clients = await kvGetJson(env, "clients", {});
      const producers = await readProducers(env);
      return json({
        ok: true,
        service: "notify-center",
        ts: Date.now(),
        auth_enforced: { post: producerCount(producers) > 0, get: Object.keys(clients).length > 0 },
      });
    }

    // ---------------- 管理接口（ADMIN_TOKEN） ----------------
    if (path.startsWith("/api/admin/")) {
      const caller = await resolveCaller(request, env);
      if (caller.kind !== "admin") {
        return json({ ok: false, error: "admin required" }, 401);
      }

      if (path === "/api/admin/overview" && request.method === "GET") {
        const idx = await readIndex(env);
        const clients = await kvGetJson(env, "clients", {});
        const producers = await readProducers(env);
        const clientList = Object.keys(clients).map((id) => ({
          id,
          groups: clients[id].groups || [],
          created: clients[id].created || 0,
          secret: clients[id].secret || null, // 老记录轮换后才有
        }));
        const producerList = Object.keys(producers).map((n) => ({
          name: n,
          created: producers[n].created || 0,
          token: producers[n].token || null, // 老记录轮换后才有
        }));
        // 分组汇总：由客户端归属实时聚合（历史消息的 to 不计入、不追改）
        const groupMap = {};
        clientList.forEach((c) => {
          (c.groups || []).forEach((g) => {
            if (!groupMap[g]) groupMap[g] = [];
            groupMap[g].push(c.id);
          });
        });
        const groupList = Object.keys(groupMap).sort().map((g) => ({
          name: g,
          count: groupMap[g].length,
          clients: groupMap[g].sort(),
        }));
        const lastCleanup = await kvGetJson(env, "cleanup:last", null);
        return json({ ok: true, messages: idx.length, clients: clientList, producers: producerList, groups: groupList, cleanup: lastCleanup });
      }

      if (path === "/api/admin/clients" && request.method === "POST") {
        let body = {};
        try { body = await request.json(); } catch (e) { return json({ ok: false, error: "invalid json" }, 400); }
        const cid = String(body.client_id || "").trim().slice(0, 64);
        const hasGroups = body && Object.prototype.hasOwnProperty.call(body, "groups");
        let groups = Array.isArray(body.groups) ? body.groups.map(String).map((s) => s.trim()).filter(Boolean).slice(0, 20)
          : String(body.groups || "").split(",").map((s) => s.trim()).filter(Boolean).slice(0, 20);
        const rotate = url.searchParams.get("rotate") === "yes";
        if (!cid) return json({ ok: false, error: "client_id required" }, 400);
        const clients = await kvGetJson(env, "clients", {});
        if (clients[cid] && !rotate) {
          return json({ ok: false, error: "client exists, use ?rotate=yes to rotate secret" }, 409);
        }
        // 轮换且调用方未传 groups 时保留原分组（修复后台“轮换即清分组”问题）
        if (clients[cid] && rotate && !hasGroups) {
          groups = clients[cid].groups || [];
        }
        const created = (clients[cid] && clients[cid].created) || Date.now();
        // 轮换时先吊销该客户端旧令牌
        if (clients[cid] && rotate && Array.isArray(clients[cid].tokens)) {
          await Promise.all(clients[cid].tokens.map((h) => env.NOTIFY_KV.delete("auth:" + h).catch(() => {})));
        }
        const secret = randomToken("cs");
        const h = await sha256hex(secret);
        await env.NOTIFY_KV.put("auth:" + h, JSON.stringify({ kind: "client", id: cid, groups }));
        clients[cid] = { groups, created, tokens: [h], secret };
        await env.NOTIFY_KV.put("clients", JSON.stringify(clients));
        return json({ ok: true, client_id: cid, groups, secret, note: "密钥已保存，可在管理后台随时查看/复制" });
      }

      if (path === "/api/admin/clients" && request.method === "DELETE") {
        const cid = url.searchParams.get("id") || "";
        const clients = await kvGetJson(env, "clients", {});
        if (!clients[cid]) return json({ ok: false, error: "not found" }, 404);
        if (Array.isArray(clients[cid].tokens)) {
          await Promise.all(clients[cid].tokens.map((h) => env.NOTIFY_KV.delete("auth:" + h).catch(() => {})));
        }
        delete clients[cid];
        await env.NOTIFY_KV.put("clients", JSON.stringify(clients));
        return json({ ok: true, revoked: cid });
      }

      if (path === "/api/admin/clients" && request.method === "PUT") {
        // 编辑客户端：改 ID {client_id, new_id} / 改分组 {client_id, groups} / 换密钥 {client_id, rotate:true}（可组合）
        // 历史消息的 to.clients/to.groups 不追改，只影响后续投递与鉴权。
        let body = {};
        try { body = await request.json(); } catch (e) { return json({ ok: false, error: "invalid json" }, 400); }
        const cid = String(body.client_id || "").trim().slice(0, 64);
        if (!cid) return json({ ok: false, error: "client_id required" }, 400);
        const clients = await kvGetJson(env, "clients", {});
        const rec = clients[cid];
        if (!rec) return json({ ok: false, error: "client not found" }, 404);
        const rawNewId = body.new_id != null ? String(body.new_id).trim().slice(0, 64) : "";
        const finalId = rawNewId || cid;
        if (finalId !== cid && clients[finalId]) {
          return json({ ok: false, error: "new_id exists" }, 409);
        }
        const hasGroups = body && Object.prototype.hasOwnProperty.call(body, "groups");
        let finalGroups = rec.groups || [];
        if (hasGroups) {
          finalGroups = Array.isArray(body.groups)
            ? body.groups.map((x) => String(x).trim()).filter(Boolean).slice(0, 20)
            : String(body.groups || "").split(",").map((s) => s.trim()).filter(Boolean).slice(0, 20);
        }
        const wantRotate = body.rotate === true;
        if (wantRotate) {
          if (Array.isArray(rec.tokens)) {
            await Promise.all(rec.tokens.map((h) => env.NOTIFY_KV.delete("auth:" + h).catch(() => {})));
          }
          const secret = randomToken("cs");
          const h = await sha256hex(secret);
          await env.NOTIFY_KV.put("auth:" + h, JSON.stringify({ kind: "client", id: finalId, groups: finalGroups }));
          rec.tokens = [h];
          rec.secret = secret;
          rec.groups = finalGroups;
          if (finalId !== cid) delete clients[cid];
          clients[finalId] = rec;
          await env.NOTIFY_KV.put("clients", JSON.stringify(clients));
          return json({ ok: true, client_id: finalId, groups: finalGroups, secret, rotated: true, renamed: finalId !== cid });
        }
        // 不换密钥：就地更新该客户端全部令牌记录的 id/groups（密钥不变）
        if (Array.isArray(rec.tokens) && rec.tokens.length) {
          await Promise.all(rec.tokens.map(async (h) => {
            try {
              await env.NOTIFY_KV.put("auth:" + h, JSON.stringify({ kind: "client", id: finalId, groups: finalGroups }));
            } catch (e) { /* 忽略单条失败 */ }
          }));
        }
        rec.groups = finalGroups;
        if (finalId !== cid) delete clients[cid];
        clients[finalId] = rec;
        await env.NOTIFY_KV.put("clients", JSON.stringify(clients));
        return json({ ok: true, client_id: finalId, groups: finalGroups, secret: rec.secret || null, rotated: false, renamed: finalId !== cid });
      }

      if (path === "/api/admin/groups" && request.method === "PUT") {
        // 分组重命名：批量更新组内所有客户端的 groups 及对应令牌记录。历史消息不追改。
        let body = {};
        try { body = await request.json(); } catch (e) { return json({ ok: false, error: "invalid json" }, 400); }
        const oldName = String(body.old_name != null ? body.old_name : (body.old != null ? body.old : "")).trim().slice(0, 64);
        const newName = String(body.new_name != null ? body.new_name : (body.new != null ? body.new : "")).trim().slice(0, 64);
        if (!oldName) return json({ ok: false, error: "old_name required" }, 400);
        if (!newName) return json({ ok: false, error: "new_name required" }, 400);
        if (oldName === newName) return json({ ok: false, error: "same name" }, 400);
        const clients = await kvGetJson(env, "clients", {});
        const affected = [];
        for (const cid of Object.keys(clients)) {
          const gs = clients[cid].groups || [];
          if (!gs.includes(oldName)) continue;
          const seen = {};
          const next = [];
          gs.forEach((g) => {
            const mapped = g === oldName ? newName : g;
            if (!seen[mapped]) { seen[mapped] = true; next.push(mapped); }
          });
          clients[cid].groups = next.slice(0, 20);
          affected.push(cid);
          // 同步该客户端令牌记录中的 groups（密钥不变）
          if (Array.isArray(clients[cid].tokens)) {
            for (const h of clients[cid].tokens) {
              try {
                const raw = await env.NOTIFY_KV.get("auth:" + h);
                const old = raw ? JSON.parse(raw) : { kind: "client", id: cid };
                const og = Array.isArray(old.groups) ? old.groups : [];
                const nseen = {};
                const ng = [];
                og.forEach((g) => {
                  const mapped = g === oldName ? newName : g;
                  if (!nseen[mapped]) { nseen[mapped] = true; ng.push(mapped); }
                });
                // 兼容令牌记录缺 groups 的情况：以客户端最新分组为准
                const finalGroups = og.length ? ng.slice(0, 20) : clients[cid].groups;
                await env.NOTIFY_KV.put("auth:" + h, JSON.stringify({ kind: "client", id: old.id || cid, groups: finalGroups }));
              } catch (e) { /* 忽略单条失败 */ }
            }
          }
        }
        if (!affected.length) return json({ ok: false, error: "group not found" }, 404);
        affected.sort();
        await env.NOTIFY_KV.put("clients", JSON.stringify(clients));
        return json({ ok: true, old: oldName, new: newName, affected, count: affected.length });
      }

      if (path === "/api/admin/producers" && request.method === "POST") {
        let body = {};
        try { body = await request.json(); } catch (e) { return json({ ok: false, error: "invalid json" }, 400); }
        const name = String(body.name || "").trim().slice(0, 64) || "producer";
        const rotate = url.searchParams.get("rotate") === "yes";
        const producers = await readProducers(env);
        if (producers[name] && !rotate) {
          return json({ ok: false, error: "producer exists, use ?rotate=yes to rotate token" }, 409);
        }
        if (producers[name] && rotate && producers[name].hash) {
          await env.NOTIFY_KV.delete("auth:" + producers[name].hash).catch(() => {});
        }
        const token = randomToken("ps");
        const h = await sha256hex(token);
        await env.NOTIFY_KV.put("auth:" + h, JSON.stringify({ kind: "producer", id: name }));
        producers[name] = { created: Date.now(), hash: h, token };
        await env.NOTIFY_KV.put("producers", JSON.stringify(producers));
        return json({ ok: true, name, token, note: "令牌已保存，可在管理后台随时查看/复制" });
      }

      if (path === "/api/admin/producers" && request.method === "PUT") {
        // 编辑：改名 {name, new_name} / 换令牌 {name, rotate:true}（可组合）
        let body = {};
        try { body = await request.json(); } catch (e) { return json({ ok: false, error: "invalid json" }, 400); }
        const name = String(body.name || "").trim().slice(0, 64);
        const producers = await readProducers(env);
        if (!name || !producers[name]) return json({ ok: false, error: "producer not found" }, 404);
        let rec = producers[name];
        const newName = String(body.new_name || "").trim().slice(0, 64);
        if (newName && newName !== name) {
          if (producers[newName]) return json({ ok: false, error: "new_name exists" }, 409);
          delete producers[name];
          producers[newName] = rec;
          if (rec.hash) {
            await env.NOTIFY_KV.put("auth:" + rec.hash, JSON.stringify({ kind: "producer", id: newName }));
          }
          rec = producers[newName];
        }
        if (body.rotate === true) {
          if (rec.hash) await env.NOTIFY_KV.delete("auth:" + rec.hash).catch(() => {});
          const token = randomToken("ps");
          const h = await sha256hex(token);
          const finalName = newName && newName !== name ? newName : name;
          await env.NOTIFY_KV.put("auth:" + h, JSON.stringify({ kind: "producer", id: finalName }));
          rec.hash = h;
          rec.token = token;
        }
        await env.NOTIFY_KV.put("producers", JSON.stringify(producers));
        const finalName = newName && newName !== name ? newName : name;
        return json({ ok: true, name: finalName, token: rec.token });
      }

      if (path === "/api/admin/producers" && request.method === "DELETE") {
        const name = url.searchParams.get("name") || "";
        const producers = await readProducers(env);
        if (!name || !producers[name]) return json({ ok: false, error: "producer not found" }, 404);
        if (producers[name].hash) {
          await env.NOTIFY_KV.delete("auth:" + producers[name].hash).catch(() => {});
        }
        delete producers[name];
        await env.NOTIFY_KV.put("producers", JSON.stringify(producers));
        return json({ ok: true, revoked: name });
      }

      if (path === "/api/admin/cleanup" && request.method === "POST") {
        // 手动清理历史消息：{keep_days?: 0-30, dry_run?: true}；keep_days=0 即只留今天（删昨天及更早）
        let body = {};
        try { body = await request.json(); } catch (e) { return json({ ok: false, error: "invalid json" }, 400); }
        let keepDays = 0;
        if (body.keep_days != null) {
          keepDays = parseInt(body.keep_days, 10);
          if (isNaN(keepDays)) return json({ ok: false, error: "keep_days must be 0-30" }, 400);
          keepDays = Math.max(0, Math.min(30, keepDays));
        }
        const result = await cleanupHistory(env, { keepDays, dryRun: body.dry_run === true });
        return json(result);
      }

      return json({ ok: false, error: "not found" }, 404);
    }

    // ---------------- 发送消息（producer/admin；配发过 producer 后强制认证） ----------------
    if (path === "/api/messages" && request.method === "POST") {
      const producers = await readProducers(env);
      const caller = await resolveCaller(request, env);
      if (producerCount(producers) > 0 && caller.kind !== "producer" && caller.kind !== "admin") {
        return json({ ok: false, error: "producer token required" }, 401);
      }
      let body = {};
      try { body = await request.json(); } catch (e) { return json({ ok: false, error: "invalid json" }, 400); }
      const title = String(body.title || "通知").slice(0, 100);
      const text = String(body.body || body.text || "").slice(0, 2000);
      const level = ["info", "warn", "error", "success"].includes(body.level) ? body.level : "info";
      const tag = String(body.tag || "").slice(0, 64);
      if (!text && !body.title) return json({ ok: false, error: "title or body required" }, 400);
      const msg = {
        id: makeId(),
        title, body: text, level, tag,
        to: normalizeTargets(body),
        from: caller.kind === "anon" ? "anon" : caller.id,
        ts: Date.now(),
      };
      // 兼容老客户端：保留 target 字段供展示
      msg.target = msg.to.all ? "all" : (msg.to.groups[0] || msg.to.clients[0] || "all");
      const idx = await readIndex(env);
      idx.push(msg.id);
      await env.NOTIFY_KV.put("msg:" + msg.id, JSON.stringify(msg), { expirationTtl: MSG_TTL });
      await env.NOTIFY_KV.put(INDEX_KEY, JSON.stringify(idx.slice(-MAX_INDEX)));
      return json({ ok: true, msg });
    }

    // ---------------- 拉取消息（client/admin；配发过 client 后强制认证+按范围过滤） ----------------
    if (path === "/api/messages" && request.method === "GET") {
      const clients = await kvGetJson(env, "clients", {});
      const enforced = Object.keys(clients).length > 0;
      const caller = await resolveCaller(request, env);
      if (enforced && caller.kind !== "client" && caller.kind !== "admin") {
        return json({ ok: false, error: "client secret required" }, 401);
      }
      const since = url.searchParams.get("since") || "";
      const limit = Math.min(parseInt(url.searchParams.get("limit") || DEFAULT_LIMIT, 10) || DEFAULT_LIMIT, 200);
      const idx = await readIndex(env);
      let ids;
      if (!since) ids = idx.slice(-limit);
      else {
        const pos = idx.indexOf(since);
        ids = pos === -1 ? idx.slice(-limit) : idx.slice(pos + 1, pos + 1 + limit);
      }
      let messages = await loadMessages(env, ids);
      if (enforced) messages = messages.filter((m) => canSee(m, caller));
      else {
        // 开放模式兼容老 target 参数
        const target = url.searchParams.get("target") || "all";
        if (target && target !== "all") messages = messages.filter((m) => (m.target || "all") === "all" || (m.target || "") === target);
      }
      return json({
        ok: true, messages, count: messages.length,
        latest: idx.length ? idx[idx.length - 1] : "",
        you: { kind: caller.kind, id: caller.id, groups: caller.groups },
      });
    }

    if (path === "/api/ack" && request.method === "POST") {
      return json({ ok: true });
    }

    if (path === "/api/messages" && request.method === "DELETE") {
      const caller = await resolveCaller(request, env);
      if (caller.kind !== "admin") return json({ ok: false, error: "admin required" }, 401);
      if (url.searchParams.get("confirm") !== "yes") {
        return json({ ok: false, error: "add ?confirm=yes to clear" }, 400);
      }
      const idx = await readIndex(env);
      await Promise.all(idx.map((id) => env.NOTIFY_KV.delete("msg:" + id).catch(() => {})));
      await env.NOTIFY_KV.delete(INDEX_KEY);
      return json({ ok: true, cleared: idx.length });
    }

    return json({ ok: false, error: "not found" }, 404);
  },

  async scheduled(event, env, ctx) {
    // 定时清理（cron 见 wrangler.toml，按 UTC 配置，对应北京时间每天 04:00）：
    // 删掉昨天及更早的历史消息，只留今天。
    try {
      const r = await cleanupHistory(env, { keepDays: 0 });
      console.log("[cleanup] deleted=" + r.deleted + " orphans=" + r.orphans + " kept=" + r.kept + " cutoff=" + r.cutoff_beijing);
    } catch (e) {
      console.log("[cleanup] failed: " + (e && e.message));
    }
  },
};
