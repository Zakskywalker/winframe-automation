using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using WinFrame.Core.Config;
using WinFrame.Core.Controls;
using WinFrame.Core.Keywords;
using WinFrame.Core.Native;
using WinFrame.Core.Recording;
using WinFrame.Core.Themes;

namespace WinFrame.App;

/// <summary>
/// Stage 1 of the Record Flow:
/// Compact, always-on-top Recording Dock Bar positioned at the bottom or right side of the screen
/// while <see cref="MainForm"/> is hidden (so <see cref="MainForm"/> is removed from initial recording).
/// Displays live Mouse-Position Scrape + Window-to-Control Coordinates, allows live inline editing of
/// recorded steps, and transitions to <see cref="RecordedTestEditorDialog"/> when the user clicks Stop.
/// </summary>
public sealed class RecordingBarForm : Form
{
    private readonly TestRecorder recorder;
    private readonly AppConfig cfg;
    private bool dockedRightSide;

    private readonly Label lblRecBadge = new()
    {
        Name = "lblRecBadge",
        Text = "● REC (0 steps)",
        ForeColor = Color.FromArgb(248, 113, 113),
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        AutoSize = true,
        Margin = new Padding(4, 6, 8, 0)
    };

    private readonly Label lblHoverInfo = new()
    {
        Name = "lblHoverInfo",
        Text = "Move mouse over target window to scrape control & Window-to-Control position...",
        ForeColor = Color.FromArgb(252, 211, 77),
        Font = new Font("Consolas", 8.5f),
        AutoSize = true,
        Margin = new Padding(4, 7, 8, 0)
    };

    private readonly DataGridView gridLiveSteps = new()
    {
        Name = "gridLiveRecordedSteps",
        Dock = DockStyle.Fill,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AllowUserToAddRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };

    private readonly TextBox txtQuickInput = new()
    {
        Name = "txtQuickKeyboardInput",
        PlaceholderText = "Quick keyboard text / value...",
        Width = 175
    };

    public RecordingBarForm(TestRecorder recorder, AppConfig cfg)
    {
        this.recorder = recorder;
        this.cfg = cfg;

        Name = "frmRecordingBar";
        Text = "WinFrame Live Recorder — Mouse Scrape + Window-to-Control Position + Keyboard Input";
        TopMost = true;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = true;

        var topStrip = new FlowLayoutPanel
        {
            Name = "barRecordingControls",
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(4),
            WrapContents = false
        };

        var btnStop = new Button
        {
            Name = "btnStopRecordingBar",
            Text = "■ Stop Recording & Edit Steps",
            Width = 195,
            Height = 28,
            BackColor = Color.FromArgb(220, 38, 38),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnStop.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnAddType = new Button
        {
            Name = "btnAddQuickTypeStep",
            Text = "+ Type Input",
            Width = 92,
            Height = 28
        };
        btnAddType.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(txtQuickInput.Text))
            {
                string lastTarget = recorder.CurrentTest.Steps.LastOrDefault()?.Target ?? "";
                recorder.AppendStep("TYPE", lastTarget, txtQuickInput.Text, $"Typed '{txtQuickInput.Text}'");
                txtQuickInput.Clear();
            }
        };

        var btnToggleDock = new Button
        {
            Name = "btnToggleBarDock",
            Text = "Dock: Bottom ⇄ Side",
            Width = 135,
            Height = 28
        };
        btnToggleDock.Click += (_, _) =>
        {
            dockedRightSide = !dockedRightSide;
            ApplyDockPlacement();
        };

        topStrip.Controls.AddRange(new Control[]
        {
            btnStop, lblRecBadge, txtQuickInput, btnAddType, btnToggleDock, lblHoverInfo
        });

