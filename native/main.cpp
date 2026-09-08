// NotifyClient native (C++ / Win32, no CLR): tray + poll + stacked toasts.
// Single exe, static CRT (/MT), Win7-compatible APIs only.
#include <windows.h>
#include <windowsx.h>
#include <commctrl.h>
#include <shellapi.h>
#include <psapi.h>
#include <strsafe.h>
#include <string>
#include <vector>
#include "resource.h"
#include "cfg.h"
#include "json.h"
#include "poll.h"
#include "toast.h"

enum {
    IDC_URL = 1001, IDC_SECRET, IDC_POLL, IDC_MAX, IDC_STAY,
    IDC_AUTO, IDC_MIN, IDC_SND, IDC_ACLOSE,
    IDC_SAVE, IDC_TEST, IDC_HIDE, IDC_CLEAR,
    IDC_TOASTW, IDC_TOASTH, IDC_TITLEFS, IDC_BODYFS, IDC_MARGIN, IDC_GAP,
    IDC_LOG, IDM_OPEN = 2001, IDM_TEST, IDM_QUIT
};
#define WM_APP_TRAY (WM_APP + 100)
#define TID_MEM 2
#define TRAY_UID 1

static HINSTANCE g_hInst = NULL;
static HWND g_hMain = NULL;
static HWND g_hLog = NULL;
static HWND g_hStatus = NULL;
static HICON g_hTrayIcon = NULL;
static HFONT g_hFont = NULL;
static bool s_dying = false;
static const wchar_t* KCLS = L"NotifyClientNativeMain";

static int ClampI(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

static std::wstring Clock() {
    SYSTEMTIME st;
    GetLocalTime(&st);
    wchar_t b[32];
    StringCchPrintfW(b, 32, L"%02d:%02d:%02d", st.wHour, st.wMinute, st.wSecond);
    return b;
}

static void Status(const wchar_t* s) {
    if (g_hStatus && s) SendMessageW(g_hStatus, SB_SETTEXTW, 0, (LPARAM)s);
}

static void LogAppend(const wchar_t* line) {
    if (!g_hLog || !line) return;
    std::wstring t;
    for (const wchar_t* p = line; *p; ++p) {
        if (*p == L'\n') t += L"\r\n";
        else if (*p != L'\r') t += *p;
    }
    std::wstring full = L"[" + Clock() + L"] " + t + L"\r\n";
    int len = GetWindowTextLengthW(g_hLog);
    SendMessageW(g_hLog, EM_SETSEL, (WPARAM)len, (LPARAM)len);
    SendMessageW(g_hLog, EM_REPLACESEL, FALSE, (LPARAM)full.c_str());
    if (SendMessageW(g_hLog, EM_GETLINECOUNT, 0, 0) > 800) {
        int idx = (int)SendMessageW(g_hLog, EM_LINEINDEX, 300, 0);
        SendMessageW(g_hLog, EM_SETSEL, 0, (LPARAM)idx);
        SendMessageW(g_hLog, EM_REPLACESEL, FALSE, (LPARAM)L"");
    }
    int e2 = GetWindowTextLengthW(g_hLog);
    SendMessageW(g_hLog, EM_SETSEL, (WPARAM)e2, (LPARAM)e2);
    SendMessageW(g_hLog, EM_SCROLLCARET, 0, 0);
}

static std::wstring MemInfo() {
    PROCESS_MEMORY_COUNTERS_EX pm;
    pm.cb = sizeof(pm);
    wchar_t b[128];
    b[0] = 0;
    // cb=sizeof(EX) tells the API to fill the extended fields; the cast
    // satisfies the Win7-level SDK prototype (K32GetProcessMemoryInfo).
    BOOL gmiOk = GetProcessMemoryInfo(GetCurrentProcess(), (PPROCESS_MEMORY_COUNTERS)&pm, sizeof(pm));
    if (gmiOk)
        // SIZE_T is 32-bit in x86 builds: cast before %llu, otherwise printf
        // reads 8 bytes (value + stack garbage) and prints nonsense like 47GB.
        StringCchPrintfW(b, 128, L"内存 工作集%lluMB 私有%lluMB",
            (unsigned long long)(pm.WorkingSetSize / 1048576),
            (unsigned long long)(pm.PrivateUsage / 1048576));
    return b;
}

static void UpdateMemStatus() {
    if (!g_hStatus) return;
    std::wstring m = MemInfo();
    if (!m.empty()) SendMessageW(g_hStatus, SB_SETTEXTW, 1, (LPARAM)m.c_str());
}

static void TrimWorkingSet() {
    EmptyWorkingSet(GetCurrentProcess());
}

static void ShowMain() {
    if (!g_hMain) return;
    ShowWindow(g_hMain, SW_SHOW);
    ShowWindow(g_hMain, SW_RESTORE);
    SetForegroundWindow(g_hMain);
}
static void HideMain() {
    if (!g_hMain) return;
    ShowWindow(g_hMain, SW_HIDE);
    TrimWorkingSet();
}
static void ToggleMain() {
    if (g_hMain && IsWindowVisible(g_hMain)) HideMain();
    else ShowMain();
}

static void ApplyAutoStart(BOOL on) {
    HKEY k = NULL;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run",
            0, KEY_SET_VALUE, &k) != ERROR_SUCCESS)
        return;
    if (on) {
        wchar_t exe[MAX_PATH], cmd[MAX_PATH + 4];
        if (GetModuleFileNameW(NULL, exe, MAX_PATH)) {
            StringCchPrintfW(cmd, MAX_PATH + 4, L"\"%s\"", exe);
            RegSetValueExW(k, L"NotifyClientNative", 0, REG_SZ,
                (const BYTE*)cmd, ((DWORD)wcslen(cmd) + 1) * 2);
        }
    } else {
        RegDeleteValueW(k, L"NotifyClientNative");
    }
    RegCloseKey(k);
}

