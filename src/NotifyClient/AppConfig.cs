using System;
using System.IO;
using System.Windows.Forms;

// v2: config lives in %USERPROFILE%\.notifyclient\, auto-created. No exe-side files.
static class AppConfig
{
    public static string BaseUrl = "https://notify.ics.de5.net";
    public static string Secret = "";
    public static int PollSeconds = 5;
    public static int MaxToasts = 5;
    public static int StaySeconds = 10;
    public static bool AutoStart = false;
    public static bool StartMinimized = true;
    public static bool SoundEnabled = true;
    public static bool ToastAutoClose = true;

    static string dir;
    static string cfgPath;
    static string cursorPath;

    public static string HomeDir
    {
        get
        {
            if (dir != null) return dir;
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(home))
                    home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                if (string.IsNullOrEmpty(home))
                    home = AppDomain.CurrentDomain.BaseDirectory;
                dir = Path.Combine(home, ".notifyclient");
            }
            catch { dir = AppDomain.CurrentDomain.BaseDirectory; }
            return dir;
        }
    }

    public static string ConfigPath
    {
        get
        {
            if (cfgPath == null) cfgPath = Path.Combine(HomeDir, "config.ini");
            return cfgPath;
        }
    }

    public static string CursorFile
    {
        get
        {
            if (cursorPath == null) cursorPath = Path.Combine(HomeDir, "cursor.txt");
            return cursorPath;
        }
    }

    public static string HistoryFile
    {
        get { return Path.Combine(HomeDir, "history.tsv"); }
    }

    static void EnsureDir()
    {
        try { if (!Directory.Exists(HomeDir)) Directory.CreateDirectory(HomeDir); }
        catch { }
    }

    public static void Load()
    {
        EnsureDir();
        try
        {
            if (!File.Exists(ConfigPath)) { Save(); return; }
            foreach (string raw in File.ReadAllLines(ConfigPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                try
                {
                    if (k == "baseurl") { if (v.Length > 0) BaseUrl = v.TrimEnd('/'); }
                    else if (k == "secret") Secret = v;
                    else if (k == "pollseconds") PollSeconds = Math.Max(2, Math.Min(300, int.Parse(v)));
                    else if (k == "maxtoasts") MaxToasts = Math.Max(1, Math.Min(10, int.Parse(v)));
                    else if (k == "stayseconds") StaySeconds = Math.Max(3, Math.Min(300, int.Parse(v)));
                    else if (k == "autostart") AutoStart = v == "1" || v.ToLower() == "true";
                    else if (k == "startminimized") StartMinimized = !(v == "0" || v.ToLower() == "false");
                    else if (k == "soundenabled" || k == "sound") SoundEnabled = !(v == "0" || v.ToLower() == "false");
                    else if (k == "toastautoclose" || k == "autoclose") ToastAutoClose = !(v == "0" || v.ToLower() == "false");
                }
                catch { }
            }
        }
        catch { }
    }

    public static void Save()
    {
        EnsureDir();
        try
        {
            File.WriteAllLines(ConfigPath, new string[] {
                "# NotifyClient config (auto-generated in home dir)",
                "# dir: " + HomeDir,
                "BaseUrl=" + BaseUrl,
                "Secret=" + Secret,
                "PollSeconds=" + PollSeconds,
                "MaxToasts=" + MaxToasts,
                "StaySeconds=" + StaySeconds,
                "AutoStart=" + (AutoStart ? "1" : "0"),
                "StartMinimized=" + (StartMinimized ? "1" : "0"),
                "SoundEnabled=" + (SoundEnabled ? "1" : "0"),
                "ToastAutoClose=" + (ToastAutoClose ? "1" : "0"),
            });
        }
        catch { }
    }

    public static string LoadCursor()
    {
        try { if (File.Exists(CursorFile)) return File.ReadAllText(CursorFile).Trim(); } catch { }
        return "";
    }

    public static void SaveCursor(string id)
    {
        EnsureDir();
        try { File.WriteAllText(CursorFile, id ?? ""); } catch { }
    }
}