        gridLiveSteps.Columns.Add("Idx", "#");
        gridLiveSteps.Columns[0].FillWeight = 10;
        gridLiveSteps.Columns[0].ReadOnly = true;
        gridLiveSteps.Columns.Add("Action", "Action (Editable)");
        gridLiveSteps.Columns[1].FillWeight = 22;
        gridLiveSteps.Columns.Add("Target", "Control (@name)");
        gridLiveSteps.Columns[2].FillWeight = 26;
        gridLiveSteps.Columns.Add("Value", "Data Input / WinRel X|Y (Editable)");
        gridLiveSteps.Columns[3].FillWeight = 42;

        gridLiveSteps.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < recorder.CurrentTest.Steps.Count)
            {
                var step = recorder.CurrentTest.Steps[e.RowIndex];
                step.Action = (gridLiveSteps.Rows[e.RowIndex].Cells[1].Value?.ToString() ?? step.Action).Trim().ToUpperInvariant();
                step.Target = (gridLiveSteps.Rows[e.RowIndex].Cells[2].Value?.ToString() ?? step.Target).Trim();
                step.Value = gridLiveSteps.Rows[e.RowIndex].Cells[3].Value?.ToString() ?? "";
            }
        };

        Controls.Add(gridLiveSteps);
        Controls.Add(topStrip);

        recorder.StepRecorded += OnStepRecorded;
        recorder.HoverControlChanged += OnHoverControlChanged;

        Shown += (_, _) =>
        {
            recorder.IgnoredWindowHwnds.Add(Handle);
            ApplyDockPlacement();
        };

        FormClosed += (_, _) =>
        {
            recorder.StepRecorded -= OnStepRecorded;
            recorder.HoverControlChanged -= OnHoverControlChanged;
            recorder.IgnoredWindowHwnds.Remove(Handle);
        };

        ThemeEngine.Apply(this, ThemeEngine.GetTheme(cfg.Theme));
        btnStop.BackColor = Color.FromArgb(220, 38, 38);
        btnStop.ForeColor = Color.White;
    }

    private void ApplyDockPlacement()
    {
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        if (!dockedRightSide)
        {
            // Bottom bar layout
            int height = 185;
            Bounds = new Rectangle(workArea.Left + 20, workArea.Bottom - height - 8, Math.Max(780, workArea.Width - 40), height);
        }
        else
        {
            // Right-side vertical bar layout
            int width = 460;
            Bounds = new Rectangle(workArea.Right - width - 8, workArea.Top + 40, width, Math.Max(420, workArea.Height - 80));
        }
    }

    private void OnStepRecorded(TestStep step)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            gridLiveSteps.Rows.Add(step.Index, step.Action, step.Target, step.Value);
            if (gridLiveSteps.Rows.Count > 0)
                gridLiveSteps.FirstDisplayedScrollingRowIndex = gridLiveSteps.Rows.Count - 1;
            lblRecBadge.Text = $"● REC ({recorder.CurrentTest.Steps.Count} steps)";
        });
    }

    private void OnHoverControlChanged(HoveredControlInfo hover)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            lblHoverInfo.Text =
                $"@{hover.SuggestedName} [{hover.ControlType}] " +
                $"WinRel:({hover.WindowRelativePoint.X},{hover.WindowRelativePoint.Y}) " +
                $"CtrlRel:({hover.ControlRelativePoint.X},{hover.ControlRelativePoint.Y}) " +
                $"\"{Truncate(hover.UiaName, 26)}\"";
        });
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "...";
}

/// <summary>
/// Stage 2 of the Record Flow:
/// Post-Recording Test Case Layout &amp; Step Editor Dialog.
/// Presents all recorded steps with their Action, Control (<c>@name</c>), and Step Function / Value,
/// allowing the user to add, edit, move up/down, or delete steps one by one, and inspect/rename the scraped controls.
/// When done, opens <see cref="SaveRecordedTestDialog"/>.
/// </summary>
public sealed class RecordedTestEditorDialog : Form
{
    private readonly TestCase testCase;
    private readonly ControlRepository repo;
    private readonly AppConfig cfg;
    private readonly KeywordContext previewCtx;

