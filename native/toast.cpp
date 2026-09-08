#include "toast.h"
#include "cfg.h"
#include <vector>
#include <string>
#include <new>

struct Toast {
    HWND hwnd;
    std::wstring title, body, level;
    int ty;          // target top (v2.0.7 lesson: retarget restarts the timer)
    int alpha, alphaT;
    bool closing, fresh;
    int tw, th;
    Toast() : hwnd(NULL), ty(0), alpha(0), alphaT(255),
        closing(false), fresh(true), tw(320), th(110) {}
};

static HINSTANCE s_hInst = NULL;
static ATOM s_cls = 0;
static std::vector<Toast*> s_list;
static HFONT s_fTitle = NULL;
static HFONT s_fBody = NULL;

#define TID_ANIM 1

static int TW() { return (g_cfg.toastW < 200) ? 320 : (g_cfg.toastW > 600 ? 600 : g_cfg.toastW); }
static int TH() { return (g_cfg.toastH < 80) ? 110 : (g_cfg.toastH > 300 ? 300 : g_cfg.toastH); }
static int TM() { return (g_cfg.margin < 0) ? 12 : (g_cfg.margin > 64 ? 64 : g_cfg.margin); }
static int TG() { return (g_cfg.gap < 0) ? 8 : (g_cfg.gap > 64 ? 64 : g_cfg.gap); }
static int TFS() { return (g_cfg.titleFS < 8) ? 10 : (g_cfg.titleFS > 20 ? 20 : g_cfg.titleFS); }
static int BFS() { return (g_cfg.bodyFS < 8) ? 9 : (g_cfg.bodyFS > 20 ? 20 : g_cfg.bodyFS); }

static COLORREF LevelColor(const wchar_t* lv) {
    if (!lstrcmpiW(lv, L"success")) return RGB(0x2e, 0x7d, 0x32);
    if (!lstrcmpiW(lv, L"warn") || !lstrcmpiW(lv, L"warning")) return RGB(0xe6, 0x8a, 0x00);
    if (!lstrcmpiW(lv, L"error") || !lstrcmpiW(lv, L"danger")) return RGB(0xc6, 0x28, 0x28);
    return RGB(0x6E, 0xC1, 0xF5);
}

void ToastReloadFonts() {
    // Old instances intentionally leak (visible toasts may still paint with
    // them); a save costs at most 2 HFONTs, negligible. Same rule as C# port.
    HDC dc = GetDC(NULL);
    int dpi = dc ? GetDeviceCaps(dc, LOGPIXELSY) : 96;
    if (dc) ReleaseDC(NULL, dc);
    HFONT f = CreateFontW(-MulDiv(TFS(), dpi, 72), 0, 0, 0, FW_BOLD,
        FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
        CLIP_DEFAULT_PRECIS, DEFAULT_QUALITY, DEFAULT_PITCH | FF_DONTCARE,
        L"Microsoft YaHei");
    if (f) s_fTitle = f;
    f = CreateFontW(-MulDiv(BFS(), dpi, 72), 0, 0, 0, FW_NORMAL,
        FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
        CLIP_DEFAULT_PRECIS, DEFAULT_QUALITY, DEFAULT_PITCH | FF_DONTCARE,
        L"Microsoft YaHei");
    if (f) s_fBody = f;
}

static void ToastRemove(HWND hwnd) {
    for (size_t i = 0; i < s_list.size(); ++i) {
        if (s_list[i]->hwnd == hwnd) {
            s_list.erase(s_list.begin() + (int)i);
            DestroyWindow(hwnd); // struct freed in WM_NCDESTROY
            break;
        }
    }
    // relayout survivors (slide down to fill the gap)
    RECT wa;
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &wa, 0);
    int W = TW(), H = TH(), M = TM(), G = TG();
    int bottom = wa.bottom - M - H;
    for (size_t i = 0; i < s_list.size(); ++i) {
        Toast* t = s_list[i];
        if (t->closing) continue;
        t->ty = bottom - (int)i * (H + G);
        SetTimer(t->hwnd, TID_ANIM, 20, NULL);
    }
    (void)W;
}

