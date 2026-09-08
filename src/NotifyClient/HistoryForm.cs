using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// Simple history viewer: newest first, click to preview body.
class HistoryForm : Form
{
    ListBox lst;
    TextBox txt;
    List<HistoryStore.Item> items;

    public HistoryForm()
    {
        Text = "历史消息";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 380);
        ShowInTaskbar = true;
        MinimizeBox = false;

        lst = new ListBox();
        lst.SetBounds(12, 12, 496, 220);
        lst.HorizontalScrollbar = true;
        Controls.Add(lst);

        txt = new TextBox();
        txt.Multiline = true;
        txt.ReadOnly = true;
        txt.ScrollBars = ScrollBars.Vertical;
        txt.SetBounds(12, 240, 496, 88);
        Controls.Add(txt);

        Button btnRefresh = new Button();
        btnRefresh.Text = "刷新";
        btnRefresh.SetBounds(12, 336, 100, 28);
        btnRefresh.Click += delegate { LoadData(); };
        Controls.Add(btnRefresh);

        Button btnClear = new Button();
        btnClear.Text = "清空历史";
        btnClear.SetBounds(122, 336, 100, 28);
        btnClear.Click += delegate {
            if (MessageBox.Show("确定清空全部历史吗?", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                HistoryStore.Clear();
                LoadData();
            }
        };
        Controls.Add(btnClear);

        Button btnClose = new Button();
        btnClose.Text = "关闭";
        btnClose.SetBounds(408, 336, 100, 28);
        btnClose.Click += delegate { Close(); };
        Controls.Add(btnClose);

        try
        {
            string ico = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (System.IO.File.Exists(ico)) Icon = new Icon(ico);
        }
        catch { }

        lst.SelectedIndexChanged += delegate {
            try
            {
                int i = lst.SelectedIndex;
                if (i >= 0 && i < items.Count)
                    txt.Text = items[i].body ?? "";
                else
                    txt.Text = "";
            }
            catch { }
        };

        LoadData();
    }

    void LoadData()
    {
        try
        {
            items = HistoryStore.Load(500);
            lst.BeginUpdate();
            lst.Items.Clear();
            foreach (HistoryStore.Item it in items)
            {
                string t = (it.time ?? "") + " [" + (it.level ?? "info") + "] " + (it.title ?? "");
                lst.Items.Add(t.Length > 90 ? t.Substring(0, 90) : t);
            }
            lst.EndUpdate();
            if (lst.Items.Count > 0) { lst.SelectedIndex = 0; }
            else { txt.Text = ""; }
        }
        catch { }
    }
}
