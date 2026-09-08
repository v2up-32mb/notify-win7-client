#pragma once
#include <windows.h>

// Settings shared byte-for-byte with the C# client: same file
// (%USERPROFILE%\.notifyclient\config.ini), same keys. Narrow UTF-8 file IO
// is used on purpose so both clients can read/write it interchangeably.
struct AppCfg {
    wchar_t baseUrl[256];
    wchar_t secret[256];
    int pollSec, maxToasts, staySec;
    int toastW, toastH, titleFS, bodyFS, margin, gap;
    BOOL autoStart, startMin, sound, autoClose;
};
extern AppCfg g_cfg;

void CfgInitPaths();
const wchar_t* CfgIniPath();
void CfgLoad();
void CfgSave();
bool CursorLoad(wchar_t* out, int cch);
void CursorSave(const wchar_t* id);