static void Relayout() {
    RECT wa;
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &wa, 0);
    int H = TH(), M = TM(), G = TG();
    int bottom = wa.bottom - M - H;
    for (size_t i = 0; i < s_list.size(); ++i) {
        Toast* t = s_list[i];
        int top = bottom - (int)i * (H + G);
        if (t->fresh) {
            t->fresh = false;
            t->ty = top;
            SetWindowPos(t->hwnd, NULL, wa.right, top, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            SetLayeredWindowAttributes(t->hwnd, 0, 0, LWA_ALPHA);
            t->alpha = 0;
            t->alphaT = 255;
            SetTimer(t->hwnd, TID_ANIM, 20, NULL);
        } else if (!t->closing) {
            t->ty = top;
            SetTimer(t->hwnd, TID_ANIM, 20, NULL); // wake settled toast
        }
    }
}

static void ToastStep(Toast* t) {
    RECT wa;
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &wa, 0);
    int wantX = wa.right - t->tw - 8;
    RECT r;
    GetWindowRect(t->hwnd, &r);
    int dx = wantX - r.left;
    int dy = t->ty - r.top;
    int nx = r.left, ny = r.top;
    if (dx > 24 || dx < -24) nx = r.left + dx / 3; else if (dx) nx = wantX;
    if (dy > 12 || dy < -12) ny = r.top + dy / 3; else if (dy) ny = t->ty;
    int na = t->alpha;
    if (na < t->alphaT) { na += 30; if (na > t->alphaT) na = t->alphaT; }
    else if (na > t->alphaT) { na -= 38; if (na < t->alphaT) na = t->alphaT; }
    t->alpha = na;
    SetWindowPos(t->hwnd, NULL, nx, ny, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    SetLayeredWindowAttributes(t->hwnd, 0, (BYTE)na, LWA_ALPHA);
    if (t->closing && na <= 5) {
        KillTimer(t->hwnd, TID_ANIM);
        ToastRemove(t->hwnd);
        return;
    }
    if (!t->closing && nx == wantX && ny == t->ty && na == 255)
        KillTimer(t->hwnd, TID_ANIM);
}

static void PaintToast(HWND h, Toast* t) {
    if (!t) return;
    PAINTSTRUCT ps;
    HDC dc = BeginPaint(h, &ps);
    RECT rc;
    GetClientRect(h, &rc);
    FillRect(dc, &rc, (HBRUSH)GetStockObject(WHITE_BRUSH));
    HBRUSH bb = CreateSolidBrush(LevelColor(t->level.c_str()));
    if (bb) {
        RECT bar = { 0, 0, 6, rc.bottom };
        FillRect(dc, &bar, bb);
        DeleteObject(bb);
    }
    HPEN pen = CreatePen(PS_SOLID, 1, RGB(0xE0, 0xE0, 0xE0));
    if (pen) {
        HPEN op = (HPEN)SelectObject(dc, pen);
        HGDIOBJ ob = SelectObject(dc, GetStockObject(NULL_BRUSH));
        Rectangle(dc, 0, 0, rc.right, rc.bottom);
        SelectObject(dc, ob);
        SelectObject(dc, op);
        DeleteObject(pen);
    }
    SetBkMode(dc, TRANSPARENT);
    HFONT of = (HFONT)SelectObject(dc, s_fTitle ? s_fTitle : (HFONT)GetStockObject(DEFAULT_GUI_FONT));
    SetTextColor(dc, RGB(0x21, 0x21, 0x21));
    RECT tr = { 14, 8, t->tw - 38, 28 };
    DrawTextW(dc, t->title.c_str(), -1, &tr, DT_SINGLELINE | DT_LEFT | DT_VCENTER | DT_END_ELLIPSIS | DT_NOPREFIX);
    RECT xr = { t->tw - 30, 4, t->tw - 4, 26 };
    SetTextColor(dc, RGB(0x80, 0x80, 0x80));
    DrawTextW(dc, L"x", -1, &xr, DT_SINGLELINE | DT_CENTER | DT_VCENTER | DT_NOPREFIX);
    SelectObject(dc, s_fBody ? s_fBody : (HFONT)GetStockObject(DEFAULT_GUI_FONT));
    SetTextColor(dc, RGB(0x42, 0x42, 0x42));
    RECT br = { 14, 30, t->tw - 14, t->th - 10 };
    if (!t->body.empty())
        DrawTextW(dc, t->body.c_str(), -1, &br, DT_LEFT | DT_TOP | DT_WORDBREAK | DT_EDITCONTROL | DT_NOPREFIX);
    SelectObject(dc, of);
    EndPaint(h, &ps);
}