    private readonly DataGridView gridSteps = new()
    {
        Name = "gridRecordedTestLayout",
        Dock = DockStyle.Fill,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AllowUserToAddRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false
    };

    private readonly PropertyGridOrDetailsPanel pnlControlDetails;

    public string? SavedFilePath { get; private set; }

    public RecordedTestEditorDialog(TestCase recordedTest, ControlRepository repo, AppConfig cfg)
    {
        this.testCase = recordedTest;
        this.repo = repo;
        this.cfg = cfg;
        this.previewCtx = new KeywordContext(repo, cfg);

        Name = "dlgRecordedTestEditor";
        Text = $"Recorded Test Case Layout & Step Editor — {testCase.Name} ({testCase.Steps.Count} steps)";
        Size = new Size(1120, 640);
        StartPosition = FormStartPosition.CenterScreen;

        var topBar = new FlowLayoutPanel
        {
            Name = "barStepEditorToolbar",
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(8, 7, 8, 4)
        };

        var btnAdd = new Button { Name = "btnAddStep", Text = "+ Add Step", Width = 100, Height = 30 };
        btnAdd.Click += (_, _) => AddStepRow();

        var btnMoveUp = new Button { Name = "btnMoveStepUp", Text = "▲ Move Up", Width = 100, Height = 30 };
        btnMoveUp.Click += (_, _) => MoveSelectedStep(-1);

        var btnMoveDown = new Button { Name = "btnMoveStepDown", Text = "▼ Move Down", Width = 110, Height = 30 };
        btnMoveDown.Click += (_, _) => MoveSelectedStep(+1);

        var btnDelete = new Button { Name = "btnDeleteStep", Text = "✕ Delete Step", Width = 110, Height = 30 };
        btnDelete.Click += (_, _) => DeleteSelectedStep();

        var btnOutline = new Button { Name = "btnOutlineStepControl", Text = "★ Outline Control (Win32)", Width = 165, Height = 30 };
        btnOutline.Click += (_, _) => OutlineSelectedControl();

        var lblHint = new Label
        {
            Text = "Edit any cell directly, or use Add / Move Up / Move Down / Delete to refine steps one by one.",
            AutoSize = true,
            Margin = new Padding(12, 7, 0, 0)
        };

        topBar.Controls.AddRange(new Control[] { btnAdd, btnMoveUp, btnMoveDown, btnDelete, btnOutline, lblHint });

        var split = new SplitContainer
        {
            Name = "splitTestLayoutAndControlInfo",
            Dock = DockStyle.Fill
        };
        Shown += (_, _) =>
        {
            try { if (split.Width > 400) split.SplitterDistance = Math.Min(760, split.Width - 300); }
            catch { }
        };

        gridSteps.Columns.Add("Index", "#");
        gridSteps.Columns[0].FillWeight = 8;
        gridSteps.Columns[0].ReadOnly = true;
        gridSteps.Columns.Add("Action", "Action / Step Function");
        gridSteps.Columns[1].FillWeight = 22;
        gridSteps.Columns.Add("Target", "Control (@name / Locator)");
        gridSteps.Columns[2].FillWeight = 22;
        gridSteps.Columns.Add("Value", "Data Input / Coordinates / Value");
        gridSteps.Columns[3].FillWeight = 24;
        gridSteps.Columns.Add("Description", "Step Description & Window-to-Control Pos");
        gridSteps.Columns[4].FillWeight = 24;

        gridSteps.CellEndEdit += (_, _) => SyncGridToModel();
        gridSteps.SelectionChanged += (_, _) => UpdateControlInspector();

        split.Panel1.Controls.Add(gridSteps);

        pnlControlDetails = new PropertyGridOrDetailsPanel(repo);
        split.Panel2.Controls.Add(pnlControlDetails);

        var bottomBar = new FlowLayoutPanel
        {
            Name = "barEditorBottom",
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };

        var btnDoneSave = new Button
        {
            Name = "btnDoneOpenSaveDialog",
            Text = "Done — Choose Save Location & TestBed ->",
            Width = 275,
            Height = 32,
            Tag = "accent"
        };
        btnDoneSave.Click += (_, _) =>
        {
            SyncGridToModel();
            using var saveDlg = new SaveRecordedTestDialog(testCase, cfg);
            if (saveDlg.ShowDialog(this) == DialogResult.OK)
            {
                SavedFilePath = saveDlg.SavedFilePath;
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        var btnKeepInMemory = new Button
        {
            Name = "btnKeepInQueueOnly",
            Text = "Keep in Queue Without Saving File",
            Width = 220,
            Height = 32
        };
        btnKeepInMemory.Click += (_, _) =>
        {
            SyncGridToModel();
            DialogResult = DialogResult.OK;
            Close();
        };

        bottomBar.Controls.Add(btnDoneSave);
        bottomBar.Controls.Add(btnKeepInMemory);

        Controls.Add(split);
        Controls.Add(topBar);
        Controls.Add(bottomBar);

        PopulateGridFromModel();
        ThemeEngine.Apply(this, ThemeEngine.GetTheme(cfg.Theme));
    }

    private void PopulateGridFromModel()
    {
        gridSteps.Rows.Clear();
        for (int i = 0; i < testCase.Steps.Count; i++)
        {
            var s = testCase.Steps[i];
            s.Index = i + 1;
            gridSteps.Rows.Add(s.Index, s.Action, s.Target, s.Value, s.Description);
        }
        UpdateControlInspector();
    }

    private void SyncGridToModel()
    {
        var list = new List<TestStep>();
        int idx = 1;
        foreach (DataGridViewRow row in gridSteps.Rows)
        {
            if (row.IsNewRow) continue;
            string action = (row.Cells[1].Value?.ToString() ?? "CLICK").Trim().ToUpperInvariant();
            string target = (row.Cells[2].Value?.ToString() ?? "").Trim();
            string val = row.Cells[3].Value?.ToString() ?? "";
            string desc = row.Cells[4].Value?.ToString() ?? "";
            list.Add(new TestStep
            {
                Index = idx++,
                Action = action,
                Target = target,
                Value = val,
                Description = desc
            });
        }
        testCase.Steps = list;
    }

    private void AddStepRow()
    {
        SyncGridToModel();
        int insertAt = gridSteps.CurrentRow != null ? gridSteps.CurrentRow.Index + 1 : testCase.Steps.Count;
        var newStep = new TestStep
        {
            Index = insertAt + 1,
            Action = "CLICK",
            Target = testCase.Steps.LastOrDefault()?.Target ?? "@button_1",
            Value = "",
            Description = "Added step"
        };
        testCase.Steps.Insert(Math.Clamp(insertAt, 0, testCase.Steps.Count), newStep);
        PopulateGridFromModel();
        if (insertAt >= 0 && insertAt < gridSteps.Rows.Count)
            gridSteps.CurrentCell = gridSteps.Rows[insertAt].Cells[1];
    }

    private void MoveSelectedStep(int delta)
    {
        if (gridSteps.CurrentRow == null) return;
        SyncGridToModel();
        int idx = gridSteps.CurrentRow.Index;
        int targetIdx = idx + delta;
        if (targetIdx < 0 || targetIdx >= testCase.Steps.Count) return;

        (testCase.Steps[idx], testCase.Steps[targetIdx]) = (testCase.Steps[targetIdx], testCase.Steps[idx]);
        PopulateGridFromModel();
        gridSteps.CurrentCell = gridSteps.Rows[targetIdx].Cells[1];
    }

    private void DeleteSelectedStep()
    {
        if (gridSteps.CurrentRow == null) return;
        SyncGridToModel();
        int idx = gridSteps.CurrentRow.Index;
        if (idx >= 0 && idx < testCase.Steps.Count)
        {
            testCase.Steps.RemoveAt(idx);
            PopulateGridFromModel();
            if (gridSteps.Rows.Count > 0)
            {
                int nextIdx = Math.Clamp(idx, 0, gridSteps.Rows.Count - 1);
                gridSteps.CurrentCell = gridSteps.Rows[nextIdx].Cells[1];
            }
        }
    }

    private void OutlineSelectedControl()
    {
        if (gridSteps.CurrentRow?.Cells[2].Value is string target && !string.IsNullOrWhiteSpace(target))
        {
            Win32Highlighter.KeywordHighlight(previewCtx, target, cfg.HighlightColor, cfg.HighlightIntervalMs);
        }
    }

    private void UpdateControlInspector()
    {
        string target = gridSteps.CurrentRow?.Cells[2].Value?.ToString()?.Trim() ?? "";
        pnlControlDetails.ShowControl(target.TrimStart('@'));
    }

    private sealed class PropertyGridOrDetailsPanel : Panel
    {
        private readonly ControlRepository repo;
        private readonly TextBox txtCtrlName = new() { Name = "txtInspectorCtrlName", Dock = DockStyle.Top };
        private readonly TextBox txtDetails = new()
        {
            Name = "txtInspectorAttributes",
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 8.5f)
        };
        private readonly PictureBox picSnap = new()
        {
            Name = "picInspectorSnapshot",
            Dock = DockStyle.Bottom,
            Height = 140,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        private string? currentControlName;

        public PropertyGridOrDetailsPanel(ControlRepository repo)
        {
            this.repo = repo;
            Dock = DockStyle.Fill;
            Padding = new Padding(8);

            var lblTitle = new Label
            {
                Text = "SCRAPED CONTROL & WINDOW-TO-CONTROL POSITION",
                Dock = DockStyle.Top,
                Height = 24,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            var renamePanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
            txtCtrlName.Width = 175;
            var btnRename = new Button { Text = "Rename Control", Width = 115, Height = 26 };
            btnRename.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(currentControlName) && !string.IsNullOrWhiteSpace(txtCtrlName.Text))
                {
                    repo.Rename(currentControlName, txtCtrlName.Text.Trim());
                    repo.Save();
                    currentControlName = txtCtrlName.Text.Trim();
                    ShowControl(currentControlName);
                }
            };
            renamePanel.Controls.Add(txtCtrlName);
            renamePanel.Controls.Add(btnRename);

            Controls.Add(txtDetails);
            Controls.Add(picSnap);
            Controls.Add(renamePanel);
            Controls.Add(lblTitle);
        }

        public void ShowControl(string controlName)
        {
            currentControlName = controlName;
            txtCtrlName.Text = controlName;
            if (string.IsNullOrWhiteSpace(controlName) || !repo.TryGet(controlName, out var def))
            {
                txtDetails.Text = "Select a step referencing a @control to view its scraped UIA attributes and Window-to-Control relative coordinates.";
                picSnap.Image = null;
                return;
            }

            txtDetails.Text = string.Join(
                Environment.NewLine,
                def.Attributes.OrderBy(k => k.Key).Select(kv => $"{kv.Key}: {kv.Value}"));

            var bytes = repo.GetImage(def) ?? repo.GetWindowImage(def);
            if (bytes is { Length: > 0 })
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    using var src = Image.FromStream(ms);
                    picSnap.Image = new Bitmap(src);
                }
                catch { picSnap.Image = null; }
            }
            else
            {
                picSnap.Image = null;
            }
        }
    }
}

