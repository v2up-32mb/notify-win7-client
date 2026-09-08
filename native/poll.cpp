#include "poll.h"
#include "cfg.h"
#include <winhttp.h>
#include <strsafe.h>
#include <new>

// Win7 ships TLS1.1/1.2 in SChannel but WinHTTP does not enable them by
// default: opt-in explicitly (same story as the C# client's TLS switch).
#ifndef WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_1
#define WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_1 0x00000200
#endif
#ifndef WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2
#define WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2 0x00000800
#endif

static HANDLE s_hThread = NULL;
static HANDLE s_hStop = NULL;
static HWND s_hwnd = NULL;
static std::string s_cursor; // worker-owned
static bool s_first = true;

static std::string LastErr(DWORD e, const char* what) {
    char m[160];
    m[0] = 0;
    FormatMessageA(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        NULL, e, 0, m, 160, NULL);
    size_t n = strlen(m);
    while (n && (m[n-1] == '\r' || m[n-1] == '\n' || m[n-1] == ' ')) m[--n] = 0;
    char o[256];
    StringCchPrintfA(o, 256, "%s: %s (%lu)", what, m, e);
    return o;
}

static std::wstring EscapeQ(const std::wstring& s) {
    std::wstring o;
    for (size_t i = 0; i < s.size(); ++i) {
        wchar_t c = s[i];
        if ((c >= L'0' && c <= L'9') || (c >= L'A' && c <= L'Z') ||
            (c >= L'a' && c <= L'z') || c == L'-' || c == L'_' || c == L'.' || c == L'~')
            o += c;
        else {
            wchar_t h[8];
            StringCchPrintfW(h, 8, L"%%%02X", c);
            o += h;
        }
    }
    return o;
}