static LRESULT CALLBACK ToastWndProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    Toast* t = (Toast*)GetWindowLongPtrW(h, GWLP_USERDATA);
    switch (m) {
        case WM_PAINT: PaintToast(h, t); return 0;
        case WM_LBUTTONDOWN:
            if (t && !t->closing) {
                t->closing = true;
                t->alphaT = 0;
                SetTimer(h, TID_ANIM, 20, NULL);
            }
            return 0;
        case WM_TIMER:
            if (w == TID_ANIM && t) ToastStep(t);
            return 0;
        case WM_NCDESTROY:
            delete t;
            SetWindowLongPtrW(h, GWLP_USERDATA, 0);
            break;
    }
    return DefWindowProcW(h, m, w, l);
}

void ToastInit(HINSTANCE h) {
    s_hInst = h;
    if (s_cls) return;
    WNDCLASSEXW wc;
    ZeroMemory(&wc, sizeof(wc));
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = ToastWndProc;
    wc.hInstance = h;
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    wc.lpszClassName = L"NotifyClientNativeToast";
    s_cls = RegisterClassExW(&wc);
}

static void OneLine(std::wstring& s, size_t mx) {
    for (size_t i = 0; i < s.size(); ++i)
        if (s[i] == L'\r' || s[i] == L'\n') s[i] = L' ';
    while (!s.empty() && s[0] == L' ') s.erase(s.begin());
    while (!s.empty() && s[s.size()-1] == L' ') s.resize(s.size() - 1);
    if (s.size() > mx) { s.resize(mx); s += L"..."; }
}

void ToastShow(const wchar_t* title, const wchar_t* body, const wchar_t* level) {
    if (!s_cls) return;
    int max = g_cfg.maxToasts < 1 ? 1 : g_cfg.maxToasts;
    while ((int)s_list.size() >= max) {
        Toast* old = s_list.back();
        s_list.pop_back();
        DestroyWindow(old->hwnd);
    }
    Toast* t = new (std::nothrow) Toast();
    if (!t) return;
    t->title = title ? title : L"";
    OneLine(t->title, 80);
    if (t->title.empty()) t->title = L"(\u65e0\u6807\u9898)";
    t->body = body ? body : L"";
    for (size_t i = 0; i < t->body.size(); ++i)
        if (t->body[i] == L'\r') t->body[i] = L'\n';
    if (t->body.size() > 600) { t->body.resize(600); t->body += L"..."; }
    while (!t->body.empty()) {
        wchar_t c = t->body[t->body.size()-1];
        if (c == L'\n' || c == L' ' || c == L'\t') t->body.resize(t->body.size() - 1);
        else break;
    }
    t->level = level ? level : L"info";
    t->tw = TW();
    t->th = TH();
    t->hwnd = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED,
        (LPCWSTR)MAKEINTATOM(s_cls), L"", WS_POPUP, 0, 0, t->tw, t->th,
        NULL, NULL, s_hInst, t);
    if (!t->hwnd) { delete t; return; }
    ShowWindow(t->hwnd, SW_SHOWNOACTIVATE);
    SetLayeredWindowAttributes(t->hwnd, 0, 0, LWA_ALPHA);
    s_list.insert(s_list.begin(), t);
    Relayout();
}

void ToastUnregister() {
    while (!s_list.empty()) {
        Toast* t = s_list.back();
        s_list.pop_back();
        KillTimer(t->hwnd, TID_ANIM);
        DestroyWindow(t->hwnd); // struct freed in WM_NCDESTROY
    }
    if (s_cls) { UnregisterClassW((LPCWSTR)MAKEINTATOM(s_cls), s_hInst); s_cls = 0; }
}
