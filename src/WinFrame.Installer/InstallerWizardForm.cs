using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace WinFrame.Installer;

/// <summary>
/// Multi-step Windows 11 Setup Wizard for WinFrame Automation Studio,
/// stylized with the Hand of Glory splash artwork.
/// Installs <c>WinFrame.App.exe</c>, <c>WinFrame.Cli.exe</c>, <c>WinFrame.Service.exe</c>,
/// <c>WinFrame.Core.dll</c>, Desktop &amp; Start Menu shortcuts, and Windows 11 Installed Apps registry entry.
/// </summary>
public sealed class InstallerWizardForm : Form
{
    private int currentPage;
    private const int TotalPages = 6;

    private string targetDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "WinFrame");
    private bool createDesktopShortcut = true;
    private bool createStartMenuShortcut = true;
    private bool launchOnStartup;
    private bool launchNow = true;

    private readonly Panel pnlSidebar = new() { Dock = DockStyle.Left, Width = 210, BackColor = Color.FromArgb(18, 13, 22) };
    private readonly Panel pnlContent = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 18, 30), Padding = new Padding(24) };
    private readonly Panel pnlBottom = new() { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.FromArgb(18, 13, 22), Padding = new Padding(12) };

    private readonly Button btnBack = new() { Text = "< Back", Width = 90, Height = 32, Enabled = false };
    private readonly Button btnNext = new() { Text = "Next >", Width = 110, Height = 32 };
    private readonly Button btnCancel = new() { Text = "Cancel", Width = 90, Height = 32 };

    private readonly Panel[] pages = new Panel[TotalPages];
    private RadioButton rbAccept = null!;
    private TextBox txtInstallPath = null!;
    private CheckBox chkDesktop = null!;
    private CheckBox chkStartMenu = null!;
    private CheckBox chkStartup = null!;
    private RichTextBox rtbReadySummary = null!;
    private ProgressBar prgInstall = null!;
    private Label lblInstallStatus = null!;
    private CheckBox chkLaunchNow = null!;

    public InstallerWizardForm()
    {
        Text = "Setup — WinFrame Automation Studio v1.0 (Hand of Glory Games and Software)";
        ClientSize = new Size(720, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 18, 30);
        ForeColor = Color.FromArgb(245, 233, 211);
        Font = new Font("Segoe UI", 9.5f);

        BuildSidebar();
        BuildBottomBar();
        BuildPages();

        Controls.Add(pnlContent);
        Controls.Add(pnlSidebar);
        Controls.Add(pnlBottom);

        ShowPage(0);
    }

    private void BuildSidebar()
    {
        var pic = new PictureBox
        {
            Dock = DockStyle.Top,
            Height = 280,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(15, 11, 18)
        };
        string imgPath = Path.Combine(AppContext.BaseDirectory, "assets", "splash.jpg");
        if (!File.Exists(imgPath))
            imgPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "splash.jpg"));
        if (File.Exists(imgPath))
        {
            try
            {
                using var fs = new FileStream(imgPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var src = Image.FromStream(fs);
                pic.Image = new Bitmap(src);
            }
            catch { }
        }

        var lblBrand = new Label
        {
            Text = "HAND OF GLORY\nGAMES & SOFTWARE\n\nWinFrame v1.0 Setup\nZak Millikin\n(Zakskywalker M_A.P.)\n\nSplash Art:\nPerchance AI (Cursed)",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(245, 158, 11),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };

        pnlSidebar.Controls.Add(lblBrand);
        pnlSidebar.Controls.Add(pic);
    }

    private void BuildBottomBar()
    {
        StyleButton(btnBack, false);
        StyleButton(btnNext, true);
        StyleButton(btnCancel, false);

        btnBack.Location = new Point(380, 12);
        btnNext.Location = new Point(480, 12);
        btnCancel.Location = new Point(600, 12);

        btnBack.Click += (_, _) => { if (currentPage > 0) ShowPage(currentPage - 1); };
        btnNext.Click += async (_, _) => await AdvanceAsync();
        btnCancel.Click += (_, _) => Close();

        pnlBottom.Controls.AddRange(new Control[] { btnBack, btnNext, btnCancel });
    }

    private static void StyleButton(Button b, bool accent)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Color.FromArgb(80, 60, 90);
        b.BackColor = accent ? Color.FromArgb(245, 158, 11) : Color.FromArgb(36, 26, 44);
        b.ForeColor = accent ? Color.Black : Color.FromArgb(245, 233, 211);
        b.Font = new Font("Segoe UI", 9f, accent ? FontStyle.Bold : FontStyle.Regular);
    }

    private void BuildPages()
    {
        for (int i = 0; i < TotalPages; i++)
        {
            pages[i] = new Panel { Dock = DockStyle.Fill, Visible = false };
            pnlContent.Controls.Add(pages[i]);
        }

        // Page 0: Welcome
        pages[0].Controls.Add(MakeTitle("Welcome to WinFrame Automation Studio Setup"));
        pages[0].Controls.Add(new Label
        {
            Text = "This wizard will install WinFrame Automation Studio v1.0 on your computer:\n\n" +
                   "  • WinFrame.App.exe (Desktop Studio, Recorder, Accessory Naming Panel)\n" +
                   "  • WinFrame.Core.dll (Embeddable Win32, UIA, OpenCV & Step/Inverse Engine)\n" +
                   "  • WinFrame.Cli.exe (Command-Line Runner & Scraper)\n" +
                   "  • WinFrame.Service.exe (Remote REST API & Step-Queue Service)\n\n" +
                   "Created by Zak Millikin (Zakskywalker M_A.P.) — Hand of Glory Games and Software.\n" +
                   "Click Next to continue.",
            Location = new Point(16, 60),
            Size = new Size(440, 260)
        });

        // Page 1: GNU GPLv3 License
        pages[1].Controls.Add(MakeTitle("GNU General Public License v3.0"));
        var rtbLic = new RichTextBox
        {
            Location = new Point(16, 52),
            Size = new Size(440, 220),
            ReadOnly = true,
            BackColor = Color.FromArgb(15, 11, 18),
            ForeColor = Color.FromArgb(230, 237, 243),
            Font = new Font("Consolas", 8.5f),
            Text = "GNU GENERAL PUBLIC LICENSE Version 3\nCopyright (C) 2026 Zak Millikin (Zakskywalker M_A.P.) / Hand of Glory Games and Software.\n\n" +
                   "This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.\n\n" +
                   "Full license: https://www.gnu.org/licenses/gpl-3.0.html"
        };
        rbAccept = new RadioButton { Text = "I accept the terms in the GNU GPL v3.0 License Agreement", Location = new Point(16, 284), AutoSize = true, Checked = true };
        var rbDecline = new RadioButton { Text = "I do not accept the agreement", Location = new Point(16, 312), AutoSize = true };
        rbAccept.CheckedChanged += (_, _) => btnNext.Enabled = rbAccept.Checked;
        pages[1].Controls.AddRange(new Control[] { rtbLic, rbAccept, rbDecline });

        // Page 2: Destination Directory
        pages[2].Controls.Add(MakeTitle("Select Destination Location"));
        txtInstallPath = new TextBox
        {
            Text = targetDirectory,
            Location = new Point(16, 70),
            Width = 340,
            BackColor = Color.FromArgb(15, 11, 18),
            ForeColor = Color.White
        };
        var btnBrowse = new Button { Text = "Browse...", Location = new Point(366, 68), Width = 85, Height = 28 };
        StyleButton(btnBrowse, false);
        btnBrowse.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog { SelectedPath = txtInstallPath.Text };
            if (fbd.ShowDialog(this) == DialogResult.OK) txtInstallPath.Text = fbd.SelectedPath;
        };
        pages[2].Controls.AddRange(new Control[] { txtInstallPath, btnBrowse });

        // Page 3: Shortcuts & Startup Tasks
        pages[3].Controls.Add(MakeTitle("Select Additional Tasks"));
        chkDesktop = new CheckBox { Text = "Create a Desktop shortcut", Checked = true, Location = new Point(16, 70), AutoSize = true };
        chkStartMenu = new CheckBox { Text = "Create a Start Menu shortcut", Checked = true, Location = new Point(16, 104), AutoSize = true };
        chkStartup = new CheckBox { Text = "Launch WinFrame Studio when Windows starts", Checked = false, Location = new Point(16, 138), AutoSize = true };
        pages[3].Controls.AddRange(new Control[] { chkDesktop, chkStartMenu, chkStartup });

        // Page 4: Ready to Install Summary & Progress
        pages[4].Controls.Add(MakeTitle("Ready to Install WinFrame Automation"));
        rtbReadySummary = new RichTextBox
        {
            Location = new Point(16, 52),
            Size = new Size(440, 170),
            ReadOnly = true,
            BackColor = Color.FromArgb(15, 11, 18),
            ForeColor = Color.FromArgb(230, 237, 243),
            Font = new Font("Consolas", 9f)
        };
        lblInstallStatus = new Label { Text = "Click Install to begin.", Location = new Point(16, 236), AutoSize = true };
        prgInstall = new ProgressBar { Location = new Point(16, 262), Size = new Size(440, 24) };
        pages[4].Controls.AddRange(new Control[] { rtbReadySummary, lblInstallStatus, prgInstall });

        // Page 5: Completed
        pages[5].Controls.Add(MakeTitle("Completing WinFrame Automation Setup"));
        chkLaunchNow = new CheckBox { Text = "Launch WinFrame Automation Studio now", Checked = true, Location = new Point(16, 90), AutoSize = true };
        pages[5].Controls.Add(chkLaunchNow);
    }

    private static Label MakeTitle(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 13f, FontStyle.Bold),
        ForeColor = Color.FromArgb(245, 158, 11),
        Location = new Point(16, 12),
        AutoSize = true
    };

    private void ShowPage(int index)
    {
        currentPage = index;
        for (int i = 0; i < TotalPages; i++) pages[i].Visible = i == index;

        btnBack.Enabled = index > 0 && index < TotalPages - 1;
        btnNext.Text = index switch
        {
            4 => "Install",
            5 => "Finish",
            _ => "Next >"
        };

        if (index == 4)
        {
            targetDirectory = txtInstallPath.Text.Trim();
            createDesktopShortcut = chkDesktop.Checked;
            createStartMenuShortcut = chkStartMenu.Checked;
            launchOnStartup = chkStartup.Checked;
            rtbReadySummary.Text =
                $"Destination folder:\n  {targetDirectory}\n\n" +
                $"Components:\n  WinFrame.App.exe, WinFrame.Core.dll, WinFrame.Cli.exe, WinFrame.Service.exe\n\n" +
                $"Shortcuts:\n  Desktop={createDesktopShortcut}, StartMenu={createStartMenuShortcut}, RunOnStartup={launchOnStartup}";
        }
    }

    private async Task AdvanceAsync()
    {
        if (currentPage == 4)
        {
            btnNext.Enabled = false;
            btnBack.Enabled = false;
            await PerformInstallAsync();
            btnNext.Enabled = true;
            ShowPage(5);
            return;
        }
        if (currentPage == 5)
        {
            launchNow = chkLaunchNow.Checked;
            string destExe = Path.Combine(targetDirectory, "WinFrame.App.exe");
            if (launchNow && File.Exists(destExe))
            {
                try { Process.Start(new ProcessStartInfo(destExe) { UseShellExecute = true, WorkingDirectory = targetDirectory }); }
                catch { }
            }
            Close();
            return;
        }
        ShowPage(currentPage + 1);
    }

    private async Task PerformInstallAsync()
    {
        lblInstallStatus.Text = "Creating installation folder...";
        prgInstall.Value = 15;
        Directory.CreateDirectory(targetDirectory);
        await Task.Delay(150);

        lblInstallStatus.Text = "Copying WinFrame binaries & OpenCV runtime...";
        prgInstall.Value = 45;

        string[] candidateDirs =
        {
            AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "WinFrame.App", "bin", "out", "Release", "net10.0-windows", "win-x64")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "WinFrame.App", "bin", "out", "Debug", "net10.0-windows", "win-x64")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "publish", "win11-x64"))
        };

        string? sourceDir = candidateDirs.FirstOrDefault(d => File.Exists(Path.Combine(d, "WinFrame.App.exe")));
        if (sourceDir != null)
        {
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(sourceDir, file);
                string dst = Path.Combine(targetDirectory, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                try { File.Copy(file, dst, overwrite: true); } catch { }
            }
        }
        await Task.Delay(200);

        lblInstallStatus.Text = "Creating Windows 11 shortcuts & registry entry...";
        prgInstall.Value = 80;
        string destExe = Path.Combine(targetDirectory, "WinFrame.App.exe");
        CreateShortcutsAndRegistry(destExe);
        await Task.Delay(150);

        lblInstallStatus.Text = "Installation complete!";
        prgInstall.Value = 100;
    }

    private void CreateShortcutsAndRegistry(string destExe)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                if (createDesktopShortcut)
                {
                    string deskLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "WinFrame Automation.lnk");
                    dynamic s = shell.CreateShortcut(deskLnk);
                    s.TargetPath = destExe;
                    s.WorkingDirectory = targetDirectory;
                    s.Description = "WinFrame Automation Studio — Hand of Glory Games and Software";
                    s.Save();
                }
                if (createStartMenuShortcut)
                {
                    string smDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "WinFrame Automation");
                    Directory.CreateDirectory(smDir);
                    dynamic s = shell.CreateShortcut(Path.Combine(smDir, "WinFrame Automation.lnk"));
                    s.TargetPath = destExe;
                    s.WorkingDirectory = targetDirectory;
                    s.Description = "WinFrame Automation Studio";
                    s.Save();
                }
            }

            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinFrameAutomation");
            if (key != null)
            {
                key.SetValue("DisplayName", "WinFrame Automation Studio 1.0");
                key.SetValue("DisplayVersion", "1.0.0");
                key.SetValue("Publisher", "Hand of Glory Games and Software — Zak Millikin (Zakskywalker M_A.P.)");
                key.SetValue("InstallLocation", targetDirectory);
                key.SetValue("DisplayIcon", destExe);
                key.SetValue("UninstallString", $"powershell.exe -Command \"Remove-Item -Recurse -Force '{targetDirectory}'; Remove-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\WinFrameAutomation' -Force;\"");
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
            }

            if (launchOnStartup)
            {
                using var runKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                runKey?.SetValue("WinFrameAutomation", $"\"{destExe}\"");
            }
        }
        catch { }
    }
}

