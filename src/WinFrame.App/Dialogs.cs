using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using WinFrame.Core.Config;
using WinFrame.Core.Controls;
using WinFrame.Core.Execution;
using WinFrame.Core.Themes;

namespace WinFrame.App;

/// <summary>
/// File -> Settings -> General Settings &amp; Config Settings dialog.
/// Edits general preferences, base local data &amp; DPAPI credentials, Win32 control outline highlighting
/// (<c>highlight = 1/true/0/""</c>, color, interval), failure policy thresholds, and TestBed configs.
/// </summary>
public sealed class SettingsDialog : Form
{
    public SettingsDialog(AppConfig cfg, int initialTab = 0)
    {
        Name = "dlgSettings";
        Text = "WinFrame Settings — General, Config & TestBeds";
        Size = new Size(720, 560);
        StartPosition = FormStartPosition.CenterParent;

        var tabs = new TabControl { Name = "tabsSettings", Dock = DockStyle.Fill };

        // ---- Tab 1: General Settings ----
        var tabGeneral = new TabPage("General Settings") { Name = "tabGeneralSettings", Padding = new Padding(16) };
        var chkSplash = new CheckBox { Name = "chkShowSplash", Text = "Show Hand of Glory splash screen on startup", Checked = cfg.ShowSplash, AutoSize = true, Location = new Point(20, 20) };
        var chkTray = new CheckBox { Name = "chkMinimizeToTray", Text = "Minimize to notification tray", Checked = cfg.MinimizeToTray, AutoSize = true, Location = new Point(20, 52) };
        var chkTrayNotif = new CheckBox { Name = "chkTrayNotifications", Text = "Show notification tray tooltips & scrolling icon messages", Checked = cfg.TrayNotifications, AutoSize = true, Location = new Point(20, 84) };
        var chkFailShot = new CheckBox { Name = "chkCaptureScreenshotOnFailure", Text = "Capture OpenCV screenshot automatically on step failure", Checked = cfg.CaptureScreenshotOnFailure, AutoSize = true, Location = new Point(20, 116) };
        var chkRunShot = new CheckBox { Name = "chkCaptureControlImagesDuringRun", Text = "Capture OpenCV control + window snapshots on every run", Checked = cfg.CaptureControlImagesDuringRun, AutoSize = true, Location = new Point(20, 148) };

        var lblHighlight = new Label { Text = "Win32 Control Highlight Variable (1/true = ON, 0/empty = OFF):", AutoSize = true, Location = new Point(20, 190) };
        var cmbHighlight = new ComboBox { Name = "cmbHighlightVar", Location = new Point(390, 186), Width = 140 };
        cmbHighlight.Items.AddRange(new object[] { "1", "true", "0", "" });
        cmbHighlight.Text = cfg.Highlight ?? "1";

        var lblHiColor = new Label { Text = "Win32 Outline Highlight Color (Hex or Name):", AutoSize = true, Location = new Point(20, 226) };
        var txtHiColor = new TextBox { Name = "txtHighlightColor", Text = cfg.HighlightColor, Location = new Point(390, 222), Width = 140 };
        var btnPickHiColor = new Button { Name = "btnPickHighlightColor", Text = "Pick...", Location = new Point(540, 220), Width = 70 };
        btnPickHiColor.Click += (_, _) =>
        {
            using var cd = new ColorDialog();
            if (cd.ShowDialog(this) == DialogResult.OK)
                txtHiColor.Text = $"#{cd.Color.R:X2}{cd.Color.G:X2}{cd.Color.B:X2}";
        };

        var lblHiInterval = new Label { Text = "Default Short Highlight Interval (ms):", AutoSize = true, Location = new Point(20, 262) };
        var numHiInterval = new NumericUpDown { Name = "numHighlightInterval", Minimum = 30, Maximum = 5000, Value = Math.Clamp(cfg.HighlightIntervalMs, 30, 5000), Location = new Point(390, 258), Width = 140 };

        var lblStepDelay = new Label { Text = "Default Step Delay (ms):", AutoSize = true, Location = new Point(20, 298) };
        var numStepDelay = new NumericUpDown { Name = "numStepDelay", Minimum = 0, Maximum = 10000, Value = Math.Clamp(cfg.StepDelayMs, 0, 10000), Location = new Point(390, 294), Width = 140 };

        var lblReportTheme = new Label { Text = "Preferred HTML Report Template:", AutoSize = true, Location = new Point(20, 334) };
        var cmbReportTheme = new ComboBox { Name = "cmbReportTheme", DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(390, 330), Width = 140 };
        cmbReportTheme.Items.AddRange(new object[] { "dark", "light" });
        cmbReportTheme.SelectedItem = cfg.ReportTheme.Equals("light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";

        tabGeneral.Controls.AddRange(new Control[]
        {
            chkSplash, chkTray, chkTrayNotif, chkFailShot, chkRunShot,
            lblHighlight, cmbHighlight, lblHiColor, txtHiColor, btnPickHiColor,
            lblHiInterval, numHiInterval, lblStepDelay, numStepDelay, lblReportTheme, cmbReportTheme
        });

        // ---- Tab 2: Config Settings (Credentials, Thresholds, Storage, Remote Service) ----
        var tabConfig = new TabPage("Config & Thresholds") { Name = "tabConfigSettings", Padding = new Padding(16) };
        var lblUser = new Label { Text = "Base Local Username:", AutoSize = true, Location = new Point(20, 20) };
        var txtUser = new TextBox { Name = "txtConfigUsername", Text = cfg.Credentials.Username, Location = new Point(280, 16), Width = 260 };

        var lblDomain = new Label { Text = "Base Local Domain:", AutoSize = true, Location = new Point(20, 54) };
        var txtDomain = new TextBox { Name = "txtConfigDomain", Text = cfg.Credentials.Domain, Location = new Point(280, 50), Width = 260 };

        var lblPass = new Label { Text = "Password (DPAPI Encrypted):", AutoSize = true, Location = new Point(20, 88) };
        var txtPass = new TextBox { Name = "txtConfigPassword", Text = cfg.Credentials.Password, UseSystemPasswordChar = true, Location = new Point(280, 84), Width = 260 };

        var lblOnFail = new Label { Text = "On Step Failure Action:", AutoSize = true, Location = new Point(20, 130) };
        var cmbOnFail = new ComboBox { Name = "cmbOnStepFailure", DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(280, 126), Width = 200 };
        cmbOnFail.DataSource = Enum.GetValues<FailureAction>();
        cmbOnFail.SelectedItem = cfg.FailurePolicy.OnStepFailure;

        var lblStepRetry = new Label { Text = "Step Retry Count:", AutoSize = true, Location = new Point(20, 164) };
        var numStepRetry = new NumericUpDown { Name = "numStepRetryCount", Minimum = 0, Maximum = 20, Value = cfg.FailurePolicy.StepRetryCount, Location = new Point(280, 160), Width = 120 };

        var lblTestRetry = new Label { Text = "Test Retry From Start Count:", AutoSize = true, Location = new Point(20, 198) };
        var numTestRetry = new NumericUpDown { Name = "numTestRetryCount", Minimum = 0, Maximum = 10, Value = cfg.FailurePolicy.TestRetryCount, Location = new Point(280, 194), Width = 120 };

        var lblMaxFailTest = new Label { Text = "Max Failures Per Test Threshold:", AutoSize = true, Location = new Point(20, 232) };
        var numMaxFailTest = new NumericUpDown { Name = "numMaxFailuresPerTest", Minimum = 0, Maximum = 500, Value = cfg.FailurePolicy.MaxFailuresPerTest, Location = new Point(280, 228), Width = 120 };

        var lblHardStop = new Label { Text = "Full Stop Threshold (0 = off):", AutoSize = true, Location = new Point(20, 266) };
        var numHardStop = new NumericUpDown { Name = "numHardStopThreshold", Minimum = 0, Maximum = 1000, Value = cfg.FailurePolicy.HardStopThreshold, Location = new Point(280, 262), Width = 120 };

        var lblAutoInv = new Label { Text = "Auto-Inverse Rollback Steps on Failure:", AutoSize = true, Location = new Point(20, 300) };
        var numAutoInv = new NumericUpDown { Name = "numAutoInverseRollback", Minimum = 0, Maximum = 50, Value = cfg.FailurePolicy.AutoInverseRollbackSteps, Location = new Point(280, 296), Width = 120 };

        var lblStoreFmt = new Label { Text = "Control Sheet Format:", AutoSize = true, Location = new Point(20, 340) };
        var cmbStoreFmt = new ComboBox { Name = "cmbControlStoreFormat", DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(280, 336), Width = 160 };
        cmbStoreFmt.DataSource = Enum.GetValues<ControlStoreFormat>();
        cmbStoreFmt.SelectedItem = cfg.ControlStore.Format;

        var lblImgMode = new Label { Text = "Control Image Encoding (file/b=/c=/7zc=):", AutoSize = true, Location = new Point(20, 374) };
        var cmbImgMode = new ComboBox { Name = "cmbImageStorageMode", DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(280, 370), Width = 160 };
        cmbImgMode.DataSource = Enum.GetValues<ImageStorageMode>();
        cmbImgMode.SelectedItem = cfg.ControlStore.ImageStorage;

        var lblSvcUrl = new Label { Text = "Remote Service / API URL:", AutoSize = true, Location = new Point(20, 408) };
        var txtSvcUrl = new TextBox { Name = "txtServiceUrl", Text = cfg.Service.Urls, Location = new Point(280, 404), Width = 260 };

        tabConfig.Controls.AddRange(new Control[]
        {
            lblUser, txtUser, lblDomain, txtDomain, lblPass, txtPass,
            lblOnFail, cmbOnFail, lblStepRetry, numStepRetry, lblTestRetry, numTestRetry,
            lblMaxFailTest, numMaxFailTest, lblHardStop, numHardStop, lblAutoInv, numAutoInv,
            lblStoreFmt, cmbStoreFmt, lblImgMode, cmbImgMode, lblSvcUrl, txtSvcUrl
        });

        // ---- Tab 3: Separate TestBeds Config ----
        var tabBeds = new TabPage("TestBeds Config") { Name = "tabTestBeds", Padding = new Padding(12) };
        var txtBedsJson = new TextBox
        {
            Name = "txtTestBedsJson",
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.5f),
            Text = JsonSerializer.Serialize(ConfigManager.LoadTestBeds(), ConfigManager.JsonOptions)
        };
        tabBeds.Controls.Add(txtBedsJson);

        tabs.TabPages.Add(tabGeneral);
        tabs.TabPages.Add(tabConfig);
        tabs.TabPages.Add(tabBeds);
        tabs.SelectedIndex = Math.Clamp(initialTab, 0, 2);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnSave = new Button { Name = "btnSaveSettings", Text = "Save Settings", Width = 120, Height = 30, Tag = "accent" };
        var btnCancel = new Button { Name = "btnCancelSettings", Text = "Cancel", Width = 90, Height = 30 };

        btnSave.Click += (_, _) =>
        {
            cfg.ShowSplash = chkSplash.Checked;
            cfg.MinimizeToTray = chkTray.Checked;
            cfg.TrayNotifications = chkTrayNotif.Checked;
            cfg.CaptureScreenshotOnFailure = chkFailShot.Checked;
            cfg.CaptureControlImagesDuringRun = chkRunShot.Checked;
            cfg.Highlight = cmbHighlight.Text;
            cfg.HighlightColor = txtHiColor.Text.Trim();
            cfg.HighlightIntervalMs = (int)numHiInterval.Value;
            cfg.StepDelayMs = (int)numStepDelay.Value;
            cfg.ReportTheme = cmbReportTheme.SelectedItem?.ToString() ?? "dark";

            cfg.Credentials.Username = txtUser.Text.Trim();
            cfg.Credentials.Domain = txtDomain.Text.Trim();
            cfg.Credentials.Password = txtPass.Text;

            cfg.FailurePolicy.OnStepFailure = (FailureAction)cmbOnFail.SelectedItem!;
            cfg.FailurePolicy.StepRetryCount = (int)numStepRetry.Value;
            cfg.FailurePolicy.TestRetryCount = (int)numTestRetry.Value;
            cfg.FailurePolicy.MaxFailuresPerTest = (int)numMaxFailTest.Value;
            cfg.FailurePolicy.HardStopThreshold = (int)numHardStop.Value;
            cfg.FailurePolicy.AutoInverseRollbackSteps = (int)numAutoInv.Value;

            cfg.ControlStore.Format = (ControlStoreFormat)cmbStoreFmt.SelectedItem!;
            cfg.ControlStore.ImageStorage = (ImageStorageMode)cmbImgMode.SelectedItem!;
            cfg.Service.Urls = txtSvcUrl.Text.Trim();

            try
            {
                var beds = JsonSerializer.Deserialize<List<TestBedConfig>>(txtBedsJson.Text, ConfigManager.JsonOptions);
                if (beds != null) foreach (var b in beds) ConfigManager.SaveTestBed(b);
            }
            catch { }

            ConfigManager.Save(cfg);
            DialogResult = DialogResult.OK;
            Close();
        };
        btnCancel.Click += (_, _) => Close();

        bottom.Controls.Add(btnSave);
        bottom.Controls.Add(btnCancel);
        Controls.Add(tabs);
        Controls.Add(bottom);

        ThemeEngine.Apply(this, ThemeEngine.GetTheme(cfg.Theme));
    }
}

