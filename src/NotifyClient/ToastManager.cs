using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// Bottom-right stacking. Index0 = newest at bottom.
// Closing a lower one slides uppers down via TargetTop animation.
static class ToastManager
{
    static List<ToastForm> list = new List<ToastForm>();
    static object locker = new object();
    public static int MaxCount = 5;
    public static int StaySeconds = 10;
    public static bool AutoClose = true;

    // Layout follows AppConfig live (old hard-coded 330x96 did not even match
    // ToastForm's 320x110, so stacked positions used to drift by a few px).
    static int TW() { try { return Math.Max(200, Math.Min(600, AppConfig.ToastWidth)); } catch { return 320; } }
    static int TH() { try { return Math.Max(80, Math.Min(300, AppConfig.ToastHeight)); } catch { return 110; } }
    static int TM() { try { return Math.Max(0, Math.Min(64, AppConfig.ToastMargin)); } catch { return 12; } }
    static int TG() { try { return Math.Max(0, Math.Min(64, AppConfig.ToastGap)); } catch { return 8; } }

    public static void Show(string title, string body, string level)
    {
        if (Form.ActiveForm != null && Form.ActiveForm.InvokeRequired)
        {
            // called from poll timer on UI thread normally; fallback
            Form.ActiveForm.BeginInvoke((MethodInvoker)delegate { Show(title, body, level); });
            return;
        }
        lock (locker)
        {
            while (list.Count >= Math.Max(1, MaxCount))
            {
                // drop oldest (topmost = last)
                ToastForm old = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                try { old.Close(); old.Dispose(); } catch { }
            }
            ToastForm f = new ToastForm(title, body, level);
            list.Insert(0, f); // newest at index 0 (bottom)
            Relayout();
        }
    }

    public static void Remove(ToastForm f)
    {
        lock (locker)
        {
            list.Remove(f);
            try { f.Dispose(); } catch { }
            Relayout();
        }
    }

    static void Relayout()
    {
        try
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int W = TW(), H = TH(), MARGIN = TM(), GAP = TG();
            int right = wa.Right - MARGIN - W;
            int bottom = wa.Bottom - MARGIN - H;
            for (int i = 0; i < list.Count; i++)
            {
                ToastForm f = list[i];
                int top = bottom - i * (H + GAP);
                int left = right;
                if (!f.Visible)
                    f.SlideIn(left, top);
                else
                    f.TargetTop = top;
            }
        }
        catch { }
    }
}