static void OneLineW(std::wstring& s, size_t mx) {
    for (size_t i = 0; i < s.size(); ++i)
        if (s[i] == L'\r' || s[i] == L'\n') s[i] = L' ';
    while (!s.empty() && s[0] == L' ') s.erase(s.begin());
    while (!s.empty() && s[s.size()-1] == L' ') s.resize(s.size() - 1);
    if (s.size() > mx) { s.resize(mx); s += L"..."; }
}

static void ApplyPoll(PollResult* pr) {
    if (s_dying) return;
    if (!pr->ok) {
        std::wstring s = L"拉取失败 " + Clock() + L" " + NUtf8ToWide(pr->err.substr(0, 120));
        Status(s.c_str());
        LogAppend(s.c_str());
        return;
    }
    if (pr->first) {
        std::wstring s = L"连接正常 " + Clock() + L" (首轮仅同步游标，不打扰)";
        Status(s.c_str());
        if (!pr->latest.empty())
            LogAppend((s + L" 游标=" + NUtf8ToWide(pr->latest)).c_str());
        else
            LogAppend((s + L" 游标=(空，暂无消息)").c_str());
        return;
    }
    if (pr->msgs.empty()) {
        Status((L"无新消息 " + Clock()).c_str());
        if (g_hMain && !IsWindowVisible(g_hMain)) TrimWorkingSet();
        return;
    }
    int n = 0;
    for (size_t i = 0; i < pr->msgs.size(); ++i) {
        const NMsg& m = pr->msgs[i];
        std::wstring t = NUtf8ToWide(m.title.empty() ? "(无标题)" : m.title);
        OneLineW(t, 80);
        std::wstring lv = NUtf8ToWide(m.level.empty() ? "info" : m.level);
        std::wstring bd = NUtf8ToWide(m.body);
        ToastShow(t.c_str(), bd.c_str(), lv.c_str());
        if (g_cfg.sound) MessageBeep(MB_ICONASTERISK);
        // One log line per message: collapse body newlines to spaces.
        // (Full multi-line body stays visible in the toast itself.)
        std::wstring head;
        for (size_t k = 0; k < bd.size(); ++k) {
            wchar_t c = bd[k];
            head += (c == L'\r' || c == L'\n' || c == L'\t') ? L' ' : c;
        }
        if (head.size() > 200) { head.resize(200); head += L"..."; }
        std::wstring line = L"[" + lv + L"] " + t;
        if (!head.empty()) line += L" — " + head;
        LogAppend(line.c_str());
        ++n;
    }
    wchar_t done[64];
    StringCchPrintfW(done, 64, L"收到 %d 条 ", n);
    std::wstring ds = std::wstring(done) + Clock();
    Status(ds.c_str());
    if (g_hMain && !IsWindowVisible(g_hMain)) TrimWorkingSet();
}

