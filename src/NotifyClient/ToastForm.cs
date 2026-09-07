using System;
using System.Drawing;
using System.Windows.Forms;

// Win10-like toast, borderless, topmost, no focus steal, fade+slide anim.
class ToastForm : Form
{
    string title, body, level;
    Label lblTitle, lblBody;
    Button btnX;
    Panel bar;
    Timer lifeTimer, animTimer;
    int stayMs;
    int targetTop;
    float opacityTarget = 1f;

    public ToastForm(string title, string body, string level, int staySeconds)
    {
        this.title = title ?? "";
        this.body = body ?? "";
        this.level = (level ?? "info").ToLower();
        this.stayMs = Math.Max(3, staySeconds) * 1000;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(330, 96);
        BackColor = Color.FromArgb(248, 248, 248);
        Opacity = 0;

        bar = new Panel();
        bar.BackColor = LevelColor(this.level);
        bar.SetBounds(0, 0, 6, 96);
        Controls.Add(bar);

        lblTitle = new Label();
        lblTitle.Text = this.title;
        lblTitle.Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold);
        lblTitle.ForeColor = Color.FromArgb(30, 30, 30);
        lblTitle.SetBounds(14, 8, 278, 20);
        Controls.Add(lblTitle);

        lblBody = new Label();
        lblBody.Text = this.body;
        lblBody.Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Regular);
        lblBody.ForeColor = Color.FromArgb(60, 60, 60);
        lblBody.SetBounds(14, 30, 278, 56);
        Controls.Add(lblBody);

        btnX = new Button();
        btnX.Text = "x";
        btnX.FlatStyle = FlatStyle.Flat;
        btnX.FlatAppearance.BorderSize = 0;
        btnX.ForeColor = Color.Gray;
        btnX.Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold);
        btnX.SetBounds(300, 4, 26, 22);
        btnX.Click += delegate { CloseToast(); };
        Controls.Add(btnX);

        // border
        Paint += delegate(object s, PaintEventArgs e) {
            using (Pen p = new Pen(Color.FromArgb(200, 200, 200)))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        };

        lifeTimer = new Timer();
        lifeTimer.Interval = stayMs;
        lifeTimer.Tick += delegate { lifeTimer.Stop(); CloseToast(); };

        animTimer = new Timer();
        animTimer.Interval = 15;
        animTimer.Tick += delegate {
            bool done = true;
            // slide toward target
            if (Top != targetTop)
            {
                int d = targetTop - Top;
                int step = d / 4;
                if (step == 0) step = d > 0 ? 1 : -1;
                if (Math.Abs(d) <= 2) Top = targetTop;
                else { Top += step * 2; done = false; }
            }
            if (Opacity < opacityTarget)
            {
                Opacity = Math.Min(opacityTarget, Opacity + 0.12);
                done = false;
            }
            if (done) animTimer.Stop();
        };

        Click += delegate { CloseToast(); };
        lblBody.Click += delegate { CloseToast(); };
        lblTitle.Click += delegate { CloseToast(); };
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE (no focus steal)
            cp.ExStyle |= 0x00080000; // WS_EX_LAYERED (smooth opacity)
            return cp;
        }
    }

    public int TargetTop { get { return targetTop; } set { targetTop = value; if (!animTimer.Enabled) animTimer.Start(); } }

    public void SlideIn(int left, int top)
    {
        SetBounds(left, top + 24, Width, Height);
        Opacity = 0;
        targetTop = top;
        Show();
        animTimer.Start();
        lifeTimer.Start();
    }

    public void CloseToast()
    {
        try { lifeTimer.Stop(); } catch { }
        try { animTimer.Stop(); } catch { }
        ToastManager.Remove(this);
        try { Close(); } catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (lifeTimer != null) lifeTimer.Dispose();
            if (animTimer != null) animTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    static Color LevelColor(string lv)
    {
        if (lv == "success") return Color.FromArgb(0x2e, 0x7d, 0x32);
        if (lv == "warn" || lv == "warning") return Color.FromArgb(0xe6, 0x8a, 0x00);
        if (lv == "error" || lv == "danger") return Color.FromArgb(0xc6, 0x28, 0x28);
        return Color.FromArgb(0x1e, 0x88, 0xe5); // info
    }
}
