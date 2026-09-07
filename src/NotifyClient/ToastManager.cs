using System;
using System.Collections.Generic;
using System.Windows.Forms;

// Bottom-right stacking. Index0 = newest at bottom.
// Closing a lower one slides uppers down via TargetTop animation.
static class ToastManager
{
    static List<ToastForm> list = new List<ToastForm>();
    static object locker = new object();
    public static int MaxCount = 5;
    public static int StaySeconds = 10;
    const int W = 330, H = 96, MARGIN = 12, GAP = 8;

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
            ToastForm f = new ToastForm(title, body, level, StaySeconds);
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