static void SaveSettings() {
    wchar_t b[256];
    GetDlgItemTextW(g_hMain, IDC_URL, b, 256);
    std::wstring base = b;
    while (!base.empty() && base[base.size()-1] == L'/') base.resize(base.size() - 1);
    if (base.empty()) base = L"https://notify.ics.de5.net";
    StringCchCopyW(g_cfg.baseUrl, 256, base.c_str());
    GetDlgItemTextW(g_hMain, IDC_SECRET, b, 256);
    StringCchCopyW(g_cfg.secret, 256, b);
    g_cfg.pollSec = ClampI((int)GetDlgItemInt(g_hMain, IDC_POLL, NULL, FALSE), 2, 300);
    g_cfg.maxToasts = ClampI((int)GetDlgItemInt(g_hMain, IDC_MAX, NULL, FALSE), 1, 10);
    g_cfg.staySec = ClampI((int)GetDlgItemInt(g_hMain, IDC_STAY, NULL, FALSE), 3, 300);
    g_cfg.toastW = ClampI((int)GetDlgItemInt(g_hMain, IDC_TOASTW, NULL, FALSE), 200, 600);
    g_cfg.toastH = ClampI((int)GetDlgItemInt(g_hMain, IDC_TOASTH, NULL, FALSE), 80, 300);
    g_cfg.titleFS = ClampI((int)GetDlgItemInt(g_hMain, IDC_TITLEFS, NULL, FALSE), 8, 20);
    g_cfg.bodyFS = ClampI((int)GetDlgItemInt(g_hMain, IDC_BODYFS, NULL, FALSE), 8, 20);
    g_cfg.margin = ClampI((int)GetDlgItemInt(g_hMain, IDC_MARGIN, NULL, FALSE), 0, 64);
    g_cfg.gap = ClampI((int)GetDlgItemInt(g_hMain, IDC_GAP, NULL, FALSE), 0, 64);
    g_cfg.autoStart = IsDlgButtonChecked(g_hMain, IDC_AUTO) == BST_CHECKED;
    g_cfg.startMin = IsDlgButtonChecked(g_hMain, IDC_MIN) == BST_CHECKED;
    g_cfg.sound = IsDlgButtonChecked(g_hMain, IDC_SND) == BST_CHECKED;
    g_cfg.autoClose = IsDlgButtonChecked(g_hMain, IDC_ACLOSE) == BST_CHECKED;
    CfgSave();
    ApplyAutoStart(g_cfg.autoStart);
    ToastReloadFonts();
    wchar_t s[160];
    StringCchPrintfW(s, 160, L"已保存 %s StartMinimized=%d toast=%dx%d",
        Clock().c_str(), g_cfg.startMin ? 1 : 0, g_cfg.toastW, g_cfg.toastH);
    Status(s);
    LogAppend(s);
}

static void TestNotify() {
    ToastShow(L"测试通知", L"托盘客户端工作正常。\n第二行换行测试。", L"info");
    if (g_cfg.sound) MessageBeep(MB_ICONASTERISK);
    std::wstring s = L"已发送测试通知 " + Clock();
    Status(s.c_str());
    LogAppend(L"已发送测试通知（气泡+提示音）。");
}

static void ClearLog() {
    if (g_hLog) SetWindowTextW(g_hLog, L"");
    std::wstring s = L"日志已清空 " + Clock();
    Status(s.c_str());
    LogAppend(L"日志已清空。");
}

static HWND MkCtl(const wchar_t* cls, const wchar_t* txt, DWORD st, int x, int y, int w, int h, int id) {
    HWND c = CreateWindowExW(st & WS_EX_CLIENTEDGE ? WS_EX_CLIENTEDGE : 0,
        cls, txt, WS_CHILD | WS_VISIBLE | WS_TABSTOP | (st & ~WS_EX_CLIENTEDGE),
        x, y, w, h, g_hMain, (HMENU)(INT_PTR)id, g_hInst, NULL);
    if (c && g_hFont) SendMessageW(c, WM_SETFONT, (WPARAM)g_hFont, 0);
    return c;
}

