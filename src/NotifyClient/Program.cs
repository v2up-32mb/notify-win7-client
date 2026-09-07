using System;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        AppConfig.Load();
        using (MainForm f = new MainForm())
        {
            Application.Run();
        }
    }
}
