#pragma once
#include <windows.h>

void ToastInit(HINSTANCE h);
void ToastReloadFonts(); // rebuild cached fonts from g_cfg (call at startup + on save)
void ToastShow(const wchar_t* title, const wchar_t* body, const wchar_t* level);
void ToastUnregister();  // destroy all (process exit)
