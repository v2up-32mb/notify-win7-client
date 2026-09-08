using System;
using System.IO;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) { LogCrash("UI", e.Exception); };
        AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { LogCrash("BG", e.ExceptionObject as Exception); };
        try
        {
            AppConfig.Load();
        }
        catch (Exception ex) { LogCrash("Config", ex); }
        try
        {
            using (MainForm f = new MainForm())
            {
                // MUST pass main form: parameterless Run() never shows/activates it,
                // so Shown/Load startup logic (minimize + first poll) would never run.
                Application.Run(f);
            }
        }
        catch (Exception ex)
        {
            LogCrash("Main", ex);
            try { MessageBox.Show("NotifyClient 启动失败:\n" + ex.Message + "\n详见 %USERPROFILE%\\.notifyclient\\crash.log", "NotifyClient"); } catch { }
        }
    }

    static void LogCrash(string where, Exception ex)
    {
        try
        {
            string dir = AppConfig.HomeDir;
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + where + "] " + (ex == null ? "(null)" : ex.ToString()) + "\r\n");
        }
        catch { }
    }
}
