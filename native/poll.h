#pragma once
#include <windows.h>
#include <string>
#include <vector>
#include "json.h"

#define WM_APP_POLL_DONE (WM_APP + 101)

struct PollResult {
    bool ok;      // false => transport/auth error, see err
    bool first;   // first successful sync: cursor only, no disturb
    std::string err;
    std::string latest;
    std::vector<NMsg> msgs;
    PollResult() : ok(false), first(false) {}
};

// Worker thread owns the cursor + first-sync flag; UI only renders results.
void PollStart(HWND notifyWnd);
void PollStop();
