using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

// Tray + config + poll loop. Single hidden form, minimal controls.
class MainForm : Form
{
    NotifyIcon tray;
    ContextMenuStrip menu;
    Timer pollTimer;
    bool firstFetch = true;
    string cursor;
    bool polling = false;

    TextBox txtUrl, txtSecret;
    NumericUpDown numPoll, numMax, numStay;
    CheckBox chkAuto, chkMin, chkSound, chkAutoClose;
    Label lblStatus;
    Button btnSave, btnTest;

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
        pollTimer.Tick += delegate { PollOnce(); };
        pollTimer.Start();
        if (AppConfig.StartMinimized)
        {
            HideWindow();
        }
        else
        {
            ShowWindow();
        }
        // initial fetch in background-ish (sync is fine, fast fail)
        BeginInvoke((MethodInvoker)delegate { PollOnce(); });
    }

    void InitTray()
    {
        menu = new ContextMenuStrip();
        menu.Items.Add("打开设置", null, delegate { ShowWindow(); });
        menu.Items.Add("历史消息", null, delegate { ShowHistory(); });
        menu.Items.Add("测试通知", null, delegate {
            ToastManager.Show("测试通知", "托盘客户端工作正常。", "info");
        });
        menu.Items.Add("退出", null, delegate { Quit(); });
        tray = new NotifyIcon();
        tray.Text = "NotifyClient";
        try
        {
            string ico = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (System.IO.File.Exists(ico)) tray.Icon = new Icon(ico);
            else tray.Icon = SystemIcons.Application;
        }
        catch { tray.Icon = SystemIcons.Application; }
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.DoubleClick += delegate { ShowWindow(); };
    }

    void InitWindow()
    {
        Text = "NotifyClient 设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(380, 400);
        ShowInTaskbar = true;

        int y = 12, lh = 24;
        Label l1 = new Label(); l1.Text = "服务端 BaseUrl:"; l1.SetBounds(12, y, 360, 16); Controls.Add(l1); y += 18;
        txtUrl = new TextBox(); txtUrl.Text = AppConfig.BaseUrl; txtUrl.SetBounds(12, y, 356, 22); Controls.Add(txtUrl); y += 28;
        Label l2 = new Label(); l2.Text = "Client Secret (Bearer Token):"; l2.SetBounds(12, y, 360, 16); Controls.Add(l2); y += 18;
        txtSecret = new TextBox(); txtSecret.Text = AppConfig.Secret; txtSecret.UseSystemPasswordChar = true; txtSecret.SetBounds(12, y, 356, 22); Controls.Add(txtSecret); y += 28;

        Label l3 = new Label(); l3.Text = "轮询秒数(>=2):"; l3.SetBounds(12, y, 110, lh); Controls.Add(l3);
        numPoll = new NumericUpDown(); numPoll.Minimum = 2; numPoll.Maximum = 300; numPoll.Value = Clamp(AppConfig.PollSeconds, 2, 300); numPoll.SetBounds(122, y, 60, lh); Controls.Add(numPoll);
        Label l4 = new Label(); l4.Text = "最大堆叠:"; l4.SetBounds(190, y, 70, lh); Controls.Add(l4);
        numMax = new NumericUpDown(); numMax.Minimum = 1; numMax.Maximum = 10; numMax.Value = Clamp(AppConfig.MaxToasts, 1, 10); numMax.SetBounds(260, y, 50, lh); Controls.Add(numMax);
        y += 28;
        Label l5 = new Label(); l5.Text = "停留秒数:"; l5.SetBounds(12, y, 110, lh); Controls.Add(l5);
        numStay = new NumericUpDown(); numStay.Minimum = 3; numStay.Maximum = 120; numStay.Value = Clamp(AppConfig.StaySeconds, 3, 120); numStay.SetBounds(122, y, 60, lh); Controls.Add(numStay);
        y += 30;

        chkAuto = new CheckBox(); chkAuto.Text = "开机启动"; chkAuto.Checked = AppConfig.AutoStart; chkAuto.SetBounds(12, y, 150, 22); Controls.Add(chkAuto);
        chkMin = new CheckBox(); chkMin.Text = "启动后自动最小化到托盘"; chkMin.Checked = AppConfig.StartMinimized; chkMin.SetBounds(170, y, 200, 22); Controls.Add(chkMin);
        y += 28;
        chkSound = new CheckBox(); chkSound.Text = "新消息提示音"; chkSound.Checked = AppConfig.SoundEnabled; chkSound.SetBounds(12, y, 150, 22); Controls.Add(chkSound);
        chkAutoClose = new CheckBox(); chkAutoClose.Text = "Toast超时自动消失"; chkAutoClose.Checked = AppConfig.ToastAutoClose; chkAutoClose.SetBounds(170, y, 200, 22); Controls.Add(chkAutoClose);
        y += 28;

        btnSave = new Button(); btnSave.Text = "保存"; btnSave.SetBounds(12, y, 100, 28);
        btnSave.Click += delegate { SaveSettings(); };
        Controls.Add(btnSave);
        btnTest = new Button(); btnTest.Text = "测试拉取"; btnTest.SetBounds(122, y, 100, 28);
        btnTest.Click += delegate { PollOnce(true); };
        Controls.Add(btnTest);
        Button btnHide = new Button(); btnHide.Text = "最小化到托盘"; btnHide.SetBounds(232, y, 136, 28);
        btnHide.Click += delegate { HideWindow(); };
        Controls.Add(btnHide);
        y += 34;

        Button btnHist = new Button(); btnHist.Text = "历史消息"; btnHist.SetBounds(12, y, 100, 28);
        btnHist.Click += delegate { ShowHistory(); };
        Controls.Add(btnHist);
        y += 34;

        lblStatus = new Label(); lblStatus.Text = "就绪"; lblStatus.SetBounds(12, y, 356, 40); Controls.Add(lblStatus);

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
        lblStatus.Text = "已保存 " + DateTime.Now.ToString("HH:mm:ss");
    }

    void PollOnce() { PollOnce(false); }

    void PollOnce(bool manual)
    {
        if (polling) return;
        polling = true;
        try
        {
            if (string.IsNullOrEmpty(AppConfig.Secret))
            {
                if (manual) { lblStatus.Text = "请先填写 Secret"; ShowWindow(); }
                return;
            }
            string latest, err;
            List<Msg> msgs = Poller.Fetch(AppConfig.BaseUrl, AppConfig.Secret, cursor, out latest, out err);
            if (err != null)
            {
                lblStatus.Text = "拉取失败 " + DateTime.Now.ToString("HH:mm:ss") + " " + Short(err, 120);
                return;
            }
            if (latest != null && latest.Length > 0) { cursor = latest; AppConfig.SaveCursor(cursor); }
            if (firstFetch)
            {
                firstFetch = false;
                lblStatus.Text = "连接正常 " + DateTime.Now.ToString("HH:mm:ss") + " (首轮仅同步游标，不打扰)";
                return;
            }
            int n = 0;
            foreach (Msg m in msgs)
            {
                string t = string.IsNullOrEmpty(m.title) ? "(无标题)" : m.title;
                string lv = string.IsNullOrEmpty(m.level) ? "info" : m.level;
                string bd = m.body ?? "";
                HistoryStore.Append(m);
                ToastManager.Show(t, bd, lv);
                Sounder.Notify();
                n++;
            }
            lblStatus.Text = (n > 0 ? ("收到 " + n + " 条 ") : "无新消息 ") + DateTime.Now.ToString("HH:mm:ss");
        }
        finally { polling = false; }
    }

    static string Short(string s, int n) { if (s == null) return ""; s = s.Replace("\r", " ").Replace("\n", " "); return s.Length > n ? s.Substring(0, n) + "..." : s; }

    void ShowWindow()
    {
        Show(); WindowState = FormWindowState.Normal; ShowInTaskbar = true; Activate();
    }

    void ShowHistory()
    {
        try
        {
            HistoryForm f = new HistoryForm();
            f.Show();
            try { f.Activate(); } catch { }
        }
        catch { }
    }

    void HideWindow()
    {
        Hide(); ShowInTaskbar = false;
    }

    void Quit()
    {
        tray.Visible = false;
        pollTimer.Stop();
        Application.Exit();
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