static void BuildControls() {
    int y = 12;
    const int lh = 24;
    MkCtl(L"STATIC", L"服务端 BaseUrl:", 0, 12, y, 496, 16, 0); y += 18;
    HWND h = MkCtl(L"EDIT", g_cfg.baseUrl, ES_AUTOHSCROLL, 12, y, 496, 22, IDC_URL); y += 28;
    (void)h;
    MkCtl(L"STATIC", L"Client Secret (Bearer Token):", 0, 12, y, 496, 16, 0); y += 18;
    MkCtl(L"EDIT", g_cfg.secret, ES_AUTOHSCROLL | ES_PASSWORD, 12, y, 496, 22, IDC_SECRET); y += 28;
    MkCtl(L"STATIC", L"轮询秒数(>=2):", 0, 12, y, 110, lh, 0);
    MkCtl(L"EDIT", L"", ES_NUMBER, 122, y, 60, lh, IDC_POLL);
    SetDlgItemInt(g_hMain, IDC_POLL, (UINT)ClampI(g_cfg.pollSec, 2, 300), FALSE);
    MkCtl(L"STATIC", L"最大堆叠:", 0, 190, y, 70, lh, 0);
    MkCtl(L"EDIT", L"", ES_NUMBER, 260, y, 50, lh, IDC_MAX);
    SetDlgItemInt(g_hMain, IDC_MAX, (UINT)ClampI(g_cfg.maxToasts, 1, 10), FALSE);
    y += 28;
    MkCtl(L"STATIC", L"停留秒数:", 0, 12, y, 110, lh, 0);
    MkCtl(L"EDIT", L"", ES_NUMBER, 122, y, 60, lh, IDC_STAY);
    SetDlgItemInt(g_hMain, IDC_STAY, (UINT)ClampI(g_cfg.staySec, 3, 300), FALSE);
    y += 30;
    MkCtl(L"STATIC", L"气泡宽(200-600):", 0, 12, y, 150, lh, 0);
    MkCtl(L"EDIT", L"", ES_NUMBER, 165, y, 55, lh, IDC_TOASTW);
    SetDlgItemInt(g_hMain, IDC_TOASTW, (UINT)ClampI(g_cfg.toastW, 200, 600), FALSE);
    MkCtl(L"STATIC", L"气泡高(80-300):", 0, 225, y, 150, lh, 0);
    MkCtl(L"EDIT", L"", ES_NUMBER, 378, y, 55, lh, IDC_TOASTH);
    SetDlgItemInt(g_hMain, IDC_TOASTH, (UINT)ClampI(g_cfg.toastH, 80, 300), FALSE);
    y += 28;
    MkCtl(L"STATIC", L"标题字号(8-20):", 0, 12, y, 150, lh, 0);
    MkCtl(L"EDIT", L"", ES_NUMBER, 165, y, 55, lh, IDC_TITLEFS);
    SetDlgItemInt(g_hMain, IDC_TITLEFS, (UINT)ClampI(g_cfg.titleFS, 8, 20), FALSE);
    MkCtl(L"STATIC", L"正文字号(8-20):", 0, 225, y, 150, lh, 0);
    MkCtl(L"EDIT", L"", ES_NUMBER, 378, y, 55, lh, IDC_BODYFS);
    SetDlgItemInt(g_hMain, IDC_BODYFS, (UINT)ClampI(g_cfg.bodyFS, 8, 20), FALSE);
    y += 28;
    MkCtl(L"STATIC", L"边距(0-64):", 0, 12, y, 150, lh, 0);
    MkCtl(L"EDIT", L"", ES_NUMBER, 165, y, 55, lh, IDC_MARGIN);
    SetDlgItemInt(g_hMain, IDC_MARGIN, (UINT)ClampI(g_cfg.margin, 0, 64), FALSE);
    MkCtl(L"STATIC", L"间隔(0-64):", 0, 225, y, 150, lh, 0);
    MkCtl(L"EDIT", L"", ES_NUMBER, 378, y, 55, lh, IDC_GAP);
    SetDlgItemInt(g_hMain, IDC_GAP, (UINT)ClampI(g_cfg.gap, 0, 64), FALSE);
    y += 30;
    MkCtl(L"BUTTON", L"开机启动", BS_AUTOCHECKBOX, 12, y, 150, 22, IDC_AUTO);
    CheckDlgButton(g_hMain, IDC_AUTO, g_cfg.autoStart ? BST_CHECKED : BST_UNCHECKED);
    MkCtl(L"BUTTON", L"启动后自动最小化到托盘", BS_AUTOCHECKBOX, 170, y, 220, 22, IDC_MIN);
    CheckDlgButton(g_hMain, IDC_MIN, g_cfg.startMin ? BST_CHECKED : BST_UNCHECKED);
    y += 28;
    MkCtl(L"BUTTON", L"新消息提示音", BS_AUTOCHECKBOX, 12, y, 150, 22, IDC_SND);
    CheckDlgButton(g_hMain, IDC_SND, g_cfg.sound ? BST_CHECKED : BST_UNCHECKED);
    MkCtl(L"BUTTON", L"Toast超时自动消失", BS_AUTOCHECKBOX, 170, y, 220, 22, IDC_ACLOSE);
    CheckDlgButton(g_hMain, IDC_ACLOSE, g_cfg.autoClose ? BST_CHECKED : BST_UNCHECKED);
    y += 28;
    MkCtl(L"BUTTON", L"保存", BS_PUSHBUTTON, 12, y, 100, 28, IDC_SAVE);
    MkCtl(L"BUTTON", L"测试通知", BS_PUSHBUTTON, 122, y, 100, 28, IDC_TEST);
    MkCtl(L"BUTTON", L"最小化到托盘", BS_PUSHBUTTON, 232, y, 136, 28, IDC_HIDE);
    MkCtl(L"BUTTON", L"清空日志", BS_PUSHBUTTON, 378, y, 130, 28, IDC_CLEAR);
    y += 34;
    MkCtl(L"STATIC", L"消息日志（只读多行，支持换行）：", 0, 12, y, 496, 16, 0); y += 18;
    g_hLog = MkCtl(L"EDIT", L"", ES_MULTILINE | ES_READONLY | WS_VSCROLL | ES_AUTOVSCROLL, 12, y, 496, 150, IDC_LOG);
    y += 156;
    wchar_t pcfg[MAX_PATH + 16];
    StringCchPrintfW(pcfg, MAX_PATH + 16, L"配置：%s", CfgIniPath());
    MkCtl(L"STATIC", pcfg, 0, 12, y, 496, 16, 0);
    RECT rc;
    GetClientRect(g_hMain, &rc);
    g_hStatus = CreateWindowExW(0, STATUSCLASSNAMEW, NULL, WS_CHILD | WS_VISIBLE,
        0, rc.bottom - 22, rc.right, 22, g_hMain, NULL, g_hInst, NULL);
    if (g_hStatus) {
        // Two parts: left status text, right memory readout (~220px).
        int edges[2];
        edges[0] = (rc.right > 220 ? rc.right : 520) - 220;
        edges[1] = -1;
        SendMessageW(g_hStatus, SB_SETPARTS, 2, (LPARAM)edges);
    }
    Status(L"就绪");
}

