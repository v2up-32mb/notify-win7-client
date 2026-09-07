using System;
using System.IO;
using System.Windows.Forms;

// Extremely small key=value config, no XML/JSON libs, tiny memory footprint.
static class AppConfig
{
    public static string BaseUrl = "https://notify.ics.de5.net";
    public static string Secret = "";
    public static int PollSeconds = 5;
    public static int MaxToasts = 5;
    public static int StaySeconds = 10;
    public static bool AutoStart = false;
    public static bool StartMinimized = true;

    static string Path
    {
        get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NotifyClient.conf"); }
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(Path)) return;
            foreach (string raw in File.ReadAllLines(Path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                try
                {
                    if (k == "baseurl") { if (v.Length > 0) BaseUrl = v.TrimEnd('/'); }
                    else if (k == "secret") Secret = v;
                    else if (k == "pollseconds") PollSeconds = Math.Max(2, int.Parse(v));
                    else if (k == "maxtoasts") MaxToasts = Math.Max(1, Math.Min(10, int.Parse(v)));
                    else if (k == "stayseconds") StaySeconds = Math.Max(3, Math.Min(120, int.Parse(v)));
                    else if (k == "autostart") AutoStart = v == "1" || v.ToLower() == "true";
                    else if (k == "startminimized") StartMinimized = !(v == "0" || v.ToLower() == "false");
                }
                catch { }
            }
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            File.WriteAllLines(Path, new string[] {
                "# notify-center Win7 client config",
                "BaseUrl=" + BaseUrl,
                "Secret=" + Secret,
                "PollSeconds=" + PollSeconds,
                "MaxToasts=" + MaxToasts,
                "StaySeconds=" + StaySeconds,
                "AutoStart=" + (AutoStart ? "1" : "0"),
                "StartMinimized=" + (StartMinimized ? "1" : "0"),
            });
        }
        catch { }
    }

    static string CursorPath
    {
        get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NotifyClient.cursor"); }
    }

    public static string LoadCursor()
    {
        try { if (File.Exists(CursorPath)) return File.ReadAllText(CursorPath).Trim(); } catch { }
        return "";
    }

    public static void SaveCursor(string id)
    {
        try { File.WriteAllText(CursorPath, id ?? ""); } catch { }
    }
}
