using System.Windows.Forms;

namespace WinFrame.Installer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerWizardForm());
    }
}