/// <summary>
/// Stage 3 of the Record Flow:
/// Custom Save &amp; TestBed Repository Dialog.
/// Shows the user the Save Location and TestBed Repository, or lets them choose a new TestBed location
/// and give it a name to store permanently in the <see cref="ConfigManager"/> TestBed collection.
/// </summary>
public sealed class SaveRecordedTestDialog : Form
{
    private readonly TestCase testCase;
    private readonly AppConfig cfg;
    private readonly IReadOnlyList<TestBedConfig> existingBeds;

    private readonly TextBox txtTestName = new() { Name = "txtSaveTestName", Width = 340 };
    private readonly ComboBox cmbFormat = new() { Name = "cmbSaveFormat", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly TextBox txtSaveDir = new() { Name = "txtSaveDirectory", Width = 340 };

    private readonly RadioButton rbExistingBed = new() { Name = "rbExistingTestBed", Text = "Use Existing TestBed in Config Collection:", Checked = true, AutoSize = true };
    private readonly ComboBox cmbExistingBeds = new() { Name = "cmbExistingTestBeds", DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly Label lblExistingBedSummary = new() { Name = "lblExistingBedSummary", AutoSize = true, ForeColor = Color.FromArgb(148, 163, 184) };

    private readonly RadioButton rbNewBed = new() { Name = "rbCreateNewTestBed", Text = "Choose New TestBed Location & Store in Config Collection:", AutoSize = true };
    private readonly TextBox txtNewBedName = new() { Name = "txtNewTestBedName", Width = 240, PlaceholderText = "e.g. Calculator_Prod or MyERP_TestBed" };
    private readonly TextBox txtNewBedExe = new() { Name = "txtNewTestBedExe", Width = 300, PlaceholderText = "C:\\Path\\To\\Application.exe" };
    private readonly TextBox txtNewBedWorkDir = new() { Name = "txtNewTestBedWorkDir", Width = 300, PlaceholderText = "Optional working folder..." };
    private readonly TextBox txtNewBedArgs = new() { Name = "txtNewTestBedArgs", Width = 300, PlaceholderText = "Optional launch arguments..." };

    public string? SavedFilePath { get; private set; }

    public SaveRecordedTestDialog(TestCase testCase, AppConfig cfg)
    {
        this.testCase = testCase;
        this.cfg = cfg;
        this.existingBeds = ConfigManager.LoadTestBeds();

        Name = "dlgSaveRecordedTest";
        Text = "Save Recorded Test Case & Configure TestBed Repository";
        Size = new Size(680, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        string defaultSamplesDir = Path.Combine(AppContext.BaseDirectory, "samples");
        string? walk = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(walk) && !File.Exists(Path.Combine(walk, "WinFrame.sln")))
        {
            walk = Path.GetDirectoryName(walk);
        }
        if (!string.IsNullOrEmpty(walk) && Directory.Exists(Path.Combine(walk, "samples")))
            defaultSamplesDir = Path.Combine(walk, "samples");
        Directory.CreateDirectory(defaultSamplesDir);

        txtTestName.Text = string.IsNullOrWhiteSpace(testCase.Name) ? $"Recorded_{DateTime.Now:yyyyMMdd_HHmmss}" : testCase.Name;
        txtSaveDir.Text = defaultSamplesDir;

        cmbFormat.Items.AddRange(new object[]
        {
            "CSV Test Sheet (.csv)",
            "JSON Test Case (.json)",
            "Excel Workbook (.xlsx)",
            "All Formats (.csv + .json + .xlsx)"
        });
        cmbFormat.SelectedIndex = 0;

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };

        int y = 14;
        pnl.Controls.Add(new Label { Text = "1. Test Case Name & Save Location", Font = new Font("Segoe UI", 10f, FontStyle.Bold), AutoSize = true, Location = new Point(16, y) });
        y += 30;

        pnl.Controls.Add(new Label { Text = "Test Case Name:", AutoSize = true, Location = new Point(16, y + 4) });
        txtTestName.Location = new Point(180, y);
        pnl.Controls.Add(txtTestName);
        y += 34;

        pnl.Controls.Add(new Label { Text = "Save Format:", AutoSize = true, Location = new Point(16, y + 4) });
        cmbFormat.Location = new Point(180, y);
        pnl.Controls.Add(cmbFormat);
        y += 34;

        pnl.Controls.Add(new Label { Text = "Save Folder:", AutoSize = true, Location = new Point(16, y + 4) });
        txtSaveDir.Location = new Point(180, y);
        var btnBrowseSaveDir = new Button { Name = "btnBrowseSaveFolder", Text = "Browse...", Location = new Point(530, y - 1), Width = 95, Height = 26 };
        btnBrowseSaveDir.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog { SelectedPath = txtSaveDir.Text };
            if (fbd.ShowDialog(this) == DialogResult.OK) txtSaveDir.Text = fbd.SelectedPath;
        };
        pnl.Controls.Add(txtSaveDir);
        pnl.Controls.Add(btnBrowseSaveDir);
        y += 44;

        // Section 2: TestBed Repository Selection
        pnl.Controls.Add(new Label { Text = "2. TestBed Repository (Existing or New in Config Collection)", Font = new Font("Segoe UI", 10f, FontStyle.Bold), AutoSize = true, Location = new Point(16, y) });
        y += 30;

        rbExistingBed.Location = new Point(16, y);
        cmbExistingBeds.Location = new Point(330, y - 2);
        foreach (var b in existingBeds) cmbExistingBeds.Items.Add(b.Name);
        if (cmbExistingBeds.Items.Count > 0) cmbExistingBeds.SelectedIndex = 0;
        pnl.Controls.Add(rbExistingBed);
        pnl.Controls.Add(cmbExistingBeds);
        y += 30;

        lblExistingBedSummary.Location = new Point(40, y);
        pnl.Controls.Add(lblExistingBedSummary);
        y += 32;

        rbNewBed.Location = new Point(16, y);
        pnl.Controls.Add(rbNewBed);
        y += 30;

        var pnlNewBed = new Panel { Name = "pnlNewTestBedFields", Location = new Point(36, y), Size = new Size(595, 145), Enabled = false };
        pnlNewBed.Controls.Add(new Label { Text = "New TestBed Name:", AutoSize = true, Location = new Point(0, 6) });
        txtNewBedName.Location = new Point(155, 2);
        pnlNewBed.Controls.Add(txtNewBedName);

        pnlNewBed.Controls.Add(new Label { Text = "App / TestBed Path:", AutoSize = true, Location = new Point(0, 38) });
        txtNewBedExe.Location = new Point(155, 34);
        var btnBrowseExe = new Button { Name = "btnBrowseTestBedExe", Text = "Browse EXE...", Location = new Point(465, 33), Width = 110, Height = 26 };
        btnBrowseExe.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Applications (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All Files (*.*)|*.*",
                Title = "Select Target Application for New TestBed"
            };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                txtNewBedExe.Text = ofd.FileName;
                if (string.IsNullOrWhiteSpace(txtNewBedWorkDir.Text))
                    txtNewBedWorkDir.Text = Path.GetDirectoryName(ofd.FileName) ?? "";
                if (string.IsNullOrWhiteSpace(txtNewBedName.Text))
                    txtNewBedName.Text = Path.GetFileNameWithoutExtension(ofd.FileName);
            }
        };
        pnlNewBed.Controls.Add(txtNewBedExe);
        pnlNewBed.Controls.Add(btnBrowseExe);

        pnlNewBed.Controls.Add(new Label { Text = "Working Folder:", AutoSize = true, Location = new Point(0, 70) });
        txtNewBedWorkDir.Location = new Point(155, 66);
        var btnBrowseWorkDir = new Button { Name = "btnBrowseTestBedFolder", Text = "Browse...", Location = new Point(465, 65), Width = 110, Height = 26 };
        btnBrowseWorkDir.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog(this) == DialogResult.OK) txtNewBedWorkDir.Text = fbd.SelectedPath;
        };
        pnlNewBed.Controls.Add(txtNewBedWorkDir);
        pnlNewBed.Controls.Add(btnBrowseWorkDir);

        pnlNewBed.Controls.Add(new Label { Text = "Launch Arguments:", AutoSize = true, Location = new Point(0, 102) });
        txtNewBedArgs.Location = new Point(155, 98);
        pnlNewBed.Controls.Add(txtNewBedArgs);

        pnl.Controls.Add(pnlNewBed);

        void UpdateBedUi()
        {
            cmbExistingBeds.Enabled = rbExistingBed.Checked;
            pnlNewBed.Enabled = rbNewBed.Checked;
            var sel = existingBeds.FirstOrDefault(b => b.Name == cmbExistingBeds.SelectedItem?.ToString());
            lblExistingBedSummary.Text = sel != null
                ? $"App: {sel.ApplicationPath}  |  Process: {sel.ProcessName}  |  WorkDir: {(string.IsNullOrEmpty(sel.WorkingDirectory) ? "(default)" : sel.WorkingDirectory)}"
                : "No existing TestBed selected.";
        }

        rbExistingBed.CheckedChanged += (_, _) => UpdateBedUi();
        rbNewBed.CheckedChanged += (_, _) => UpdateBedUi();
        cmbExistingBeds.SelectedIndexChanged += (_, _) => UpdateBedUi();
        UpdateBedUi();

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };

        var btnSave = new Button
        {
            Name = "btnConfirmSaveTestAndBed",
            Text = "Save Test Case & TestBed",
            Width = 195,
            Height = 32,
            Tag = "accent"
        };
        btnSave.Click += (_, _) => ExecuteSave();

        var btnCancel = new Button { Name = "btnCancelSaveDialog", Text = "Cancel", Width = 95, Height = 32 };
        btnCancel.Click += (_, _) => Close();

        bottom.Controls.Add(btnSave);
        bottom.Controls.Add(btnCancel);

        Controls.Add(pnl);
        Controls.Add(bottom);

        ThemeEngine.Apply(this, ThemeEngine.GetTheme(cfg.Theme));
    }

    private void ExecuteSave()
    {
        string testName = string.IsNullOrWhiteSpace(txtTestName.Text) ? "RecordedTest" : txtTestName.Text.Trim();
        testCase.Name = testName;

        // 1. Handle TestBed selection or creation in Config Collection
        if (rbNewBed.Checked)
        {
            string bedName = txtNewBedName.Text.Trim();
            if (string.IsNullOrWhiteSpace(bedName))
            {
                MessageBox.Show(this, "Please provide a name for the new TestBed.", "TestBed Name Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string exePath = txtNewBedExe.Text.Trim();
            var newBed = new TestBedConfig
            {
                Name = bedName,
                ApplicationPath = exePath,
                WorkingDirectory = txtNewBedWorkDir.Text.Trim(),
                Arguments = txtNewBedArgs.Text.Trim(),
                ProcessName = string.IsNullOrWhiteSpace(exePath) ? bedName : Path.GetFileNameWithoutExtension(exePath)
            };
            ConfigManager.SaveTestBed(newBed);
            testCase.TestBed = newBed.Name;
        }
        else
        {
            testCase.TestBed = cmbExistingBeds.SelectedItem?.ToString() ?? "Default";
        }

        // 2. Save Test Case in the chosen format(s)
        string dir = txtSaveDir.Text.Trim();
        if (string.IsNullOrWhiteSpace(dir)) dir = AppContext.BaseDirectory;
        Directory.CreateDirectory(dir);

        string safeFile = string.Join("_", testName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        int fmt = cmbFormat.SelectedIndex;

        if (fmt == 0 || fmt == 3)
        {
            string p = Path.Combine(dir, safeFile + ".csv");
            TestSheetLoader.SaveCsv(testCase, p);
            SavedFilePath = p;
        }
        if (fmt == 1 || fmt == 3)
        {
            string p = Path.Combine(dir, safeFile + ".json");
            TestSheetLoader.SaveJson(testCase, p);
            SavedFilePath ??= p;
        }
        if (fmt == 2 || fmt == 3)
        {
            string p = Path.Combine(dir, safeFile + ".xlsx");
            TestSheetLoader.SaveXlsx(new[] { testCase }, p);
            SavedFilePath ??= p;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
