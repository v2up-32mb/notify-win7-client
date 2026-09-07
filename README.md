# NotifyClient Win7

Lightweight Win7 x86 tray client for notify-center.

- .NET 4.0 Client Profile, single exe, no deps
- Polls GET /api/messages?limit=50&since=cursor with Bearer secret
- Win10-like stacked toasts, close-lower => uppers slide down
- Config NotifyClient.conf: BaseUrl/Secret/PollSeconds/MaxToasts/StaySeconds/AutoStart/StartMinimized
- Cursor file NotifyClient.cursor (since param), first fetch only syncs cursor
- Run key HKCU autostart

Build: GitHub Actions windows-latest msbuild/dotnet, artifact NotifyClient-win7-x86.
