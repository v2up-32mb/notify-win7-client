using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Lightweight history: append-only TSV in home dir, cap 500 rows.
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
                it.time = p[0]; it.id = p[1]; it.level = p[2]; it.title = p[3]; it.body = p[4];
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
        return s.Replace("\\", "\\\\").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
    }
}
