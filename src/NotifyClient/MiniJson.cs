using System;
using System.Collections.Generic;
using System.Text;

// Minimal JSON scanner: only understands what /api/messages returns.
// Avoids System.Web.Extensions / Newtonsoft => smaller exe + less memory.
class Msg
{
    public string id, title, body, level, tag;
    public long ts;
}

static class MiniJson
{
    public static List<Msg> ParseMessages(string json, out string latest)
    {
        List<Msg> list = new List<Msg>(8);
        latest = "";
        if (string.IsNullOrEmpty(json)) return list;
        latest = GetStr(json, "\"latest\"");
        int mi = json.IndexOf("\"messages\"");
        if (mi < 0) return list;
        int arr = json.IndexOf('[', mi);
        if (arr < 0) return list;
        int i = arr + 1;
        int depth = 0;
        int objStart = -1;
        bool inStr = false, esc = false;
        for (; i < json.Length; i++)
        {
            char c = json[i];
            if (inStr)
            {
                if (esc) esc = false;
                else if (c == '\\') esc = true;
                else if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') inStr = true;
            else if (c == '{')
            {
                if (depth == 0) objStart = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && objStart >= 0)
                {
                    string obj = json.Substring(objStart, i - objStart + 1);
                    Msg m = new Msg();
                    m.id = GetStr(obj, "\"id\"");
                    m.title = GetStr(obj, "\"title\"");
                    m.body = GetStr(obj, "\"body\"");
                    m.level = GetStr(obj, "\"level\"");
                    m.tag = GetStr(obj, "\"tag\"");
                    m.ts = GetLong(obj, "\"ts\"");
                    if (!string.IsNullOrEmpty(m.id)) list.Add(m);
                    objStart = -1;
                }
            }
            else if (c == ']' && depth == 0) break;
        }
        return list;
    }

    static string GetStr(string obj, string key)
    {
        int k = obj.IndexOf(key);
        if (k < 0) return "";
        int colon = obj.IndexOf(':', k + key.Length);
        if (colon < 0) return "";
        int q1 = obj.IndexOf('"', colon + 1);
        if (q1 < 0) return "";
        StringBuilder sb = null;
        int i = q1 + 1;
        string plain = null;
        // fast path: no escapes
        for (; i < obj.Length; i++)
        {
            char c = obj[i];
            if (c == '\\') break;
            if (c == '"') { plain = obj.Substring(q1 + 1, i - q1 - 1); break; }
        }
        if (plain != null) return plain;
        sb = new StringBuilder(256);
        for (i = q1 + 1; i < obj.Length; i++)
        {
            char c = obj[i];
            if (c == '"') break;
            if (c == '\\' && i + 1 < obj.Length)
            {
                i++;
                char e = obj[i];
                if (e == 'n') sb.Append('\n');
                else if (e == 'r') sb.Append('\r');
                else if (e == 't') sb.Append('\t');
                else if (e == 'b') sb.Append('\b');
                else if (e == 'f') sb.Append('\f');
                else if (e == '/') sb.Append('/');
                else if (e == '"') sb.Append('"');
                else if (e == '\\') sb.Append('\\');
                else if (e == 'u' && i + 4 < obj.Length)
                {
                    try { sb.Append((char)Convert.ToInt32(obj.Substring(i + 1, 4), 16)); i += 4; }
                    catch { sb.Append(e); }
                }
                else sb.Append(e);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    static long GetLong(string obj, string key)
    {
        int k = obj.IndexOf(key);
        if (k < 0) return 0;
        int colon = obj.IndexOf(':', k + key.Length);
        if (colon < 0) return 0;
        int s = colon + 1;
        while (s < obj.Length && (obj[s] == ' ' || obj[s] == '"')) s++;
        int e = s;
        while (e < obj.Length && (char.IsDigit(obj[e]) || obj[e] == '-')) e++;
        long v = 0;
        try { if (e > s) long.TryParse(obj.Substring(s, e - s), out v); } catch { }
        return v;
    }


}
