using System.Windows.Forms;
using WinFrame.Core.Config;
using WinFrame.Core.Native;

namespace WinFrame.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        NativeMethods.EnsureDpiAwareness();
        ApplicationConfiguration.Initialize();

        var cfg = ConfigManager.Current;
        if (cfg.ShowSplash)
        {
            using var splash = new SplashScreenForm();
            splash.Show();
            Application.DoEvents();
            Thread.Sleep(1400);
            splash.Close();
        }

        Application.Run(new MainForm());
    }
}