static bool FetchOnce(const std::wstring& base, const std::wstring& secret,
                      const std::string& since, std::string& outBody, std::string& err) {
    outBody.clear();
    err.clear();
    std::wstring url = base;
    while (!url.empty() && url[url.size()-1] == L'/') url.resize(url.size() - 1);
    url += L"/api/messages?limit=50";
    if (!since.empty()) {
        url += L"&since=";
        url += EscapeQ(NUtf8ToWide(since));
    }
    URL_COMPONENTS uc;
    ZeroMemory(&uc, sizeof(uc));
    uc.dwStructSize = sizeof(uc);
    uc.dwSchemeLength = uc.dwHostNameLength = uc.dwUrlPathLength = uc.dwExtraInfoLength = (DWORD)-1;
    if (!WinHttpCrackUrl(url.c_str(), 0, 0, &uc)) {
        err = LastErr(GetLastError(), "bad url");
        return false;
    }
    std::wstring host(uc.lpszHostName, uc.dwHostNameLength);
    std::wstring path(uc.lpszUrlPath, uc.dwUrlPathLength);
    if (uc.dwExtraInfoLength) path.append(uc.lpszExtraInfo, uc.dwExtraInfoLength);
    bool secure = (uc.nScheme == INTERNET_SCHEME_HTTPS);
    HINTERNET hS = NULL, hC = NULL, hR = NULL;
    bool ok = false;
    do {
        hS = WinHttpOpen(L"NotifyClientNative-Win7/1.0",
            WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
        if (!hS) { err = LastErr(GetLastError(), "open"); break; }
        WinHttpSetTimeouts(hS, 15000, 15000, 15000, 15000);
        hC = WinHttpConnect(hS, host.c_str(), uc.nPort, 0);
        if (!hC) { err = LastErr(GetLastError(), "connect"); break; }
        LPCWSTR acc[] = { L"application/json", NULL };
        hR = WinHttpOpenRequest(hC, L"GET", path.c_str(), NULL, WINHTTP_NO_REFERER,
            acc, secure ? WINHTTP_FLAG_SECURE : 0);
        if (!hR) { err = LastErr(GetLastError(), "request"); break; }
        if (secure) {
            DWORD prot = WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_1 | WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2;
            WinHttpSetOption(hR, WINHTTP_OPTION_SECURE_PROTOCOLS, &prot, sizeof(prot));
        }
        if (!secret.empty()) {
            std::wstring h = L"Authorization: Bearer " + secret;
            WinHttpAddRequestHeaders(hR, h.c_str(), (DWORD)h.size(), WINHTTP_ADDREQ_FLAG_ADD);
        }
        if (!WinHttpSendRequest(hR, WINHTTP_NO_ADDITIONAL_HEADERS, 0,
                WINHTTP_NO_REQUEST_DATA, 0, 0, 0)) {
            err = LastErr(GetLastError(), "send");
            break;
        }
        if (!WinHttpReceiveResponse(hR, NULL)) {
            err = LastErr(GetLastError(), "recv");
            break;
        }
        DWORD sc = 0;
        DWORD sclen = sizeof(sc);
        WinHttpQueryHeaders(hR, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
            WINHTTP_NO_HEADER_NAME, &sc, &sclen, WINHTTP_NO_HEADER_INDEX);
        for (;;) {
            DWORD avail = 0;
            if (!WinHttpQueryDataAvailable(hR, &avail)) {
                err = LastErr(GetLastError(), "read");
                break;
            }
            if (avail == 0) break;
            std::vector<char> buf((size_t)avail + 1);
            DWORD rd = 0;
            if (!WinHttpReadData(hR, &buf[0], avail, &rd)) {
                err = LastErr(GetLastError(), "read");
                break;
            }
            outBody.append(&buf[0], rd);
        }
        if (!err.empty()) break;
        if (sc != 200) {
            char o[256];
            std::string snip = outBody.substr(0, 120);
            StringCchPrintfA(o, 256, "HTTP %lu %s", sc, snip.c_str());
            err = o;
            break;
        }
        ok = true;
    } while (0);
    if (hR) WinHttpCloseHandle(hR);
    if (hC) WinHttpCloseHandle(hC);
    if (hS) WinHttpCloseHandle(hS);
    return ok;
}

static DWORD WINAPI PollThread(LPVOID) {
    wchar_t w[256];
    if (CursorLoad(w, 256)) {
        char b[256];
        WideCharToMultiByte(CP_UTF8, 0, w, -1, b, sizeof(b), NULL, NULL);
        s_cursor = b;
    }
    for (;;) {
        if (!g_cfg.secret[0]) {
            if (WaitForSingleObject(s_hStop, 2000) == WAIT_OBJECT_0) break;
            continue;
        }
        PollResult* pr = new (std::nothrow) PollResult();
        if (!pr) {
            if (WaitForSingleObject(s_hStop, 5000) == WAIT_OBJECT_0) break;
            continue;
        }
        std::string body, err;
        std::wstring sec(g_cfg.secret); // single copy; save-time races resolve next poll
        if (FetchOnce(g_cfg.baseUrl, sec, s_cursor, body, err)) {
            NParsed p = NJsonParseMessages(body);
            pr->ok = true;
            pr->latest = p.latest;
            pr->msgs.swap(p.msgs);
            if (s_first) { pr->first = true; s_first = false; }
            if (!p.latest.empty()) {
                s_cursor = p.latest;
                wchar_t wl[256];
                MultiByteToWideChar(CP_UTF8, 0, p.latest.c_str(), -1, wl, 256);
                CursorSave(wl);
            }
        } else {
            pr->ok = false;
            pr->err = err;
        }
        if (!PostMessageW(s_hwnd, WM_APP_POLL_DONE, 0, (LPARAM)pr)) delete pr;
        int ms = g_cfg.pollSec * 1000;
        if (ms < 2000) ms = 2000;
        if (WaitForSingleObject(s_hStop, (DWORD)ms) == WAIT_OBJECT_0) break;
    }
    return 0;
}

void PollStart(HWND w) {
    PollStop();
    s_hwnd = w;
    s_first = true;
    s_cursor.clear();
    s_hStop = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (s_hStop) s_hThread = CreateThread(NULL, 0, PollThread, NULL, 0, NULL);
}

void PollStop() {
    if (s_hStop) SetEvent(s_hStop);
    if (s_hThread) {
        WaitForSingleObject(s_hThread, 8000);
        CloseHandle(s_hThread);
        s_hThread = NULL;
    }
    if (s_hStop) { CloseHandle(s_hStop); s_hStop = NULL; }
}
