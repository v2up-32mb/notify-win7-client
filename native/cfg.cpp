#include "cfg.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strsafe.h>

AppCfg g_cfg;
static wchar_t s_ini[MAX_PATH];
static wchar_t s_cur[MAX_PATH];
static bool s_pathsOk = false;

static int ClampI(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

static void DefCfg() {
    StringCchCopyW(g_cfg.baseUrl, 256, L"https://notify.ics.de5.net");
    g_cfg.secret[0] = 0;
    g_cfg.pollSec = 5; g_cfg.maxToasts = 5; g_cfg.staySec = 10;
    g_cfg.toastW = 320; g_cfg.toastH = 110; g_cfg.titleFS = 10; g_cfg.bodyFS = 9;
    g_cfg.margin = 12; g_cfg.gap = 8;
    g_cfg.autoStart = FALSE; g_cfg.startMin = TRUE; g_cfg.sound = TRUE; g_cfg.autoClose = TRUE;
}

void CfgInitPaths() {
    wchar_t home[MAX_PATH];
    home[0] = 0;
    GetEnvironmentVariableW(L"USERPROFILE", home, MAX_PATH);
    if (!home[0]) {
        wchar_t mod[MAX_PATH];
        if (GetModuleFileNameW(NULL, mod, MAX_PATH)) {
            wchar_t* bs = wcsrchr(mod, L'\\');
            if (bs) *bs = 0;
            StringCchCopyW(home, MAX_PATH, mod);
        }
    }
    wchar_t dir[MAX_PATH];
    StringCchPrintfW(dir, MAX_PATH, L"%s\\.notifyclient", home);
    CreateDirectoryW(dir, NULL);
    StringCchPrintfW(s_ini, MAX_PATH, L"%s\\config.ini", dir);
    StringCchPrintfW(s_cur, MAX_PATH, L"%s\\cursor.txt", dir);
    s_pathsOk = true;
}

const wchar_t* CfgIniPath() { return s_ini; }

static void TrimA(char* s) {
    size_t n = strlen(s);
    while (n && (s[n-1] == '\r' || s[n-1] == '\n' || s[n-1] == ' ' || s[n-1] == '\t')) s[--n] = 0;
    size_t i = 0;
    while (s[i] == ' ' || s[i] == '\t') ++i;
    if (i) memmove(s, s + i, strlen(s + i) + 1);
}
static void LowerA(char* s) {
    for (; *s; ++s) if (*s >= 'A' && *s <= 'Z') *s += 32;
}
static bool IsTrueV(const char* v) { return strcmp(v, "1") == 0 || _stricmp(v, "true") == 0; }
static bool IsFalseV(const char* v) { return strcmp(v, "0") == 0 || _stricmp(v, "false") == 0; }
static void SetW(wchar_t* dst, int cch, const char* utf8) {
    if (!utf8 || !utf8[0]) { dst[0] = 0; return; }
    MultiByteToWideChar(CP_UTF8, 0, utf8, -1, dst, cch);
    dst[cch - 1] = 0;
}

void CfgLoad() {
    DefCfg();
    if (!s_pathsOk) CfgInitPaths();
    char iniA[MAX_PATH * 3];
    WideCharToMultiByte(CP_UTF8, 0, s_ini, -1, iniA, sizeof(iniA), NULL, NULL);
    FILE* f = NULL;
    if (fopen_s(&f, iniA, "r") != 0 || !f) { CfgSave(); return; }
    char line[1024];
    while (fgets(line, sizeof(line), f)) {
        TrimA(line);
        if (!line[0] || line[0] == '#' || line[0] == ';') continue;
        char* eq = strchr(line, '=');
        if (!eq || eq == line) continue;
        *eq = 0;
        char* k = line;
        char* v = eq + 1;
        TrimA(k); TrimA(v);
        LowerA(k);
        if (strcmp(k, "baseurl") == 0) {
            if (v[0]) {
                size_t n = strlen(v);
                while (n && v[n-1] == '/') v[--n] = 0;
                SetW(g_cfg.baseUrl, 256, v);
            }
        }
        else if (strcmp(k, "secret") == 0) SetW(g_cfg.secret, 256, v);
        else if (strcmp(k, "pollseconds") == 0) g_cfg.pollSec = ClampI(atoi(v), 2, 300);
        else if (strcmp(k, "maxtoasts") == 0) g_cfg.maxToasts = ClampI(atoi(v), 1, 10);
        else if (strcmp(k, "stayseconds") == 0) g_cfg.staySec = ClampI(atoi(v), 3, 300);
        else if (strcmp(k, "toastwidth") == 0) g_cfg.toastW = ClampI(atoi(v), 200, 600);
        else if (strcmp(k, "toastheight") == 0) g_cfg.toastH = ClampI(atoi(v), 80, 300);
        else if (strcmp(k, "toasttitlefontsize") == 0 || strcmp(k, "toasttitlefont") == 0)
            g_cfg.titleFS = ClampI(atoi(v), 8, 20);
        else if (strcmp(k, "toastbodyfontsize") == 0 || strcmp(k, "toastbodyfont") == 0)
            g_cfg.bodyFS = ClampI(atoi(v), 8, 20);
        else if (strcmp(k, "toastmargin") == 0) g_cfg.margin = ClampI(atoi(v), 0, 64);
        else if (strcmp(k, "toastgap") == 0) g_cfg.gap = ClampI(atoi(v), 0, 64);
        else if (strcmp(k, "autostart") == 0) g_cfg.autoStart = IsTrueV(v) ? TRUE : FALSE;
        else if (strcmp(k, "startminimized") == 0) g_cfg.startMin = IsFalseV(v) ? FALSE : TRUE;
        else if (strcmp(k, "soundenabled") == 0 || strcmp(k, "sound") == 0)
            g_cfg.sound = IsFalseV(v) ? FALSE : TRUE;
        else if (strcmp(k, "toastautoclose") == 0 || strcmp(k, "autoclose") == 0)
            g_cfg.autoClose = IsFalseV(v) ? FALSE : TRUE;
    }
    fclose(f);
    if (!g_cfg.baseUrl[0]) StringCchCopyW(g_cfg.baseUrl, 256, L"https://notify.ics.de5.net");
}

static void PutW(FILE* f, const char* key, const wchar_t* w) {
    char b[512];
    WideCharToMultiByte(CP_UTF8, 0, w, -1, b, sizeof(b), NULL, NULL);
    fputs(key, f);
    fputs("=", f);
    fputs(b, f);
    fputs("\n", f);
}
static void PutI(FILE* f, const char* key, int v) {
    char b[64];
    sprintf_s(b, "%s=%d\n", key, v);
    fputs(b, f);
}

void CfgSave() {
    if (!s_pathsOk) CfgInitPaths();
    char iniA[MAX_PATH * 3];
    WideCharToMultiByte(CP_UTF8, 0, s_ini, -1, iniA, sizeof(iniA), NULL, NULL);
    FILE* f = NULL;
    if (fopen_s(&f, iniA, "w") != 0 || !f) return;
    fputs("# NotifyClient config (native build, shares file with C# client)\n", f);
    PutW(f, "BaseUrl", g_cfg.baseUrl);
    PutW(f, "Secret", g_cfg.secret);
    PutI(f, "PollSeconds", g_cfg.pollSec);
    PutI(f, "MaxToasts", g_cfg.maxToasts);
    PutI(f, "StaySeconds", g_cfg.staySec);
    PutI(f, "AutoStart", g_cfg.autoStart ? 1 : 0);
    PutI(f, "StartMinimized", g_cfg.startMin ? 1 : 0);
    PutI(f, "SoundEnabled", g_cfg.sound ? 1 : 0);
    PutI(f, "ToastAutoClose", g_cfg.autoClose ? 1 : 0);
    PutI(f, "ToastWidth", g_cfg.toastW);
    PutI(f, "ToastHeight", g_cfg.toastH);
    PutI(f, "ToastTitleFontSize", g_cfg.titleFS);
    PutI(f, "ToastBodyFontSize", g_cfg.bodyFS);
    PutI(f, "ToastMargin", g_cfg.margin);
    PutI(f, "ToastGap", g_cfg.gap);
    fclose(f);
}

bool CursorLoad(wchar_t* out, int cch) {
    if (!s_pathsOk) CfgInitPaths();
    char curA[MAX_PATH * 3];
    WideCharToMultiByte(CP_UTF8, 0, s_cur, -1, curA, sizeof(curA), NULL, NULL);
    FILE* f = NULL;
    if (fopen_s(&f, curA, "r") != 0 || !f) return false;
    char b[256];
    bool ok = (fgets(b, sizeof(b), f) != NULL);
    fclose(f);
    if (!ok) return false;
    TrimA(b);
    SetW(out, cch, b);
    return out[0] != 0;
}

void CursorSave(const wchar_t* id) {
    if (!s_pathsOk) CfgInitPaths();
    char curA[MAX_PATH * 3];
    WideCharToMultiByte(CP_UTF8, 0, s_cur, -1, curA, sizeof(curA), NULL, NULL);
    FILE* f = NULL;
    if (fopen_s(&f, curA, "w") != 0 || !f) return;
    char b[256];
    WideCharToMultiByte(CP_UTF8, 0, id ? id : L"", -1, b, sizeof(b), NULL, NULL);
    fputs(b, f);
    fclose(f);
}
