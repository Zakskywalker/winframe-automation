using System.Windows.Forms;
using WinFrame.Core.Config;
using WinFrame.Core.Logging;
using WinFrame.Core.Native;

namespace WinFrame.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        NativeMethods.EnsureDpiAwareness();
        ApplicationConfiguration.Initialize();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            SystemLogger.Log($"UI Thread Exception: {e.Exception}", SystemLogger.LogLevel.ERROR);
            MessageBox.Show(e.Exception.ToString(), "WinFrame Studio Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        try
        {
            var cfg = ConfigManager.Current;
            if (cfg.ShowSplash)
            {
                using var splash = new SplashScreenForm();
                splash.Show();
                Application.DoEvents();
                Thread.Sleep(1200);
                splash.Close();
            }

            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            SystemLogger.Log($"Startup Exception: {ex}", SystemLogger.LogLevel.ERROR);
            MessageBox.Show(ex.ToString(), "WinFrame Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
