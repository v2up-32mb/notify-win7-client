using System;
using System.Drawing;
using System.Windows.Forms;

// Win10-like toast, borderless, topmost, no focus steal, fade+slide anim.
// v2: honors ToastManager.StaySeconds + ToastManager.AutoClose.
// Body supports multi-line (\n) with word-wrap via read-only TextBox.
class ToastForm : Form
{
    string title, body, level;
    Label lblTitle;
    TextBox txtBody;
    Button btnX;
    Panel bar;
    Timer lifeTimer, animTimer;
    int stayMs;
    int targetTop = -1;
    double opacityTarget = 1.0;
    bool closing = false;

    public int TargetTop { get { return targetTop; } set { targetTop = value; } }

    // Cached fonts rebuilt from AppConfig (see ReloadSettings): one toast used to
    // allocate 2 Fonts, with many messages GDI/font handles piled up.
    static Font titleFont;
    static Font bodyFont;
    int tw, th;

    // Old cached instances are intentionally not disposed: visible toasts may
    // still paint with them; a save leaks at most 2 font handles, negligible.
    public static void ReloadSettings()
    {
        try { titleFont = new Font(FontFamily.GenericSansSerif, Math.Max(8, Math.Min(20, AppConfig.ToastTitleFontSize)), FontStyle.Bold); }
        catch { }
        try { bodyFont = new Font(FontFamily.GenericSansSerif, Math.Max(8, Math.Min(20, AppConfig.ToastBodyFontSize)), FontStyle.Regular); }
        catch { }
    }

    public ToastForm(string t, string b, string lv)
    {
        title = SingleLine(t ?? "", 80);
        body = ClipBody(b ?? "");
        level = lv ?? "info";
        tw = Math.Max(200, Math.Min(600, AppConfig.ToastWidth));
        th = Math.Max(80, Math.Min(300, AppConfig.ToastHeight));
        if (titleFont == null || bodyFont == null) ReloadSettings();
        stayMs = Math.Max(3, ToastManager.StaySeconds) * 1000;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(tw, th);
        BackColor = Color.White;

        bar = new Panel();
        bar.BackColor = LevelColor(level);
        bar.SetBounds(0, 0, 6, th);
        Controls.Add(bar);

        lblTitle = new Label();
        lblTitle.Text = title;
        lblTitle.Font = titleFont ?? this.Font;
        lblTitle.ForeColor = Color.FromArgb(0x21, 0x21, 0x21);
        lblTitle.AutoSize = false;
        lblTitle.SetBounds(14, 8, tw - 52, 20);
        Controls.Add(lblTitle);

        txtBody = new TextBox();
        txtBody.Text = body.Replace("\n", "\r\n");
        txtBody.Font = bodyFont ?? this.Font;
        txtBody.ForeColor = Color.FromArgb(0x42, 0x42, 0x42);
        txtBody.BackColor = Color.White;
        txtBody.BorderStyle = BorderStyle.None;
        txtBody.Multiline = true;
        txtBody.ReadOnly = true;
        txtBody.WordWrap = true;
        txtBody.ScrollBars = ScrollBars.None;
        txtBody.TabStop = false;
        txtBody.Cursor = Cursors.Default;
        txtBody.SetBounds(14, 30, tw - 28, th - 44);
        txtBody.Click += delegate { BeginClose(); };
        Controls.Add(txtBody);

        btnX = new Button();
        btnX.Text = "x";
        btnX.FlatStyle = FlatStyle.Flat;
        btnX.FlatAppearance.BorderSize = 0;
        btnX.ForeColor = Color.Gray;
        btnX.SetBounds(tw - 30, 4, 26, 22);
        btnX.Click += delegate { BeginClose(); };
        Controls.Add(btnX);

        if (ToastManager.AutoClose)
        {
            lifeTimer = new Timer();
            lifeTimer.Interval = stayMs;
            lifeTimer.Tick += delegate { lifeTimer.Stop(); BeginClose(); };
        }

        animTimer = new Timer();
        animTimer.Interval = 20;
        animTimer.Tick += delegate { OnAnim(); };

        // rounded feel via region-less border paint
        Paint += delegate(object s, PaintEventArgs e)
        {
            try
            {
                ControlPaint.DrawBorder(e.Graphics, ClientRectangle, Color.FromArgb(0xE0, 0xE0, 0xE0), ButtonBorderStyle.Solid);
            }
            catch { }
        };
        Click += delegate { BeginClose(); };
    }

