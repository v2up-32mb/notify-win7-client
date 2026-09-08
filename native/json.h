#pragma once
// Minimal JSON scanner: only understands GET /api/messages responses.
// Header-only, no deps. UTF-8 narrow strings; convert at the UI boundary.
#include <windows.h>
#include <string>
#include <vector>

struct NMsg {
    std::string id;
    std::string title;
    std::string body;
    std::string level;
    long long ts;
    NMsg() : ts(0) {}
};

struct NParsed {
    std::string latest;
    std::vector<NMsg> msgs;
};

inline std::wstring NUtf8ToWide(const std::string& s) {
    if (s.empty()) return std::wstring();
    int n = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), NULL, 0);
    if (n <= 0) return std::wstring();
    std::wstring w;
    w.resize((size_t)n);
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), &w[0], n);
    return w;
}

namespace njson {
inline void AppendUtf8(std::string& out, unsigned cp) {
    if (cp < 0x80) out += (char)cp;
    else if (cp < 0x800) {
        out += (char)(0xC0 | (cp >> 6));
        out += (char)(0x80 | (cp & 0x3F));
    } else {
        out += (char)(0xE0 | (cp >> 12));
        out += (char)(0x80 | ((cp >> 6) & 0x3F));
        out += (char)(0x80 | (cp & 0x3F));
    }
}
inline bool GetStr(const std::string& obj, const char* key, std::string& out) {
    out.clear();
    std::string k = std::string("\"") + key + "\"";
    size_t p = obj.find(k);
    if (p == std::string::npos) return false;
    p = obj.find(':', p + k.size());
    if (p == std::string::npos) return false;
    p = obj.find('"', p + 1);
    if (p == std::string::npos) return false;
    for (size_t i = p + 1; i < obj.size(); ++i) {
        char c = obj[i];
        if (c == '"') return true;
        if (c == '\\' && i + 1 < obj.size()) {
            char e = obj[++i];
            switch (e) {
                case 'n': out += '\n'; break;
                case 'r': out += '\r'; break;
                case 't': out += '\t'; break;
                case 'b': out += '\b'; break;
                case 'f': out += '\f'; break;
                case '/': out += '/'; break;
                case '"': out += '"'; break;
                case '\\': out += '\\'; break;
                case 'u': {
                    if (i + 4 >= obj.size()) { out += 'u'; break; }
                    unsigned cp = 0;
                    bool ok = true;
                    for (int k2 = 1; k2 <= 4; ++k2) {
                        char h = obj[i + (size_t)k2];
                        cp <<= 4;
                        if (h >= '0' && h <= '9') cp |= (unsigned)(h - '0');
                        else if (h >= 'a' && h <= 'f') cp |= (unsigned)(h - 'a' + 10);
                        else if (h >= 'A' && h <= 'F') cp |= (unsigned)(h - 'A' + 10);
                        else { ok = false; break; }
                    }
                    if (ok) {
                        if (cp >= 0xD800 && cp <= 0xDFFF) cp = '?';
                        AppendUtf8(out, cp);
                        i += 4;
                    } else out += 'u';
                    break;
                }
                default: out += e; break;
            }
        } else out += c;
    }
    return true;
}
inline long long GetLong(const std::string& obj, const char* key) {
    std::string k = std::string("\"") + key + "\"";
    size_t p = obj.find(k);
    if (p == std::string::npos) return 0;
    p = obj.find(':', p + k.size());
    if (p == std::string::npos) return 0;
    ++p;
    while (p < obj.size() && (obj[p] == ' ' || obj[p] == '"')) ++p;
    bool neg = false;
    if (p < obj.size() && obj[p] == '-') { neg = true; ++p; }
    long long v = 0;
    bool any = false;
    while (p < obj.size() && obj[p] >= '0' && obj[p] <= '9') {
        v = v * 10 + (obj[p] - '0');
        ++p;
        any = true;
    }
    return (neg && any) ? -v : v;
}
} // namespace njson

inline NParsed NJsonParseMessages(const std::string& json) {
    NParsed r;
    if (json.empty()) return r;
    njson::GetStr(json, "latest", r.latest);
    size_t mi = json.find("\"messages\"");
    if (mi == std::string::npos) return r;
    size_t arr = json.find('[', mi);
    if (arr == std::string::npos) return r;
    int depth = 0;
    size_t objStart = 0;
    bool inStr = false, esc = false;
    for (size_t i = arr + 1; i < json.size(); ++i) {
        char c = json[i];
        if (inStr) {
            if (esc) esc = false;
            else if (c == '\\') esc = true;
            else if (c == '"') inStr = false;
            continue;
        }
        if (c == '"') inStr = true;
        else if (c == '{') { if (depth == 0) objStart = i; ++depth; }
        else if (c == '}') {
            --depth;
            if (depth == 0) {
                NMsg m;
                std::string obj = json.substr(objStart, i - objStart + 1);
                njson::GetStr(obj, "id", m.id);
                njson::GetStr(obj, "title", m.title);
                njson::GetStr(obj, "body", m.body);
                njson::GetStr(obj, "level", m.level);
                m.ts = njson::GetLong(obj, "ts");
                if (!m.id.empty()) r.msgs.push_back(m);
            }
        }
        else if (c == ']' && depth == 0) break;
    }
    return r;
}