static void TrayAdd() {
    NOTIFYICONDATAW nid;
    ZeroMemory(&nid, sizeof(nid));
    nid.cbSize = sizeof(nid);
    nid.hWnd = g_hMain;
    nid.uID = TRAY_UID;
    nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    nid.uCallbackMessage = WM_APP_TRAY;
    nid.hIcon = g_hTrayIcon;
    StringCchCopyW(nid.szTip, 128, L"NotifyClient");
    Shell_NotifyIconW(NIM_ADD, &nid);
}

static void TrayMenu() {
    POINT pt;
    GetCursorPos(&pt);
    HMENU hm = CreatePopupMenu();
    if (!hm) return;
    AppendMenuW(hm, MF_STRING, IDM_OPEN, L"打开设置");
    AppendMenuW(hm, MF_STRING, IDM_TEST, L"测试通知");
    AppendMenuW(hm, MF_SEPARATOR, 0, NULL);
    AppendMenuW(hm, MF_STRING, IDM_QUIT, L"退出");
    SetForegroundWindow(g_hMain);
    int cmd = (int)TrackPopupMenuEx(hm, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
        pt.x, pt.y, g_hMain, NULL);
    PostMessageW(g_hMain, WM_NULL, 0, 0);
    DestroyMenu(hm);
    if (cmd == IDM_OPEN) ShowMain();
    else if (cmd == IDM_TEST) TestNotify();
    else if (cmd == IDM_QUIT) DestroyWindow(g_hMain);
}