    static string SingleLine(string s, int max)
    {
        if (s == null) return "";
        s = s.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
        s = s.Trim();
        if (s.Length > max) s = s.Substring(0, max) + "...";
        return s;
    }

    static string ClipBody(string s)
    {
        if (s == null) return "";
        s = s.Replace("\r\n", "\n").Replace("\r", "\n");
        // Toast shows a preview; full text lives in the main-window log.
        if (s.Length > 600) s = s.Substring(0, 600) + "...";
        // Trim trailing blank lines.
        s = s.TrimEnd('\n', ' ', '\t');
        return s;
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams p = base.CreateParams;
            p.ExStyle |= 0x00000008; // WS_EX_TOPMOST
            p.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            p.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW (hide from alt-tab)
            return p;
        }
    }

    public void SlideIn(int left, int top)
    {
        try
        {
            Rectangle wa = Screen.GetWorkingArea(new Point(left, top));
            if (left + tw > wa.Right) left = wa.Right - tw - 8;
            if (top + th > wa.Bottom) top = wa.Bottom - th - 8;
            // slide from right
            Location = new Point(wa.Right, top);
            targetTop = top;
            Opacity = 0;
            opacityTarget = 1.0;
            Show();
            animTimer.Start();
            if (lifeTimer != null) lifeTimer.Start();
        }
        catch { try { Show(); } catch { } }
    }

    public void BeginClose()
    {
        if (closing) return;
        closing = true;
        opacityTarget = 0.0;
        try { if (lifeTimer != null) lifeTimer.Stop(); } catch { }
        animTimer.Start();
    }

    void OnAnim()
    {
        try
        {
            // slide X toward target left + fade
            int wantLeft = targetTop < 0 ? Left : (Screen.GetWorkingArea(Location).Right - this.Width - 8);
            int dx = wantLeft - Left;
            if (Math.Abs(dx) > 24) Left += dx / 3;
            else if (dx != 0) Left = wantLeft;
            if (targetTop >= 0 && Top != targetTop)
            {
                int dy = targetTop - Top;
                if (Math.Abs(dy) > 12) Top += dy / 3;
                else Top = targetTop;
            }
            double op = Opacity;
            if (op < opacityTarget) op = Math.Min(opacityTarget, op + 0.12);
            else if (op > opacityTarget) op = Math.Max(opacityTarget, op - 0.15);
            Opacity = op;
            if (closing && Opacity <= 0.01)
            {
                animTimer.Stop();
                try { Hide(); } catch { }
                ToastManager.Remove(this);
                try { Dispose(); } catch { }
                return;
            }
            if (!closing && Left == wantLeft && (targetTop < 0 || Top == targetTop) && Math.Abs(Opacity - 1.0) < 0.01)
            {
                Opacity = 1.0;
                animTimer.Stop();
            }
        }
        catch { }
    }

    static Color LevelColor(string lv)
    {
        if (lv == "success") return Color.FromArgb(0x2e, 0x7d, 0x32);
        if (lv == "warn" || lv == "warning") return Color.FromArgb(0xe6, 0x8a, 0x00);
        if (lv == "error" || lv == "danger") return Color.FromArgb(0xc6, 0x28, 0x28);
        return Color.FromArgb(0x6E, 0xC1, 0xF5); // info: light blue to match icon
    }
}