/// <summary>
/// File -> Settings -> Theme Picker dialog.
/// Allows choosing an overall theme AND customizing individual controls by their <see cref="Control.Name"/>.
/// </summary>
public sealed class ThemePickerDialog : Form
{
    public ThemeProfile SelectedTheme { get; private set; }

    public ThemePickerDialog(Control targetForm, AppConfig cfg)
    {
        Name = "dlgThemePicker";
        Text = "Theme Picker — Overall Theme & Per-Control Overrides";
        Size = new Size(680, 480);
        StartPosition = FormStartPosition.CenterParent;
        SelectedTheme = ThemeEngine.GetTheme(cfg.Theme);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(10) };
        top.Controls.Add(new Label { Text = "Overall Theme:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });

        var cmbTheme = new ComboBox { Name = "cmbThemeProfile", DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        foreach (var t in ThemeEngine.BuiltInThemes) cmbTheme.Items.Add(t.Name);
        cmbTheme.SelectedItem = SelectedTheme.Name;

        top.Controls.Add(cmbTheme);

        var grid = new DataGridView
        {
            Name = "gridControlThemeOverrides",
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false
        };
        grid.Columns.Add("ControlName", "Control Name (AutomationId)");
        grid.Columns.Add("ControlInfo", "Control Type / Text");
        grid.Columns.Add("BackHex", "Custom BackColor (#RRGGBB)");
        grid.Columns.Add("ForeHex", "Custom ForeColor (#RRGGBB)");
        grid.Columns[0].ReadOnly = true;
        grid.Columns[1].ReadOnly = true;

        void PopulateGrid()
        {
            grid.Rows.Clear();
            foreach (var kv in ThemeEngine.HarvestFormControlNames(targetForm).OrderBy(k => k.Key))
            {
                SelectedTheme.ControlOverrides.TryGetValue(kv.Key, out var ov);
                grid.Rows.Add(kv.Key, kv.Value, ov?.BackColorHex ?? "", ov?.ForeColorHex ?? "");
            }
        }

        cmbTheme.SelectedIndexChanged += (_, _) =>
        {
            SelectedTheme = ThemeEngine.GetTheme(cmbTheme.SelectedItem?.ToString() ?? "Dark");
            PopulateGrid();
        };
        PopulateGrid();

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnApply = new Button { Name = "btnApplyTheme", Text = "Apply & Save Theme", Width = 150, Height = 30, Tag = "accent" };
        btnApply.Click += (_, _) =>
        {
            SelectedTheme.ControlOverrides.Clear();
            foreach (DataGridViewRow r in grid.Rows)
            {
                string cName = r.Cells[0].Value?.ToString() ?? "";
                string back = r.Cells[2].Value?.ToString()?.Trim() ?? "";
                string fore = r.Cells[3].Value?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(cName) && (!string.IsNullOrEmpty(back) || !string.IsNullOrEmpty(fore)))
                {
                    SelectedTheme.ControlOverrides[cName] = new ControlThemeOverride
                    {
                        BackColorHex = string.IsNullOrEmpty(back) ? null : back,
                        ForeColorHex = string.IsNullOrEmpty(fore) ? null : fore
                    };
                }
            }
            ThemeEngine.SaveCustomTheme(SelectedTheme);
            cfg.Theme = SelectedTheme.Name;
            ConfigManager.Save(cfg);
            ThemeEngine.Apply(targetForm, SelectedTheme);
            DialogResult = DialogResult.OK;
            Close();
        };

        bottom.Controls.Add(btnApply);
        Controls.Add(grid);
        Controls.Add(top);
        Controls.Add(bottom);

        ThemeEngine.Apply(this, SelectedTheme);
    }
}

/// <summary>
/// Help / About dialog with AI name, Zak Millikin, Hand of Glory Games and Software,
/// GNU GPL v3.0 license link, and a menu/key viewer to download a JSON of all Automation IDs.
/// </summary>
public sealed class AboutDialog : Form
{
    public AboutDialog(Control mainForm, ControlRepository repo, AppConfig cfg)
    {
        Name = "dlgAbout";
        Text = "About WinFrame Automation & Automation IDs Key";
        Size = new Size(680, 520);
        StartPosition = FormStartPosition.CenterParent;

        var header = new Panel { Dock = DockStyle.Top, Height = 175, Padding = new Padding(16) };
        var lblCompany = new Label
        {
            Name = "lblAboutCompany",
            Text = "Hand of Glory Games and Software",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 10)
        };
        var lblAuthor = new Label
        {
            Name = "lblAboutAuthor",
            Text = "Creator & Architect: Zak Millikin (Zakskywalker M_A.P.)\nAI Collaborators: Google DeepMind Antigravity (Claude Opus & Gemini)\nProduct: WinFrame Windows Automation Framework v1.0.0",
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Location = new Point(16, 38)
        };
        var lnkSplashArt = new LinkLabel
        {
            Name = "lnkPerchanceSplashArt",
            Text = "Splash Artwork Created With: Perchance AI (Cursed) (https://perchance.org/cursed-ai)",
            AutoSize = true,
            Location = new Point(16, 104),
            LinkColor = Color.FromArgb(245, 158, 11)
        };
        lnkSplashArt.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://perchance.org/cursed-ai") { UseShellExecute = true }); }
            catch { }
        };
        var lnkLicense = new LinkLabel
        {
            Name = "lnkGnuLicense",
            Text = "License: GNU General Public License v3.0 (https://www.gnu.org/licenses/gpl-3.0.html)",
            AutoSize = true,
            Location = new Point(16, 130),
            LinkColor = Color.FromArgb(245, 158, 11)
        };
        lnkLicense.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://www.gnu.org/licenses/gpl-3.0.html") { UseShellExecute = true }); }
            catch { }
        };
        header.Controls.AddRange(new Control[] { lblCompany, lblAuthor, lnkSplashArt, lnkLicense });

        // Build combined JSON of all Automation IDs (both ControlRepository + App UI controls)
        string BuildAllAutomationIdsJson()
        {
            var payload = new
            {
                generatedAt = DateTime.Now,
                organization = "Hand of Glory Games and Software",
                author = "Zak Millikin (Zakskywalker M_A.P.)",
                aiCollaborators = new[] { "Google DeepMind Antigravity (Claude Opus)", "Google DeepMind Antigravity (Gemini)" },
                splashArtworkCredit = "Perchance AI (Cursed) - https://perchance.org/cursed-ai",
                license = "GNU GPL v3.0 - https://www.gnu.org/licenses/gpl-3.0.html",
                scrapedControls = repo.All.OrderBy(c => c.Name).Select(c => new
                {
                    name = c.Name,
                    automationId = c.Get(ControlDefinition.Keys.AutomationId),
                    controlType = c.Get(ControlDefinition.Keys.ControlType),
                    window = c.Get(ControlDefinition.Keys.WindowTitle),
                    locator = c.ToLocator()
                }),
                appUiControls = ThemeEngine.HarvestFormControlNames(mainForm)
            };
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }

        var txtIds = new TextBox
        {
            Name = "txtAutomationIdsKey",
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9f),
            Text = BuildAllAutomationIdsJson()
        };

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        var btnDownload = new Button
        {
            Name = "btnDownloadAutomationIdsJson",
            Text = "Download Automation IDs JSON...",
            Width = 230,
            Height = 32,
            Tag = "accent"
        };
        btnDownload.Click += (_, _) =>
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                FileName = "winframe_automation_ids.json"
            };
            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                File.WriteAllText(sfd.FileName, txtIds.Text);
                MessageBox.Show(this, $"Saved Automation IDs JSON to:\n{sfd.FileName}", "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        bottom.Controls.Add(btnDownload);
        Controls.Add(txtIds);
        Controls.Add(header);
        Controls.Add(bottom);

        ThemeEngine.Apply(this, ThemeEngine.GetTheme(cfg.Theme));
    }
}
