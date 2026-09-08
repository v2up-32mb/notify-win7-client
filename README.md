# NotifyClient Win7

Lightweight Win7 x86 tray client for notify-center.

- .NET 4.0, single exe, no deps (Actions artifact NotifyClient-win7-x86)
- Polls GET /api/messages?limit=50&since=cursor with Bearer secret
- Win10-like stacked toasts, close-lower => uppers slide down
- Config lives in %USERPROFILE%\.notifyclient\config.ini (auto-created)
- Cursor file cursor.txt (since param), first fetch only syncs cursor
- Run key HKCU autostart

Build: GitHub Actions only (windows-latest msbuild/dotnet). See .github/workflows/build.yml.

## Behavior notes

- Tray icon: embedded exe icon is used first, so a single-exe deployment
  (no loose app.ico next to it) still shows the program icon.
  Main window uses the same icon.
- Tray mouse: single left-click toggles show/hide, double-click forces show.
  Polling runs on a background thread, so showing the window is instant
  even while a 15s HTTP fetch is in flight.
- Startup minimize: StartMinimized=0 is respected. The first show is
  suppressed via SetVisibleCore (no flash), then OnLoad applies
  Hide/Show + first async poll. Requires Application.Run(f).
- Main window has an embedded read-only multi-line log plus a bottom
  status strip. There is no separate history dialog. Recent history
  (last 50) is loaded into the log after the first sync.
- Status line shows short state (ready / saved / fetch fail / got N);
  details go to the log with timestamps.

## Newlines in message text

- Send with real newlines: JSON body "line1\nline2" (or actual \n in API).
  Example: {"title":"t","body":"a\nb","level":"info"}.
- Client chain preserves them: MiniJson decodes \n/\r/\t/\b/\f/\//\uXXXX,
  toast body shows multi-line word-wrapped preview (first 600 chars),
  main log shows the full multi-line body, history file escapes
  newlines as \\n so they survive reload.
- Title is always single-line (newlines become spaces, max 80 chars in toast).
