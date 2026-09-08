using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Lightweight history: append-only TSV in home dir, cap 500 rows.
// Newlines/tabs are escaped so multi-line message bodies survive a reload:
//   Clean:   \ -> \\, TAB -> \t, CRLF/LF -> \n
//   Unclean: reverse scan (\\ -> \, \n -> newline, \t -> tab).
// Old files (newlines stored as spaces) still load fine.
static class HistoryStore
{
    const int MAX_ROWS = 500;

    public class Item
    {
        public string time, level, title, body, id;
    }

    public static void Append(Msg m)
    {
        try
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                + "\t" + Clean(m.id)
                + "\t" + Clean(m.level)
                + "\t" + Clean(m.title)
                + "\t" + Clean(m.body);
            File.AppendAllText(AppConfig.HistoryFile, line + "\r\n", Encoding.UTF8);
            Trim();
        }
        catch { }
    }

    public static List<Item> Load(int max)
    {
        List<Item> r = new List<Item>();
        try
        {
            if (!File.Exists(AppConfig.HistoryFile)) return r;
            string[] lines = File.ReadAllLines(AppConfig.HistoryFile, Encoding.UTF8);
            int start = Math.Max(0, lines.Length - Math.Max(1, max));
            for (int i = lines.Length - 1; i >= start; i--)
            {
                string[] p = lines[i].Split('\t');
                if (p.Length < 5) continue;
                Item it = new Item();
                it.time = p[0]; it.id = Unclean(p[1]); it.level = Unclean(p[2]); it.title = Unclean(p[3]); it.body = Unclean(p[4]);
                r.Add(it);
            }
        }
        catch { }
        return r;
    }

    public static void Clear()
    {
        try { if (File.Exists(AppConfig.HistoryFile)) File.WriteAllText(AppConfig.HistoryFile, ""); } catch { }
    }

    static void Trim()
    {
        try
        {
            if (!File.Exists(AppConfig.HistoryFile)) return;
            string[] lines = File.ReadAllLines(AppConfig.HistoryFile, Encoding.UTF8);
            if (lines.Length <= MAX_ROWS) return;
            List<string> keep = new List<string>();
            for (int i = lines.Length - MAX_ROWS; i < lines.Length; i++) keep.Add(lines[i]);
            File.WriteAllLines(AppConfig.HistoryFile, keep.ToArray(), Encoding.UTF8);
        }
        catch { }
    }

    static string Clean(string s)
    {
        if (s == null) return "";
        return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n");
    }

    static string Unclean(string s)
    {
        if (s == null) return "";
        StringBuilder sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char e = s[i + 1];
                if (e == '\\') { sb.Append('\\'); i++; }
                else if (e == 'n') { sb.Append('\n'); i++; }
                else if (e == 't') { sb.Append('\t'); i++; }
                else if (e == 'r') { sb.Append('\r'); i++; }
                else { sb.Append(e); i++; }
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