static LRESULT CALLBACK MainWndProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    switch (m) {
        case WM_CREATE:
            // WM_CREATE fires inside CreateWindowEx, before it returns, so the
            // global is still NULL here: without this every WS_CHILD control
            // gets a NULL parent, creation fails, and the window stays blank.
            g_hMain = h;
            BuildControls();
            return 0;
        case WM_COMMAND: {
            int id = LOWORD(w);
            if (id == IDC_SAVE) SaveSettings();
            else if (id == IDC_TEST) TestNotify();
            else if (id == IDC_HIDE) HideMain();
            else if (id == IDC_CLEAR) ClearLog();
            return 0;
        }
        case WM_SIZE:
            if (w == SIZE_MINIMIZED) HideMain();
            return 0;
        case WM_TIMER:
            if (w == TID_MEM) UpdateMemStatus();
            return 0;
        case WM_CLOSE:
            HideMain(); // X only hides to tray; real quit is the tray menu
            return 0;
        case WM_DESTROY:
            s_dying = true;
            PollStop();
            ToastUnregister();
            {
                NOTIFYICONDATAW nid;
                ZeroMemory(&nid, sizeof(nid));
                nid.cbSize = sizeof(nid);
                nid.hWnd = h;
                nid.uID = TRAY_UID;
                Shell_NotifyIconW(NIM_DELETE, &nid);
            }
            PostQuitMessage(0);
            return 0;
        case WM_APP_TRAY:
            if ((UINT)l == WM_LBUTTONUP) ToggleMain();
            else if ((UINT)l == WM_LBUTTONDBLCLK) ShowMain();
            else if ((UINT)l == WM_RBUTTONUP) TrayMenu();
            return 0;
        case WM_APP_POLL_DONE: {
            PollResult* pr = (PollResult*)l;
            if (pr) { ApplyPoll(pr); delete pr; }
            return 0;
        }
    }
    return DefWindowProcW(h, m, w, l);
}

static HFONT GuiFont() {
    NONCLIENTMETRICSW ncm;
    ncm.cbSize = sizeof(ncm);
    if (SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, sizeof(ncm), &ncm, 0))
        return CreateFontIndirectW(&ncm.lfMessageFont);
    return (HFONT)GetStockObject(DEFAULT_GUI_FONT);
}

int WINAPI wWinMain(HINSTANCE h, HINSTANCE, LPWSTR, int) {
    g_hInst = h;
    HANDLE mtx = CreateMutexW(NULL, FALSE, L"NotifyClientNativeWin7_Mutex");
    if (mtx && GetLastError() == ERROR_ALREADY_EXISTS) {
        HWND f = FindWindowW(KCLS, NULL);
        if (f) { ShowWindow(f, SW_RESTORE); SetForegroundWindow(f); }
        CloseHandle(mtx);
        return 0;
    }
    CfgInitPaths();
    CfgLoad();
    ApplyAutoStart(g_cfg.autoStart);
    INITCOMMONCONTROLSEX ic;
    ic.dwSize = sizeof(ic);
    ic.dwICC = ICC_BAR_CLASSES;
    InitCommonControlsEx(&ic);
    g_hFont = GuiFont();
    ToastInit(h);
    ToastReloadFonts();
    WNDCLASSEXW wc;
    ZeroMemory(&wc, sizeof(wc));
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = MainWndProc;
    wc.hInstance = h;
    wc.hIcon = LoadIconW(h, MAKEINTRESOURCEW(IDI_APPICON));
    if (!wc.hIcon) wc.hIcon = LoadIcon(NULL, IDI_APPLICATION);
    wc.hIconSm = wc.hIcon;
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
    wc.lpszClassName = KCLS;
    if (!RegisterClassExW(&wc)) return 1;
    g_hTrayIcon = wc.hIcon ? wc.hIcon : LoadIcon(NULL, IDI_APPLICATION);
    RECT rc = { 0, 0, 520, 610 };
    AdjustWindowRect(&rc, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX, FALSE);
    int sw = GetSystemMetrics(SM_CXSCREEN);
    int sh = GetSystemMetrics(SM_CYSCREEN);
    int ww = rc.right - rc.left, wh = rc.bottom - rc.top;
    g_hMain = CreateWindowExW(0, KCLS, L"NotifyClient 设置",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_CLIPCHILDREN,
        (sw - ww) / 2, (sh - wh) / 2, ww, wh, NULL, NULL, h, NULL);
    if (!g_hMain) return 1;
    TrayAdd();
    LogAppend((std::wstring(L"启动完成，配置：") + CfgIniPath()).c_str());
    LogAppend((std::wstring(L"StartMinimized=") + (g_cfg.startMin ? L"1" : L"0") +
        L" BaseUrl=" + g_cfg.baseUrl).c_str());
    Status(L"就绪");
    if (!g_cfg.secret[0]) LogAppend(L"尚未配置 Secret，请在上面填写后点保存。");
    if (!g_cfg.startMin) { ShowWindow(g_hMain, SW_SHOW); UpdateWindow(g_hMain); }
    PollStart(g_hMain);
    UpdateMemStatus(); // paint immediately instead of waiting 10s
    SetTimer(g_hMain, TID_MEM, 10000, NULL);
    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    return (int)msg.wParam;
}
