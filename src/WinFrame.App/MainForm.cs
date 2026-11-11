using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WinFrame.Core.Config;
using WinFrame.Core.Controls;
using WinFrame.Core.Execution;
using WinFrame.Core.Keywords;
using WinFrame.Core.Logging;
using WinFrame.Core.Native;
using WinFrame.Core.Processes;
using WinFrame.Core.Recording;
using WinFrame.Core.Reporting;
using WinFrame.Core.Service;
using WinFrame.Core.Themes;
using WinFrame.Core.Update;
using WinFrame.Core.Vision;

namespace WinFrame.App;

public sealed class MainForm : Form
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly AppConfig cfg;
    private readonly ControlRepository repo;
    private readonly KeywordFramework engine;
    private readonly TestRecorder recorder;
    private StepQueueController stepController;
    private WinFrameApiServer? apiServer;

    private readonly List<TestCase> queuedTests = new();
    private CancellationTokenSource? runCts;

    private readonly NotifyIcon trayIcon;
    private readonly System.Windows.Forms.Timer marqueeTimer;
    private string marqueeText = "WinFrame Ready  ";
    private int marqueeOffset;

    // UI Controls
    private readonly ListBox lstQueue = new() { Name = "lstQueue", Dock = DockStyle.Fill };
    private readonly DataGridView gridSteps = new() { Name = "gridSteps", Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly DataGridView gridAccessoryControls = new() { Name = "gridAccessoryControls", Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly PictureBox picControlPreview = new() { Name = "picControlPreview", Dock = DockStyle.Bottom, Height = 140, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly TextBox txtRangeSpec = new() { Name = "txtRangeSpec", Text = "1-3", Width = 75 };
    private readonly CheckBox chkAutoInverse = new() { Name = "chkAutoInverse", Text = "Auto-Inverse on Error", AutoSize = true };
    private readonly CheckBox chkReplayOpenCv = new() { Name = "chkReplayOpenCv", Text = "2nd-Pass Record OpenCV", AutoSize = true };
    private readonly CheckBox chkWin32Highlight = new() { Name = "chkWin32Highlight", Text = "Win32 Highlight (1)", AutoSize = true };
    private readonly ComboBox cmbTestBed = new() { Name = "cmbTestBed", DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly Label lblStepCursor = new() { Name = "lblStepCursor", Text = "Cursor: 1/0", AutoSize = true, Padding = new Padding(6, 6, 6, 0) };
    private readonly ToolStripStatusLabel lblStatus = new() { Name = "lblStatus", Text = "Ready — Hand of Glory Games and Software" };

    private readonly RichTextBox rtbSystemLog = new() { Name = "rtbSystemLog", Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9f) };
    private readonly RichTextBox rtbUserLog = new() { Name = "rtbUserLog", Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9f) };
    private readonly RichTextBox rtbTestLog = new() { Name = "rtbTestLog", Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9f) };

    public MainForm()
    {
        Name = "frmWinFrameMain";
        Text = "WinFrame Automation Studio — Hand of Glory Games and Software (Zak Millikin)";
        Size = new Size(1360, 840);
        StartPosition = FormStartPosition.CenterScreen;

        cfg = ConfigManager.Current;
        repo = new ControlRepository(null, cfg.ControlStore).Load();
        engine = new KeywordFramework(repo, cfg);
        recorder = new TestRecorder(repo);
        stepController = engine.CreateStepController(CreateSampleCalculatorTest());

        chkWin32Highlight.Checked = cfg.IsHighlightEnabled;
        chkWin32Highlight.CheckedChanged += (_, _) =>
        {
            cfg.Highlight = chkWin32Highlight.Checked ? "1" : "0";
            stepController.Context.Variables["highlight"] = cfg.Highlight;
        };

        // Notification Tray Icon + Scrolling Marquee Icon
        trayIcon = new NotifyIcon
        {
            Text = "WinFrame Automation Studio",
            Visible = true,
            Icon = SystemIcons.Application
        };
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open WinFrame Studio", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        trayMenu.Items.Add("Run Active Queue", null, (_, _) => RunQueueAsync(false));
        trayMenu.Items.Add("Step Next", null, (_, _) => StepNextUi());
        trayMenu.Items.Add("Step Previous (Inverse)", null, (_, _) => StepPrevInverseUi());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => Close());
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };

        marqueeTimer = new System.Windows.Forms.Timer { Interval = 350 };
        marqueeTimer.Tick += (_, _) => UpdateTrayScrollingIcon();
        marqueeTimer.Start();

        BuildLayout();
        WireEvents();

        // Seed initial sample test into the queue so developer can immediately step/run/reverse
        AddTestToQueue(CreateSampleCalculatorTest());
        RefreshAccessoryTable();

        ThemeEngine.Apply(this, ThemeEngine.GetTheme(cfg.Theme));
        ProcessManagementEngine.Shared.StartWatchdog();
        _ = CheckForUpdatesOnStartupAsync();
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        var (newer, manifest) = await AutoUpdateManager.CheckForUpdateAsync();
        if (newer && manifest != null)
        {
            UpdateStatus($"Update v{manifest.Version} available! Use About -> Check for Updates to install.");
        }
    }

    private async Task CheckForUpdatesInteractiveAsync()
    {
        UpdateStatus("Checking GitHub Pages for WinFrame updates...");
        var (newer, manifest) = await AutoUpdateManager.CheckForUpdateAsync();
        if (newer && manifest != null)
        {
            var ans = MessageBox.Show(
                this,
                $"A newer version of WinFrame ({manifest.Version}) is available!\n\n{manifest.ReleaseNotes}\n\nDownload and run the installer now?",
                "WinFrame Auto-Updater",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (ans == DialogResult.Yes)
            {
                await AutoUpdateManager.LaunchUpdateUrlAsync(manifest.SetupUrl);
            }
        }
        else
        {
            MessageBox.Show(
                this,
                $"WinFrame is up to date (v{AutoUpdateManager.CurrentVersion}).\nUpdate source: {AutoUpdateManager.DefaultPagesBaseUrl}",
                "WinFrame Auto-Updater",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void BuildLayout()
    {
        // ---- Menu Bar ----
        var menu = new MenuStrip { Name = "menuMain", Dock = DockStyle.Top };

        var mnuFile = new ToolStripMenuItem("&File") { Name = "mnuFile" };
        var mnuLoadSheets = new ToolStripMenuItem("Load Test Sheet(s) (CSV / XLSX / JSON)...", null, (_, _) => LoadTestSheetsDialog());
        var mnuSaveSheet = new ToolStripMenuItem("Save Selected Test Sheet...", null, (_, _) => SaveSelectedTestDialog());

        var mnuSettings = new ToolStripMenuItem("&Settings") { Name = "mnuSettings" };
        mnuSettings.DropDownItems.Add("General Settings...", null, (_, _) => OpenSettings(0));
        mnuSettings.DropDownItems.Add("Config & Threshold Settings...", null, (_, _) => OpenSettings(1));
        mnuSettings.DropDownItems.Add("TestBeds Configuration...", null, (_, _) => OpenSettings(2));
        mnuSettings.DropDownItems.Add(new ToolStripSeparator());
        mnuSettings.DropDownItems.Add("Theme Picker (Overall & Per-Control)...", null, (_, _) =>
        {
            using var dlg = new ThemePickerDialog(this, cfg);
            dlg.ShowDialog(this);
        });

        mnuFile.DropDownItems.Add(mnuLoadSheets);
        mnuFile.DropDownItems.Add(mnuSaveSheet);
        mnuFile.DropDownItems.Add(new ToolStripSeparator());
        mnuFile.DropDownItems.Add(mnuSettings);
        mnuFile.DropDownItems.Add(new ToolStripSeparator());
        mnuFile.DropDownItems.Add("E&xit", null, (_, _) => Close());

        var mnuTools = new ToolStripMenuItem("&Pipelines & Tools") { Name = "mnuTools" };
        mnuTools.DropDownItems.Add("● Record New Test Case (Dock Bar + Step Editor + Save Flow)...", null, (_, _) => StartInteractiveRecordingFlow());
        mnuTools.DropDownItems.Add(new ToolStripSeparator());
        mnuTools.DropDownItems.Add("OpenCV Snipping Tool (Region/Control Snapshot)...", null, (_, _) => SnipRegionInteractive());
        mnuTools.DropDownItems.Add("Snapshot Full Screen (OpenCV)...", null, (_, _) => SnapshotScreenQuick());
        mnuTools.DropDownItems.Add("Insert Win32 HIGHLIGHT Steps for All Controls", null, (_, _) => InsertHighlightStepsIntoCurrentTest());
        mnuTools.DropDownItems.Add("Toggle Remote API Service (" + cfg.Service.Urls + ")", null, (_, _) => ToggleApiServer());
        mnuTools.DropDownItems.Add("Generate Dark & Light HTML Reports", null, (_, _) => GenerateReportsNow());

        var mnuAbout = new ToolStripMenuItem("&About") { Name = "mnuAbout" };
        mnuAbout.DropDownItems.Add("About WinFrame & GNU GPL v3.0 License...", null, (_, _) =>
        {
            using var dlg = new AboutDialog(this, repo, cfg);
            dlg.ShowDialog(this);
        });
        mnuAbout.DropDownItems.Add("Open Automation IDs Key & Download JSON...", null, (_, _) =>
        {
            using var dlg = new AboutDialog(this, repo, cfg);
            dlg.ShowDialog(this);
        });
        mnuAbout.DropDownItems.Add(new ToolStripSeparator());
        mnuAbout.DropDownItems.Add("Check for Updates (GitHub Pages Auto-Updater)...", null, async (_, _) => await CheckForUpdatesInteractiveAsync());

        menu.Items.Add(mnuFile);
        menu.Items.Add(mnuTools);
        menu.Items.Add(mnuAbout);

        // ---- Main Split: Left/Center Workspace + Right Accessory Naming Panel ----
        var mainSplit = new SplitContainer
        {
            Name = "splitMainAndAccessory",
            Dock = DockStyle.Fill
        };

        // ---- Left/Center: Queue + Step Editor + Developer Step/Inverse Range Bar + Logs ----
        var leftSplit = new SplitContainer
        {
            Name = "splitEditorAndLogs",
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        var queueAndStepsSplit = new SplitContainer
        {
            Name = "splitQueueAndSteps",
            Dock = DockStyle.Fill
        };

        Shown += (_, _) =>
        {
            try
            {
                if (mainSplit.Width > 400) mainSplit.SplitterDistance = Math.Min(900, mainSplit.Width - 320);
                if (leftSplit.Height > 300) leftSplit.SplitterDistance = Math.Min(500, leftSplit.Height - 200);
                if (queueAndStepsSplit.Width > 350) queueAndStepsSplit.SplitterDistance = 260;
            }
            catch { }
            Activate();
            BringToFront();
        };

        // Queue Panel (Pipeline 1: Load Tests Queue)
        var pnlQueue = new Panel { Name = "pnlLoadTestsQueue", Dock = DockStyle.Fill, Padding = new Padding(6) };
        var lblQueueHeader = new Label { Name = "lblQueueHeader", Text = "PIPELINE 1: LOAD TESTS QUEUE", Dock = DockStyle.Top, Height = 24, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        var barQueueBtns = new FlowLayoutPanel { Name = "barQueueBtns", Dock = DockStyle.Top, Height = 68 };

        var btnLoadDoc = new Button { Name = "btnLoadFromDoc", Text = "Load Sheet", Width = 88, Height = 28 };
        btnLoadDoc.Click += (_, _) => LoadTestSheetsDialog();
        var btnAddMem = new Button { Name = "btnLoadFromMemory", Text = "+ Memory Test", Width = 100, Height = 28 };
        btnAddMem.Click += (_, _) => AddTestToQueue(new TestCase($"InMemory_{queuedTests.Count + 1}"));
        var btnRunQueue = new Button { Name = "btnRunQueue", Text = "▶ Run Queue", Width = 95, Height = 28, Tag = "accent" };
        btnRunQueue.Click += (_, _) => RunQueueAsync(chkReplayOpenCv.Checked);
        var btnStopQueue = new Button { Name = "btnStopQueue", Text = "■ Stop", Width = 65, Height = 28 };
        btnStopQueue.Click += (_, _) => runCts?.Cancel();

        ReloadTestBedsCombo();
        barQueueBtns.Controls.AddRange(new Control[] { btnLoadDoc, btnAddMem, btnRunQueue, btnStopQueue, cmbTestBed });

        pnlQueue.Controls.Add(lstQueue);
        pnlQueue.Controls.Add(barQueueBtns);
        pnlQueue.Controls.Add(lblQueueHeader);
        queueAndStepsSplit.Panel1.Controls.Add(pnlQueue);

        // Center Panel: Step Queue Controller (Forward, Inverse, Range 1-3 / 4-1) + Create Tests Recorder
        var pnlSteps = new Panel { Name = "pnlStepsAndRecorder", Dock = DockStyle.Fill, Padding = new Padding(6) };
        var barDevQueue = new FlowLayoutPanel { Name = "barDeveloperStepQueue", Dock = DockStyle.Top, Height = 72, Padding = new Padding(2) };

        var btnStepPrev = new Button { Name = "btnStepPrevInverse", Text = "◀ Step Prev (Inverse)", Width = 142, Height = 30 };
        btnStepPrev.Click += (_, _) => StepPrevInverseUi();

        var btnStepNext = new Button { Name = "btnStepNext", Text = "Step Next ▶", Width = 105, Height = 30, Tag = "accent" };
        btnStepNext.Click += (_, _) => StepNextUi();

        var lblRange = new Label { Text = "Range (1-3 / 4-1):", AutoSize = true, Margin = new Padding(6, 7, 2, 0) };
        var btnRunRange = new Button { Name = "btnRunStepRange", Text = "Run Range", Width = 88, Height = 30 };
        btnRunRange.Click += (_, _) => RunRangeUi(useInverse: false);

        var btnRevRange = new Button { Name = "btnReverseStepRange", Text = "↺ Reverse (Inverse)", Width = 136, Height = 30 };
        btnRevRange.Click += (_, _) => RunRangeUi(useInverse: true);

        var btnRecord = new Button { Name = "btnToggleRecord", Text = "● Record Test", Width = 110, Height = 30 };
        btnRecord.Click += (_, _) => StartInteractiveRecordingFlow();

        var btnSnip = new Button { Name = "btnOpenCvSnip", Text = "✂ OpenCV Snip", Width = 105, Height = 30 };
        btnSnip.Click += (_, _) => SnipRegionInteractive();

        var btnAddHighlight = new Button { Name = "btnAddHighlightSteps", Text = "★ Add Highlight Steps", Width = 145, Height = 30 };
        btnAddHighlight.Click += (_, _) => InsertHighlightStepsIntoCurrentTest();

        barDevQueue.Controls.AddRange(new Control[]
        {
            btnStepPrev, btnStepNext, lblStepCursor, lblRange, txtRangeSpec, btnRunRange, btnRevRange,
            chkAutoInverse, chkReplayOpenCv, chkWin32Highlight, btnRecord, btnSnip, btnAddHighlight
        });

        gridSteps.Columns.Add("Index", "#");
        gridSteps.Columns[0].Width = 38;
        gridSteps.Columns.Add("Action", "Action (Win32 / UIA / OpenCV / Highlight)");
        gridSteps.Columns.Add("Target", "Target (@control / Locator / HWND)");
        gridSteps.Columns.Add("Value", "Value");
        gridSteps.Columns.Add("Options", "Options (retry/critical/inverse)");
        gridSteps.Columns.Add("Status", "Last Status");

        pnlSteps.Controls.Add(gridSteps);
        pnlSteps.Controls.Add(barDevQueue);
        queueAndStepsSplit.Panel2.Controls.Add(pnlSteps);

        leftSplit.Panel1.Controls.Add(queueAndStepsSplit);

        // ---- Bottom Tabs: Separated System Logs, User Logs, Test Logs ----
        var tabLogs = new TabControl { Name = "tabsLogs", Dock = DockStyle.Fill };
        var tpTest = new TabPage("Test Run Logs") { Name = "tabTestLogs" };
        tpTest.Controls.Add(rtbTestLog);
        var tpSystem = new TabPage("System Run Logs") { Name = "tabSystemLogs" };
        tpSystem.Controls.Add(rtbSystemLog);
        var tpUser = new TabPage("User Logs") { Name = "tabUserLogs" };
        tpUser.Controls.Add(rtbUserLog);
        tabLogs.TabPages.AddRange(new[] { tpTest, tpSystem, tpUser });
        leftSplit.Panel2.Controls.Add(tabLogs);

        mainSplit.Panel1.Controls.Add(leftSplit);

        // ---- Right Panel: Accessory Panel (Control Scraper Mock Table & Naming + OpenCV Preview) ----
        var pnlAccessory = new Panel { Name = "pnlAccessoryControlScraper", Dock = DockStyle.Fill, Padding = new Padding(6) };
        var lblAccHeader = new Label
        {
            Name = "lblAccessoryHeader",
            Text = "ACCESSORY PANEL: CONTROL SCRAPER & NAMING (Default: ctrltype_x)",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        var barAcc = new FlowLayoutPanel { Name = "barAccessoryActions", Dock = DockStyle.Top, Height = 38 };
        var txtTargetWin = new TextBox { Name = "txtScrapeWindowFilter", Text = "Calculator", Width = 115 };
        var btnScrape = new Button { Name = "btnScrapeWindow", Text = "Scrape Window", Width = 105, Height = 28, Tag = "accent" };
        btnScrape.Click += (_, _) =>
        {
            IntPtr h = string.IsNullOrWhiteSpace(txtTargetWin.Text)
                ? NativeMethods.GetForegroundWindow()
                : ProcessManagementEngine.FindTopLevelWindow(txtTargetWin.Text, txtTargetWin.Text);
            if (h == IntPtr.Zero) h = NativeMethods.GetForegroundWindow();
            recorder.ScrapeWindowToMockTable(h, commitToRepository: true);
            repo.Save();
            RefreshAccessoryTable();
            UpdateStatus($"Scraped {repo.Count} controls (default naming ctrltype_x).");
        };

        var btnHighlightSel = new Button { Name = "btnHighlightSelectedControl", Text = "Outline (Win32)", Width = 105, Height = 28 };
        btnHighlightSel.Click += (_, _) =>
        {
            if (gridAccessoryControls.CurrentRow?.Cells[0].Value is string cname && !string.IsNullOrWhiteSpace(cname))
            {
                Win32Highlighter.KeywordHighlight(stepController.Context, "@" + cname, cfg.HighlightColor, cfg.HighlightIntervalMs);
            }
        };

        var btnSaveRepo = new Button { Name = "btnSaveControlRepo", Text = "Save Sheet", Width = 86, Height = 28 };
        btnSaveRepo.Click += (_, _) =>
        {
            CommitAccessoryEdits();
            repo.Save();
            UpdateStatus($"Saved {repo.Count} controls to {repo.Store.Description}");
        };

        barAcc.Controls.AddRange(new Control[] { txtTargetWin, btnScrape, btnHighlightSel, btnSaveRepo });

        gridAccessoryControls.Columns.Add("Name", "Control Name (Editable)");
        gridAccessoryControls.Columns.Add("ControlType", "Type");
        gridAccessoryControls.Columns.Add("AutomationId", "AutomationId");
        gridAccessoryControls.Columns.Add("UiaName", "UIA Name / Text");
        gridAccessoryControls.Columns.Add("ImageMode", "Image (file/b=/c=/7zc=)");

        pnlAccessory.Controls.Add(gridAccessoryControls);
        pnlAccessory.Controls.Add(picControlPreview);
        pnlAccessory.Controls.Add(barAcc);
        pnlAccessory.Controls.Add(lblAccHeader);
        mainSplit.Panel2.Controls.Add(pnlAccessory);

        // ---- Status Bar ----
        var status = new StatusStrip { Name = "statusMain", Dock = DockStyle.Bottom };
        status.Items.Add(lblStatus);

        Controls.Add(mainSplit);
        Controls.Add(status);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    private void WireEvents()
    {
        SystemLogger.EntryLogged += entry =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(() =>
            {
                var box = entry.Channel switch
                {
                    LogChannel.System => rtbSystemLog,
                    LogChannel.User => rtbUserLog,
                    _ => rtbTestLog
                };
                box.AppendText(entry + Environment.NewLine);
                box.ScrollToCaret();
            });
        };

        engine.StatusNotification += msg => UpdateStatus(msg);

        lstQueue.SelectedIndexChanged += (_, _) =>
        {
            if (lstQueue.SelectedIndex >= 0 && lstQueue.SelectedIndex < queuedTests.Count)
            {
                LoadTestIntoGridAndController(queuedTests[lstQueue.SelectedIndex]);
            }
        };

        gridSteps.CellEndEdit += (_, _) => SyncGridToCurrentTest();

        gridAccessoryControls.SelectionChanged += (_, _) =>
        {
            if (gridAccessoryControls.CurrentRow?.Cells[0].Value is string name && repo.TryGet(name, out var def))
            {
                var bytes = repo.GetImage(def) ?? repo.GetWindowImage(def);
                if (bytes is { Length: > 0 })
                {
                    try
                    {
                        using var ms = new MemoryStream(bytes);
                        using var src = Image.FromStream(ms);
                        picControlPreview.Image = new Bitmap(src);
                    }
                    catch { }
                }
            }
        };

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && cfg.MinimizeToTray)
            {
                Hide();
                if (cfg.TrayNotifications)
                    trayIcon.ShowBalloonTip(1500, "WinFrame Running in Tray", marqueeText, ToolTipIcon.Info);
            }
        };
    }

    // --------------------------------------------------------------------------------------------
    // Interactive 3-Stage Recording Flow:
    // 1. Hide MainForm & Show Live Docked RecordingBarForm (Mouse Scrape + Window-to-Control + Keyboard)
    // 2. On Stop -> RecordedTestEditorDialog (Add / Move Up / Move Down / Delete steps one by one)
    // 3. On Done -> SaveRecordedTestDialog (Save location + Existing or New TestBed stored in Config)
    // --------------------------------------------------------------------------------------------

    private void StartInteractiveRecordingFlow()
    {
        if (recorder.IsRecording) return;

        // Remove MainForm from screen so it is not recorded initially
        Hide();
        Application.DoEvents();

        TestCase recorded;
        using (var bar = new RecordingBarForm(recorder, cfg))
        {
            recorder.IgnoredWindowHwnds.Add(Handle);
            recorder.StartRecording($"Recorded_{DateTime.Now:HHmmss}");
            bar.ShowDialog();
            recorded = recorder.StopRecording();
            recorder.IgnoredWindowHwnds.Remove(Handle);
        }

        // Stage 2 & 3: Present Test Case Layout Editor (Add/Move/Delete steps) -> Custom Save & TestBed Dialog
        using (var editor = new RecordedTestEditorDialog(recorded, repo, cfg))
        {
            editor.ShowDialog();
        }

        // Restore MainForm and populate the new test case & updated TestBeds/Controls
        Show();
        WindowState = FormWindowState.Normal;
        Activate();

        ReloadTestBedsCombo();
        if (!string.IsNullOrWhiteSpace(recorded.TestBed) && cmbTestBed.Items.Contains(recorded.TestBed))
            cmbTestBed.SelectedItem = recorded.TestBed;

        AddTestToQueue(recorded);
        RefreshAccessoryTable();
        UpdateStatus($"Recorded & configured '{recorded.Name}' ({recorded.Steps.Count} steps, TestBed: {recorded.TestBed}).");
    }

    // --------------------------------------------------------------------------------------------
    // Developer Step Queue (Forward, Inverse, Range 1-3 / 4-1)
    // --------------------------------------------------------------------------------------------

    private void StepNextUi()
    {
        SyncGridToCurrentTest();
        stepController.AutoInverseOnError = chkAutoInverse.Checked;
        var res = stepController.StepNext();
        UpdateStepRowStatus(res);
        UpdateStatus($"Step {res.Index} ({res.Action}): {res.Status} — {res.Message}");
    }

    private void StepPrevInverseUi()
    {
        SyncGridToCurrentTest();
        var res = stepController.StepPrevious(useInverse: true);
        UpdateStepRowStatus(res, isInverse: true);
        UpdateStatus($"Inverse Step {res.Index} ({res.Action}): {res.Status} — {res.Message}");
    }

    private void RunRangeUi(bool useInverse)
    {
        SyncGridToCurrentTest();
        stepController.AutoInverseOnError = chkAutoInverse.Checked;
        string spec = txtRangeSpec.Text.Trim();
        var list = stepController.ExecuteRange(spec, useInverse);
        foreach (var r in list) UpdateStepRowStatus(r, useInverse || spec.Contains('-') && IsDescendingRange(spec));
        UpdateStatus($"Range '{spec}' ({(useInverse ? "INVERSE" : "RUN")}): {list.Count(x => x.IsSuccess)}/{list.Count} passed.");
    }

    private static bool IsDescendingRange(string spec)
    {
        var (s, e) = StepQueueController.ParseRange(spec, 999);
        return s > e;
    }

    private void UpdateStepRowStatus(StepResult res, bool isInverse = false)
    {
        lblStepCursor.Text = $"Cursor: {stepController.Cursor}/{stepController.TotalSteps}";
        int rowIdx = res.Index - 1;
        if (rowIdx >= 0 && rowIdx < gridSteps.Rows.Count)
        {
            gridSteps.Rows[rowIdx].Cells[5].Value = $"{(isInverse ? "[INV] " : "")}{res.Status}: {res.Message}";
            gridSteps.ClearSelection();
            gridSteps.Rows[rowIdx].Selected = true;
        }
    }

    // --------------------------------------------------------------------------------------------
    // Queue Execution, OpenCV Snipping, Highlight Steps, Tray Marquee
    // --------------------------------------------------------------------------------------------

    private async void RunQueueAsync(bool recordOpenCv)
    {
        SyncGridToCurrentTest();
        if (queuedTests.Count == 0) return;

        runCts?.Cancel();
        runCts = new CancellationTokenSource();
        var token = runCts.Token;

        UpdateStatus($"Running Queue ({queuedTests.Count} test(s))...");
        var result = await Task.Run(() =>
            recordOpenCv
                ? engine.ReplayAndRecordOpenCv(queuedTests, token)
                : engine.ExecuteQueue(queuedTests, "Studio Queue", recordOpenCvSnapshots: false, generateHtmlReports: true, token));

        RefreshAccessoryTable();
        UpdateStatus($"Queue Complete: {result.Passed} passed, {result.Failed} failed. Report: {Path.GetFileName(result.ReportPath)}");
        if (cfg.TrayNotifications)
        {
            trayIcon.ShowBalloonTip(2500, "WinFrame Queue Finished",
                $"Passed: {result.Passed} | Failed: {result.Failed}",
                result.Failed == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }
    }

    private void InsertHighlightStepsIntoCurrentTest()
    {
        CommitAccessoryEdits();
        var highlightSteps = Win32Highlighter.CreateHighlightStepsForControls(repo, cfg.HighlightColor, cfg.HighlightIntervalMs);
        if (highlightSteps.Count == 0)
        {
            // Even if repo is empty, insert a sample HIGHLIGHT row
            stepController.Test.Highlight("@num8Button", cfg.HighlightColor, cfg.HighlightIntervalMs);
        }
        else
        {
            foreach (var hs in highlightSteps.Take(15))
                stepController.Test.AddStep(hs);
        }
        LoadTestIntoGridAndController(stepController.Test);
        UpdateStatus($"Inserted Win32 HIGHLIGHT keyword steps (active when highlight=1/true).");
    }

    private void SnipRegionInteractive()
    {
        WindowState = FormWindowState.Minimized;
        Thread.Sleep(250);
        var (png, bounds) = OpenCvAutomation.InteractiveSnipRegion();
        WindowState = FormWindowState.Normal;
        Activate();

        if (png.Length > 0)
        {
            string name = repo.GenerateDefaultName("snip");
            var def = new ControlDefinition
            {
                Name = name,
                Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [ControlDefinition.Keys.ControlType] = "SnipRegion",
                    [ControlDefinition.Keys.BoundingRectangle] = $"{bounds.Left},{bounds.Top},{bounds.Width},{bounds.Height}"
                }
            };
            repo.SetImages(def, png, null);
            repo.Save();
            RefreshAccessoryTable();
            stepController.Test.CvClickImage("@" + name);
            LoadTestIntoGridAndController(stepController.Test);
            UpdateStatus($"Snipped region {bounds} -> saved as '@{name}' in Control Sheet.");
        }
    }

    private void SnapshotScreenQuick()
    {
        string path = OpenCvAutomation.KeywordSnapshotScreen(stepController.Context);
        UpdateStatus($"Saved full-screen OpenCV snapshot: {path}");
    }

    private void GenerateReportsNow()
    {
        var q = new QueueRunResult { Name = "ManualSnapshotReport" };
        q.MarkStarted();
        q.MarkFinished(TestStatus.Passed);
        string path = HtmlReportGenerator.GenerateBothThemes(q, cfg.ReportPath, cfg.ReportTheme);
        UpdateStatus($"Generated Dark & Light HTML reports: {path}");
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    private void ToggleApiServer()
    {
        if (apiServer is { IsRunning: true })
        {
            apiServer.Stop();
            UpdateStatus("Remote API Service stopped.");
        }
        else
        {
            apiServer = new WinFrameApiServer(engine, cfg.Service.Urls);
            apiServer.Start();
            UpdateStatus($"Remote API Service listening on {apiServer.ListenUrl}");
        }
    }

    private void OpenSettings(int tab)
    {
        using var dlg = new SettingsDialog(cfg, tab);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            chkWin32Highlight.Checked = cfg.IsHighlightEnabled;
            ReloadTestBedsCombo();
            ThemeEngine.Apply(this, ThemeEngine.GetTheme(cfg.Theme));
        }
    }

    private void ReloadTestBedsCombo()
    {
        cmbTestBed.Items.Clear();
        foreach (var b in ConfigManager.LoadTestBeds()) cmbTestBed.Items.Add(b.Name);
        if (cmbTestBed.Items.Count > 0) cmbTestBed.SelectedIndex = 0;
    }

    private void LoadTestSheetsDialog()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Test Sheets (*.csv;*.xlsx;*.json;*.txt)|*.csv;*.xlsx;*.json;*.txt|All Files (*.*)|*.*",
            Multiselect = true
        };
        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            foreach (var f in ofd.FileNames)
                foreach (var t in TestSheetLoader.LoadFromFile(f))
                    AddTestToQueue(t);
        }
    }

    private void SaveSelectedTestDialog()
    {
        SyncGridToCurrentTest();
        using var sfd = new SaveFileDialog
        {
            Filter = "CSV Sheet (*.csv)|*.csv|Excel Workbook (*.xlsx)|*.xlsx|JSON Test (*.json)|*.json",
            FileName = stepController.Test.Name + ".csv"
        };
        if (sfd.ShowDialog(this) == DialogResult.OK)
        {
            string ext = Path.GetExtension(sfd.FileName).ToLowerInvariant();
            if (ext == ".xlsx") TestSheetLoader.SaveXlsx(new[] { stepController.Test }, sfd.FileName);
            else if (ext == ".json") TestSheetLoader.SaveJson(stepController.Test, sfd.FileName);
            else TestSheetLoader.SaveCsv(stepController.Test, sfd.FileName);
            UpdateStatus($"Saved test sheet to {sfd.FileName}");
        }
    }

    private void AddTestToQueue(TestCase tc)
    {
        queuedTests.Add(tc);
        lstQueue.Items.Add(tc.Name);
        lstQueue.SelectedIndex = queuedTests.Count - 1;
    }

    private void LoadTestIntoGridAndController(TestCase tc)
    {
        stepController.LoadTest(tc);
        gridSteps.Rows.Clear();
        foreach (var s in tc.Steps)
        {
            gridSteps.Rows.Add(s.Index, s.Action, s.Target, s.Value, s.OptionsToString(), "");
        }
        lblStepCursor.Text = $"Cursor: {stepController.Cursor}/{stepController.TotalSteps}";
    }

    private void SyncGridToCurrentTest()
    {
        var list = new List<TestStep>();
        int idx = 1;
        foreach (DataGridViewRow r in gridSteps.Rows)
        {
            if (r.IsNewRow) continue;
            string act = r.Cells[1].Value?.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(act)) continue;
            list.Add(new TestStep
            {
                Index = idx++,
                Action = act.ToUpperInvariant(),
                Target = r.Cells[2].Value?.ToString()?.Trim() ?? "",
                Value = r.Cells[3].Value?.ToString()?.Trim() ?? "",
                Options = TestStep.ParseOptions(r.Cells[4].Value?.ToString())
            });
        }
        stepController.Test.Steps = list;
    }

    private void RefreshAccessoryTable()
    {
        gridAccessoryControls.Rows.Clear();
        foreach (var c in repo.All.OrderBy(x => x.Name))
        {
            gridAccessoryControls.Rows.Add(
                c.Name,
                c.Get(ControlDefinition.Keys.ControlType),
                c.Get(ControlDefinition.Keys.AutomationId),
                c.Get(ControlDefinition.Keys.Name),
                ImageCodec.DetectMode(c.Image)?.ToString() ?? "None");
        }
    }

    private void CommitAccessoryEdits()
    {
        var current = repo.All.OrderBy(x => x.Name).ToList();
        for (int i = 0; i < Math.Min(current.Count, gridAccessoryControls.Rows.Count); i++)
        {
            string newName = gridAccessoryControls.Rows[i].Cells[0].Value?.ToString()?.Trim() ?? "";
            if (!string.IsNullOrEmpty(newName) && newName != current[i].Name)
            {
                repo.Rename(current[i].Name, newName);
            }
        }
        RefreshAccessoryTable();
    }

    private void UpdateStatus(string message)
    {
        if (IsDisposed) return;
        marqueeText = $"  {message}  •";
        if (InvokeRequired) { BeginInvoke(() => UpdateStatus(message)); return; }
        lblStatus.Text = message;
        string clipped = message.Length > 60 ? message[..60] : message;
        trayIcon.Text = clipped;
    }

    private void UpdateTrayScrollingIcon()
    {
        if (!cfg.ScrollTrayMessages || string.IsNullOrEmpty(marqueeText)) return;
        try
        {
            marqueeOffset = (marqueeOffset + 1) % Math.Max(1, marqueeText.Length);
            string rotated = marqueeText[marqueeOffset..] + marqueeText[..marqueeOffset];

            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(18, 14, 22));
                using var borderPen = new Pen(Color.FromArgb(245, 158, 11), 2f);
                g.DrawRectangle(borderPen, 1, 1, 29, 29);
                using var font = new Font("Consolas", 8f, FontStyle.Bold);
                using var brush = new SolidBrush(Color.FromArgb(245, 158, 11));
                g.DrawString(rotated[..Math.Min(4, rotated.Length)], font, brush, new PointF(2, 9));
            }
            IntPtr hIcon = bmp.GetHicon();
            var prev = trayIcon.Icon;
            trayIcon.Icon = (Icon)Icon.FromHandle(hIcon).Clone();
            DestroyIcon(hIcon);
            if (prev != null && prev != SystemIcons.Application) prev.Dispose();
        }
        catch { }
    }

    private static TestCase CreateSampleCalculatorTest()
    {
        var tc = new TestCase("Sample_Keyword_Demo") { TestBed = "Calculator" };
        tc.AddStep("SET_VAR", "greeting", "Hello WinFrame", description: "Set greeting variable");
        tc.AddStep("ASSERT_VAR", "greeting", "Hello WinFrame", description: "Verify variable");
        tc.AddStep("HIGHLIGHT", "@num8Button", "#F59E0B|250", description: "Win32 GDI outline highlight when highlight=1/true");
        tc.AddStep("LOG", "WinFrame Ready", "${greeting}", description: "Write to test log");
        return tc;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        marqueeTimer.Stop();
        trayIcon.Visible = false;
        recorder.Dispose();
        apiServer?.Dispose();
        ProcessManagementEngine.Shared.Dispose();
        base.OnFormClosing(e);
    }
}

