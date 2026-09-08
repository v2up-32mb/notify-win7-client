using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// Tray + config + poll loop. Single form with embedded message log + status strip.
class MainForm : Form
{
    NotifyIcon tray;
    ContextMenuStrip menu;
    Timer pollTimer;
    bool firstFetch = true;
    string cursor;
    volatile bool polling = false;
    bool startupDone = false;

    TextBox txtUrl, txtSecret;
    NumericUpDown numPoll, numMax, numStay;
    CheckBox chkAuto, chkMin, chkSound, chkAutoClose;
    TextBox txtLog;
    StatusStrip statusStrip;
    ToolStripStatusLabel statusLabel;
    Button btnSave, btnTest;

    static Icon appIconCache = null;

    public MainForm()
    {
        cursor = AppConfig.LoadCursor();
        ToastManager.MaxCount = AppConfig.MaxToasts;
        ToastManager.StaySeconds = AppConfig.StaySeconds;
        ToastManager.AutoClose = AppConfig.ToastAutoClose;
        InitTray();
        InitWindow();
        ApplyAutoStart(AppConfig.AutoStart);
        pollTimer = new Timer();
        pollTimer.Interval = Math.Max(2, AppConfig.PollSeconds) * 1000;
        pollTimer.Tick += delegate { PollOnceAsync(false); };
        pollTimer.Start();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!startupDone && AppConfig.StartMinimized) value = false;
        base.SetVisibleCore(value);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            AppendLog("\u542f\u52a8\u5b8c\u6210\uff0c\u914d\u7f6e\uff1a" + AppConfig.ConfigPath);
            AppendLog("StartMinimized=" + (AppConfig.StartMinimized ? "1" : "0") + " BaseUrl=" + AppConfig.BaseUrl);
            SetStatus("\u5c31\u7eea");
        }
        catch { }
        try
        {
            BeginInvoke((MethodInvoker)delegate
            {
                if (startupDone) return;
                startupDone = true;
                try
                {
                    if (AppConfig.StartMinimized) HideWindow();
                    else ShowWindow();
                }
                catch { }
                try { PollOnceAsync(false); }
                catch { }
            });
        }
        catch { }
    }

    void InitTray()
    {
        menu = new ContextMenuStrip();
        menu.Items.Add("\u6253\u5f00\u8bbe\u7f6e", null, delegate { ShowWindow(); });
        menu.Items.Add("\u6d4b\u8bd5\u901a\u77e5", null, delegate {
            ToastManager.Show("\u6d4b\u8bd5\u901a\u77e5", "\u6258\u76d8\u5ba2\u6237\u7aef\u5de5\u4f5c\u6b63\u5e38\u3002\n\u7b2c\u4e8c\u884c\u6362\u884c\u6d4b\u8bd5\u3002", "info");
        });
        menu.Items.Add("\u9000\u51fa", null, delegate { Quit(); });
        tray = new NotifyIcon();
        tray.Text = "NotifyClient";
        try { tray.Icon = LoadAppIcon(); }
        catch { try { tray.Icon = SystemIcons.Application; } catch { } }
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        try
        {
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                try { if (e.Button == MouseButtons.Left) ToggleWindow(); }
                catch { }
            };
        }
        catch { }
        tray.DoubleClick += delegate { try { ShowWindow(); } catch { } };
    }

    static Icon LoadAppIcon()
    {
        if (appIconCache != null) return appIconCache;
        try
        {
            string ico = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (System.IO.File.Exists(ico)) { appIconCache = new Icon(ico); return appIconCache; }
        }
        catch { }
        try
        {
            Icon exe = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (exe != null)
            {
                try { appIconCache = (Icon)exe.Clone(); }
                catch { appIconCache = exe; }
                try { if (appIconCache != exe) exe.Dispose(); } catch { }
                return appIconCache;
            }
        }
        catch { }
        try { appIconCache = SystemIcons.Application; }
        catch { }
        return appIconCache;
    }

    void InitWindow()
    {
        Text = "NotifyClient \u8bbe\u7f6e";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 540);
        ShowInTaskbar = true;
        try { Icon = LoadAppIcon(); } catch { }
        int y = 12, lh = 24;
        Label l1 = new Label(); l1.Text = "\u670d\u52a1\u7aef BaseUrl:"; l1.SetBounds(12, y, 496, 16); Controls.Add(l1); y += 18;
        txtUrl = new TextBox(); txtUrl.Text = AppConfig.BaseUrl; txtUrl.SetBounds(12, y, 496, 22); Controls.Add(txtUrl); y += 28;
        Label l2 = new Label(); l2.Text = "Client Secret (Bearer Token):"; l2.SetBounds(12, y, 496, 16); Controls.Add(l2); y += 18;
        txtSecret = new TextBox(); txtSecret.Text = AppConfig.Secret; txtSecret.UseSystemPasswordChar = true; txtSecret.SetBounds(12, y, 496, 22); Controls.Add(txtSecret); y += 28;
        Label l3 = new Label(); l3.Text = "\u8f6e\u8be2\u79d2\u6570(>=2):"; l3.SetBounds(12, y, 110, lh); Controls.Add(l3);
        numPoll = new NumericUpDown(); numPoll.Minimum = 2; numPoll.Maximum = 300; numPoll.Value = Clamp(AppConfig.PollSeconds, 2, 300); numPoll.SetBounds(122, y, 60, lh); Controls.Add(numPoll);
        Label l4 = new Label(); l4.Text = "\u6700\u5927\u5806\u53e0:"; l4.SetBounds(190, y, 70, lh); Controls.Add(l4);
        numMax = new NumericUpDown(); numMax.Minimum = 1; numMax.Maximum = 10; numMax.Value = Clamp(AppConfig.MaxToasts, 1, 10); numMax.SetBounds(260, y, 50, lh); Controls.Add(numMax);
        y += 28;
        Label l5 = new Label(); l5.Text = "\u505c\u7559\u79d2\u6570:"; l5.SetBounds(12, y, 110, lh); Controls.Add(l5);
        numStay = new NumericUpDown(); numStay.Minimum = 3; numStay.Maximum = 120; numStay.Value = Clamp(AppConfig.StaySeconds, 3, 120); numStay.SetBounds(122, y, 60, lh); Controls.Add(numStay);
        y += 30;
        chkAuto = new CheckBox(); chkAuto.Text = "\u5f00\u673a\u542f\u52a8"; chkAuto.Checked = AppConfig.AutoStart; chkAuto.SetBounds(12, y, 150, 22); Controls.Add(chkAuto);
        chkMin = new CheckBox(); chkMin.Text = "\u542f\u52a8\u540e\u81ea\u52a8\u6700\u5c0f\u5316\u5230\u6258\u76d8"; chkMin.Checked = AppConfig.StartMinimized; chkMin.SetBounds(170, y, 220, 22); Controls.Add(chkMin);
        y += 28;
        chkSound = new CheckBox(); chkSound.Text = "\u65b0\u6d88\u606f\u63d0\u793a\u97f3"; chkSound.Checked = AppConfig.SoundEnabled; chkSound.SetBounds(12, y, 150, 22); Controls.Add(chkSound);
        chkAutoClose = new CheckBox(); chkAutoClose.Text = "Toast\u8d85\u65f6\u81ea\u52a8\u6d88\u5931"; chkAutoClose.Checked = AppConfig.ToastAutoClose; chkAutoClose.SetBounds(170, y, 220, 22); Controls.Add(chkAutoClose);
        y += 28;
        btnSave = new Button(); btnSave.Text = "\u4fdd\u5b58"; btnSave.SetBounds(12, y, 100, 28);
        btnSave.Click += delegate { SaveSettings(); };
        Controls.Add(btnSave);
        btnTest = new Button(); btnTest.Text = "\u6d4b\u8bd5\u62c9\u53d6"; btnTest.SetBounds(122, y, 100, 28);
        btnTest.Click += delegate { PollOnceAsync(true); };
        Controls.Add(btnTest);
        Button btnHide = new Button(); btnHide.Text = "\u6700\u5c0f\u5316\u5230\u6258\u76d8"; btnHide.SetBounds(232, y, 136, 28);
        btnHide.Click += delegate { HideWindow(); };
        Controls.Add(btnHide);
        Button btnClear = new Button(); btnClear.Text = "\u6e05\u7a7a\u65e5\u5fd7"; btnClear.SetBounds(378, y, 130, 28);
        btnClear.Click += delegate { ClearLog(); };
        Controls.Add(btnClear);
        y += 34;
        Label lLog = new Label(); lLog.Text = "\u6d88\u606f\u65e5\u5fd7\uff08\u53ea\u8bfb\u591a\u884c\uff0c\u652f\u6301\u6362\u884c\uff09\uff1a"; lLog.SetBounds(12, y, 496, 16); Controls.Add(lLog); y += 18;
        txtLog = new TextBox();
        txtLog.Multiline = true;
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.WordWrap = true;
        txtLog.SetBounds(12, y, 496, 168);
        Controls.Add(txtLog); y += 174;
        Label lblPath = new Label();
        lblPath.Text = "\u914d\u7f6e\uff1a" + AppConfig.ConfigPath;
        lblPath.ForeColor = Color.Gray;
        lblPath.SetBounds(12, y, 496, 16);
        Controls.Add(lblPath);
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel();
        statusLabel.Text = "\u5c31\u7eea";
        statusLabel.Spring = true;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusStrip.Items.Add(statusLabel);
        statusStrip.Dock = DockStyle.Bottom;
        Controls.Add(statusStrip);
        FormClosing += delegate(object s, FormClosingEventArgs e) {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideWindow(); }
        };
        Resize += delegate { if (WindowState == FormWindowState.Minimized) HideWindow(); };
    }

    static decimal Clamp(int v, int lo, int hi) { if (v < lo) return lo; if (v > hi) return hi; return v; }

    void SaveSettings()
    {
        AppConfig.BaseUrl = txtUrl.Text.Trim().TrimEnd('/');
        if (AppConfig.BaseUrl.Length == 0) AppConfig.BaseUrl = "https://notify.ics.de5.net";
        AppConfig.Secret = txtSecret.Text.Trim();
        AppConfig.PollSeconds = (int)numPoll.Value;
        AppConfig.MaxToasts = (int)numMax.Value;
        AppConfig.StaySeconds = (int)numStay.Value;
        AppConfig.AutoStart = chkAuto.Checked;
        AppConfig.StartMinimized = chkMin.Checked;
        AppConfig.SoundEnabled = chkSound.Checked;
        AppConfig.ToastAutoClose = chkAutoClose.Checked;
        AppConfig.Save();
        ApplyAutoStart(AppConfig.AutoStart);
        pollTimer.Interval = AppConfig.PollSeconds * 1000;
        ToastManager.MaxCount = AppConfig.MaxToasts;
        ToastManager.StaySeconds = AppConfig.StaySeconds;
        ToastManager.AutoClose = AppConfig.ToastAutoClose;
        string s = "\u5df2\u4fdd\u5b58 " + DateTime.Now.ToString("HH:mm:ss") + " StartMinimized=" + (AppConfig.StartMinimized ? "1" : "0");
        SetStatus(s);
        AppendLog(s);
    }

    void ClearLog()
    {
        try
        {
            if (txtLog.InvokeRequired) txtLog.BeginInvoke((MethodInvoker)delegate { ClearLog(); });
            else txtLog.Clear();
        }
        catch { }
        SetStatus("\u65e5\u5fd7\u5df2\u6e05\u7a7a " + DateTime.Now.ToString("HH:mm:ss"));
        AppendLog("\u65e5\u5fd7\u5df2\u6e05\u7a7a\u3002");
    }

    void PollOnce() { PollOnceAsync(false); }

    void PollOnceAsync(bool manual)
    {
        if (polling) return;
        if (string.IsNullOrEmpty(AppConfig.Secret))
        {
            if (manual) { SetStatus("\u8bf7\u5148\u586b\u5199 Secret"); AppendLog("\u8bf7\u5148\u586b\u5199 Secret"); ShowWindow(); }
            else SetStatus("\u672a\u914d\u7f6e Secret\uff0c\u7b49\u5f85\u586b\u5199");
            return;
        }
        polling = true;
        string cur = cursor;
        string baseUrl = AppConfig.BaseUrl;
        string secret = AppConfig.Secret;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate(object _) {
            string latest = null, err = null;
            List<Msg> msgs = new List<Msg>();
            try { msgs = Poller.Fetch(baseUrl, secret, cur, out latest, out err); }
            catch (Exception ex) { err = ex.Message; }
            try
            {
                if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate { OnPollCompleted(msgs, latest, err, manual); });
                else polling = false;
            }
            catch { try { polling = false; } catch { } }
        });
    }

    void OnPollCompleted(List<Msg> msgs, string latest, string err, bool manual)
    {
        polling = false;
        try
        {
            if (err != null)
            {
                string s = "\u62c9\u53d6\u5931\u8d25 " + DateTime.Now.ToString("HH:mm:ss") + " " + Short(err, 120);
                SetStatus(s);
                AppendLog(s);
                return;
            }
            if (latest != null && latest.Length > 0) { cursor = latest; AppConfig.SaveCursor(cursor); }
            if (firstFetch)
            {
                firstFetch = false;
                string s = "\u8fde\u63a5\u6b63\u5e38 " + DateTime.Now.ToString("HH:mm:ss") + " (\u9996\u8f6e\u4ec5\u540c\u6b65\u6e38\u6807\uff0c\u4e0d\u6253\u6270)";
                SetStatus(s);
                AppendLog(s + " cursor=" + cursor);
                AppendLog(MemInfo());
                return;
            }
            if (msgs == null || msgs.Count == 0)
            {
                SetStatus("\u65e0\u65b0\u6d88\u606f " + DateTime.Now.ToString("HH:mm:ss"));
                try { if (!Visible || !ShowInTaskbar) TrimWorkingSet(); } catch { }
                return;
            }
            int n = 0;
            foreach (Msg m in msgs)
            {
                string t = string.IsNullOrEmpty(m.title) ? "(\u65e0\u6807\u9898)" : m.title;
                string lv = string.IsNullOrEmpty(m.level) ? "info" : m.level;
                string bd = m.body ?? "";
                AppendLog(FormatMsgForLog(m));
                try { ToastManager.Show(SingleLine(t), bd, lv); } catch { }
                try { Sounder.Notify(); } catch { }
                n++;
            }
            SetStatus("\u6536\u5230 " + n + " \u6761 " + DateTime.Now.ToString("HH:mm:ss"));
            try { if (!Visible || !ShowInTaskbar) TrimWorkingSet(); } catch { }
        }
        catch (Exception ex)
        {
            try { SetStatus("\u62c9\u53d6\u5904\u7406\u5f02\u5e38 " + ex.Message); AppendLog("\u62c9\u53d6\u5904\u7406\u5f02\u5e38: " + ex); } catch { }
        }
    }

    static string SingleLine(string s)
    {
        if (s == null) return "";
        s = s.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
        return s.Trim();
    }

    static string FormatMsgForLog(Msg m)
    {
        string lv = string.IsNullOrEmpty(m.level) ? "info" : m.level;
        string t = SingleLine(string.IsNullOrEmpty(m.title) ? "(\u65e0\u6807\u9898)" : m.title);
        string bd = (m.body ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
        if (bd.Length > 2000) bd = bd.Substring(0, 2000) + "...";
        if (bd.Length > 0) return "[" + lv + "] " + t + "\r\n" + bd.Replace("\n", "\r\n");
        return "[" + lv + "] " + t;
    }

    static string Short(string s, int n) { if (s == null) return ""; s = s.Replace("\r", " ").Replace("\n", " "); return s.Length > n ? s.Substring(0, n) + "..." : s; }

    void ToggleWindow()
    {
        try
        {
            if (Visible && WindowState != FormWindowState.Minimized && ShowInTaskbar) HideWindow();
            else ShowWindow();
        }
        catch { try { ShowWindow(); } catch { } }
    }

    void ShowWindow()
    {
        try { Show(); WindowState = FormWindowState.Normal; ShowInTaskbar = true; Activate(); }
        catch { try { Show(); } catch { } }
    }

    void HideWindow() { try { Hide(); ShowInTaskbar = false; } catch { } try { TrimWorkingSet(); } catch { } }

    void Quit()
    {
        try { tray.Visible = false; } catch { }
        try { pollTimer.Stop(); } catch { }
        Application.Exit();
    }

    void AppendLog(string line)
    {
        try
        {
            if (txtLog == null) return;
            if (txtLog.InvokeRequired) { try { txtLog.BeginInvoke((MethodInvoker)delegate { AppendLog(line); }); } catch { } return; }
            string text = (line ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + "\r\n");
            try
            {
                if (txtLog.Lines.Length > 800)
                {
                    string[] lines = txtLog.Lines;
                    int keep = 500;
                    string[] nk = new string[keep];
                    Array.Copy(lines, lines.Length - keep, nk, 0, keep);
                    txtLog.Lines = nk;
                }
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
            catch { }
        }
        catch { }
    }

    void SetStatus(string s)
    {
        try
        {
            string t = s ?? "";
            if (statusLabel == null) return;
            if (statusStrip.InvokeRequired) statusStrip.BeginInvoke((MethodInvoker)delegate { statusLabel.Text = t; });
            else statusLabel.Text = t;
        }
        catch { }
    }

    [DllImport("psapi.dll")]
    static extern bool EmptyWorkingSet(IntPtr hProcess);

    static void TrimWorkingSet()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect(0, GCCollectionMode.Optimized);
            try { EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle); }
            catch { }
        }
        catch { }
    }

    static string MemInfo()
    {
        try
        {
            var p = System.Diagnostics.Process.GetCurrentProcess();
            long ws = p.WorkingSet64 / 1024 / 1024;
            long pv = p.PrivateMemorySize64 / 1024 / 1024;
            long gc = GC.GetTotalMemory(false) / 1024;
            return "\u5185\u5b58 \u5de5\u4f5c\u96c6" + ws + "MB \u79c1\u6709" + pv + "MB \u6258\u7ba1" + gc + "KB";
        }
        catch { return ""; }
    }

    static void ApplyAutoStart(bool on)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
            {
                if (k == null) return;
                if (on) k.SetValue("NotifyClient", "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue("NotifyClient", false);
            }
        }
        catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (tray != null) tray.Dispose();
            if (menu != null) menu.Dispose();
            if (pollTimer != null) pollTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
