using System.Drawing;
using Xunit;
using WinFrame.Core.Config;
using WinFrame.Core.Controls;
using WinFrame.Core.Domain;
using WinFrame.Core.Execution;
using WinFrame.Core.Keywords;
using WinFrame.Core.Logging;
using WinFrame.Core.Native;
using WinFrame.Core.Recording;
using WinFrame.Core.Reporting;
using WinFrame.Core.Update;

namespace WinFrame.Tests;

public sealed class CustomOopTest : TestBase
{
    public bool BeforeCalled { get; private set; }
    public bool AfterCalled { get; private set; }

    public CustomOopTest() : base("Custom_OOP_Test")
    {
        AddStep("SET_VAR", "counter", "42");
        AddStep("ASSERT_VAR", "counter", "42");
        AddLambdaStep("CUSTOM_MULTIPLY", ctx =>
        {
            int val = int.Parse(ctx.Variables["counter"]) * 2;
            ctx.Variables["counter"] = val.ToString();
            return StepResult.Pass(3, "CUSTOM_MULTIPLY", "counter", val.ToString(), $"Doubled to {val}");
        });
    }

    public override void OnBeforeTest(KeywordContext context) => BeforeCalled = true;
    public override void OnAfterTest(KeywordContext context, TestRunResult result) => AfterCalled = true;
}

public class FrameworkTests
{
    [Fact]
    public void ImageCodec_RoundTrips_File_Base64_Deflate_And_7Zip()
    {
        byte[] samplePng = new byte[512];
        for (int i = 0; i < samplePng.Length; i++) samplePng[i] = (byte)(i % 251);

        string tempDir = Path.Combine(Path.GetTempPath(), "winframe_test_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            foreach (var mode in new[] { ImageStorageMode.File, ImageStorageMode.Base64, ImageStorageMode.Compressed, ImageStorageMode.SevenZip })
            {
                string encoded = ImageCodec.Encode(samplePng, mode, tempDir, "ctrl.png");
                Assert.Equal(mode, ImageCodec.DetectMode(encoded));
                byte[]? decoded = ImageCodec.Decode(encoded, tempDir);
                Assert.NotNull(decoded);
                Assert.Equal(samplePng, decoded!);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ControlRepository_DefaultNaming_And_Stores_RoundTrip()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "winframe_repo_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var settings = new ControlStoreSettings
            {
                Format = ControlStoreFormat.Json,
                Path = Path.Combine(tempDir, "controls.json"),
                ImageStorage = ImageStorageMode.Compressed
            };
            var repo = new ControlRepository(new JsonControlStore(settings.Path), settings);
            string n1 = repo.GenerateDefaultName("Button");
            Assert.Equal("button_1", n1);
            repo.AddOrUpdate(new ControlDefinition
            {
                Name = n1,
                Attributes = new Dictionary<string, string>
                {
                    [ControlDefinition.Keys.AutomationId] = "btnSubmit",
                    [ControlDefinition.Keys.ControlType] = "Button"
                }
            });
            string n2 = repo.GenerateDefaultName("Button");
            Assert.Equal("button_2", n2);

            repo.Save();
            var reloaded = new ControlRepository(new JsonControlStore(settings.Path), settings).Load();
            Assert.Single(reloaded.All);
            Assert.Contains("btnSubmit", reloaded.ExportAutomationIdsJson());
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void OopTestBase_Executes_With_LifecycleHooks_And_StrongResults()
    {
        var engine = new KeywordFramework();
        var oopTest = new CustomOopTest();
        var result = engine.ExecuteTest(oopTest).EnsurePassed();

        Assert.True(oopTest.BeforeCalled);
        Assert.True(oopTest.AfterCalled);
        Assert.Equal(3, result.PassedSteps);
        Assert.Equal("84", result.OutputVariables["counter"]);
    }

    [Fact]
    public void StepInversion_And_StepQueueController_Forward_Previous_And_ReverseRanges()
    {
        // Verify rule-based inversion: TYPE -> CLEAR_TEXT, UIA_TOGGLE On -> Off, UIA_EXPAND -> UIA_COLLAPSE
        var typeStep = new TestStep { Index = 1, Action = "TYPE", Target = "@txtUser", Value = "Zak" };
        var invType = StepInversionEngine.CreateInverseStep(typeStep);
        Assert.Equal("CLEAR_TEXT", invType.Action);
        Assert.Equal("@txtUser", invType.Target);

        var toggleStep = new TestStep { Index = 2, Action = "UIA_TOGGLE", Target = "@chkRemember", Value = "On" };
        var invToggle = StepInversionEngine.CreateInverseStep(toggleStep);
        Assert.Equal("UIA_TOGGLE", invToggle.Action);
        Assert.Equal("Off", invToggle.Value);

        // Build a 4-step variable test and run forward 1-4, then reverse range 4-2 using StepQueueController
        var tc = new TestCase("RangeAndInverseTest") { TestBed = "Calculator" };
        tc.AddStep("SET_VAR", "a", "1");
        tc.AddStep("SET_VAR", "b", "2");
        tc.AddStep("SET_VAR", "c", "3");
        tc.AddStep("SET_VAR", "d", "4");

        var engine = new KeywordFramework();
        var ctrl = engine.CreateStepController(tc);

        // Run range 1-4 forward
        var fwd = ctrl.ExecuteRange("1-4");
        Assert.Equal(4, fwd.Count);
        Assert.Equal("4", ctrl.Context.Variables["d"]);
        Assert.Equal("3", ctrl.Context.Variables["c"]);

        // Now reverse range "4-3" (descending range automatically runs inverse of step 4 then step 3!)
        var rev = ctrl.ExecuteRange("4-3");
        Assert.Equal(2, rev.Count);
        Assert.Equal("", ctrl.Context.Variables["d"]);
        Assert.Equal("", ctrl.Context.Variables["c"]);
        Assert.Equal("2", ctrl.Context.Variables["b"]);
    }

    [Fact]
    public void Win32Highlight_Respects_Config_And_Test_Variable()
    {
        var cfg = new AppConfig { Highlight = "0", HighlightColor = "#00FF00", HighlightIntervalMs = 40 };
        var repo = new ControlRepository();
        repo.AddOrUpdate(new ControlDefinition
        {
            Name = "button_1",
            Attributes = new Dictionary<string, string>
            {
                [ControlDefinition.Keys.BoundingRectangle] = "10,10,80,30"
            }
        });

        var ctx = new KeywordContext(repo, cfg);
        Assert.False(ctx.IsHighlightEnabled);

        // Turn on via variable "1" or "true"
        ctx.Variables["highlight"] = "1";
        Assert.True(ctx.IsHighlightEnabled);

        ctx.Variables["highlight"] = "true";
        Assert.True(ctx.IsHighlightEnabled);

        // Turn off via "0" or ""
        ctx.Variables["highlight"] = "0";
        Assert.False(ctx.IsHighlightEnabled);
        ctx.Variables["highlight"] = "";
        Assert.False(ctx.IsHighlightEnabled);

        // Generate highlight steps for control document
        var steps = Win32Highlighter.CreateHighlightStepsForControls(repo, "#FF0000", 50);
        Assert.Single(steps);
        Assert.Equal("HIGHLIGHT", steps[0].Action);
        Assert.Equal("@button_1", steps[0].Target);
    }

    [Fact]
    public void HtmlReportGenerator_Produces_Dark_And_Light_Templates()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "winframe_rpt_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            SystemLogger.Test("Sample test log entry");
            SystemLogger.User("Sample user log entry");
            var q = new QueueRunResult { Name = "UnitQueue" };
            q.MarkStarted();
            var tr = new TestRunResult { TestName = "SampleTest" };
            tr.MarkStarted();
            tr.AddStep(StepResult.Pass(1, "SET_VAR", "x", "1"));
            tr.MarkFinished(TestStatus.Passed);
            q.AddTestResult(tr);
            q.MarkFinished(TestStatus.Passed);

            string preferred = HtmlReportGenerator.GenerateBothThemes(q, tempDir, "dark");
            Assert.True(File.Exists(preferred));
            Assert.Equal(2, Directory.GetFiles(tempDir, "*.html").Length);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AutoUpdateManager_Compares_Semantic_And_Build_Versions()
    {
        Assert.True(AutoUpdateManager.IsVersionNewer("1.0.0", "1.0.1"));
        Assert.True(AutoUpdateManager.IsVersionNewer("1.0.0.0", "1.0.0.42"));
        Assert.False(AutoUpdateManager.IsVersionNewer("1.0.0", "1.0.0"));
        Assert.False(AutoUpdateManager.IsVersionNewer("1.0.0", "0.9.9"));
    }

    [Fact]
    public void SampleFormats_Csv_Json_Xlsx_And_WindowToControlPosition_WorkEndToEnd()
    {
        string repoRoot = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(repoRoot) && !File.Exists(Path.Combine(repoRoot, "WinFrame.sln")))
        {
            repoRoot = Path.GetDirectoryName(repoRoot) ?? "";
        }
        string csvPath = Path.Combine(repoRoot, "samples", "csv", "01_calculator_win32_and_uia.csv");
        string jsonSuitePath = Path.Combine(repoRoot, "samples", "json", "01_calculator_suite.json");
        string xlsxSuitePath = Path.Combine(repoRoot, "samples", "xlsx", "01_winframe_multi_sheet_suite.xlsx");
        string xlsxControlsPath = Path.Combine(repoRoot, "samples", "controls", "sample_controls.xlsx");

        var csvTests = TestSheetLoader.LoadFromFile(csvPath);
        Assert.Single(csvTests);
        Assert.Equal(13, csvTests[0].Steps.Count);
        Assert.Equal("AUT", csvTests[0].Steps[0].Action);
        Assert.Equal("COMMAND", csvTests[0].Steps[1].Action);
        Assert.Equal("RA", csvTests[0].Steps[2].Action);
        Assert.Equal("KAO", csvTests[0].Steps[3].Action);

        var jsonTests = TestSheetLoader.LoadFromFile(jsonSuitePath);
        Assert.Equal(2, jsonTests.Count);
        Assert.Equal("AUT", jsonTests[0].Steps[0].Action);
        Assert.Equal("COMMAND", jsonTests[0].Steps[1].Action);
        Assert.Equal("RA", jsonTests[0].Steps[2].Action);
        Assert.Equal("KAO", jsonTests[0].Steps[3].Action);

        // Generate/update the multi-sheet Excel (.xlsx) sample suite & Excel control sheet
        Directory.CreateDirectory(Path.GetDirectoryName(xlsxSuitePath)!);
        var combinedSuite = new List<TestCase>();
        combinedSuite.AddRange(csvTests);
        combinedSuite.AddRange(jsonTests);
        TestSheetLoader.SaveXlsx(combinedSuite, xlsxSuitePath);

        var loadedXlsx = TestSheetLoader.LoadFromFile(xlsxSuitePath);
        Assert.Equal(3, loadedXlsx.Count);

        var jsonControlsPath = Path.Combine(repoRoot, "samples", "controls", "sample_controls.json");
        var ctrlRepo = new ControlRepository(new JsonControlStore(jsonControlsPath)).Load();
        Assert.True(ctrlRepo.Count >= 4);
        new XlsxControlStore(xlsxControlsPath).Save(ctrlRepo.All);
        Assert.True(File.Exists(xlsxControlsPath));

        // Verify Window-to-Control Position & Recorder Mouse+Window Scrape
        var recorder = new TestRecorder(new ControlRepository()) { CaptureOpenCvSnapshots = false };
        var (def, hover, _) = recorder.ScrapeControlAtMouseAndWindowPosition(100, 100, commitToRepository: true);
        Assert.NotNull(def);
        Assert.True(def.Attributes.ContainsKey("Win32.WindowRelX"));
        Assert.True(def.Attributes.ContainsKey("Win32.ControlRelX"));
        Assert.False(string.IsNullOrWhiteSpace(hover.SuggestedName));
    }

    [Fact]
    public void ReservedHeaderKeywords_AUT_COMMAND_RA_And_KAO_Parse_And_Execute_With_ConfigFallback()
    {
        // 1. Verify TestCommandParser handles standalone flags AND quoted comma-containing values like name="x,x" and video="external store from config loc"
        string cmdString = "self_healing, video=\"external store from config loc\", args=\"--mode=test,fast\", highlight=\"1\", custom_pair=\"x,x\"";
        var parsed = TestCommandParser.Parse(cmdString);
        Assert.Equal(5, parsed.Count);

        Assert.Equal("self_healing", parsed[0].Name);
        Assert.False(parsed[0].HasExplicitValue);
        Assert.Equal("1", parsed[0].EffectiveValue);

        Assert.Equal("video", parsed[1].Name);
        Assert.True(parsed[1].HasExplicitValue);
        Assert.Equal("external store from config loc", parsed[1].Value);

        Assert.Equal("args", parsed[2].Name);
        Assert.Equal("--mode=test,fast", parsed[2].Value);
        Assert.Equal(new[] { "--mode=test", "fast" }, parsed[2].SplitValueItems());

        Assert.Equal("custom_pair", parsed[4].Name);
        Assert.Equal("x,x", parsed[4].Value);

        // 2. Verify AUT, COMMAND, RA, and KAO execution & Config fallback when RA/KAO are "config" or explicit 0/1
        var cfg = new AppConfig
        {
            ReuseApp = true,
            KeepAppOpen = false,
            StepDelayMs = 0
        };
        var repo = new ControlRepository();
        var ctx = new KeywordContext(repo, cfg) { AutoHarnessOnAutKeyword = false };

        // Test RA & KAO falling back to Config when value is empty or "config"
        Assert.True(TestHeaderCommands.KeywordReuseApp(ctx, "", "config"));
        Assert.False(TestHeaderCommands.KeywordKeepAppOpen(ctx, "", "config"));

        // Override RA=0 and KAO=1 explicitly in the test
        Assert.False(TestHeaderCommands.KeywordReuseApp(ctx, "", "0"));
        Assert.True(TestHeaderCommands.KeywordKeepAppOpen(ctx, "", "1"));
        Assert.Equal("0", ctx.Variables["ra"]);
        Assert.Equal("1", ctx.Variables["kao"]);

        // Execute COMMAND keyword with standalone + key="x,x" + video="external store from config loc"
        TestHeaderCommands.KeywordCommand(ctx, "", cmdString);
        Assert.True(ctx.SelfHealingEnabled);
        Assert.Equal("external store from config loc", ctx.VideoRecordingSetting);
        Assert.Equal("external store from config loc", ctx.Variables["video"]);
        Assert.Equal("--mode=test,fast", ctx.AppArgumentsOverride);
        Assert.Equal("x,x", ctx.ActiveCommands["custom_pair"]);
        Assert.Equal("x,x", ctx.Variables["cmd.custom_pair"]);

        // Execute AUT keyword override in test
        string aut = TestHeaderCommands.KeywordAut(ctx, @"C:\Apps\MyCustomAut.exe");
        Assert.Equal(@"C:\Apps\MyCustomAut.exe", aut);
        Assert.Equal(@"C:\Apps\MyCustomAut.exe", ctx.AutOverride);
        Assert.NotNull(ctx.TestBed);
        Assert.Equal("MyCustomAut", ctx.TestBed!.Name);
        Assert.Equal("--mode=test,fast", ctx.TestBed.Arguments);
        Assert.False(ctx.TestBed.ReuseApp);
        Assert.True(ctx.TestBed.KeepAppOpen);

        // 3. Verify EnsureReservedHeader & full KeywordFramework execution of a test starting with AUT, COMMAND, RA, KAO
        var tc = new TestCase("HeaderStructureTest") { TestBed = "Calculator" };
        tc.SetAut("Calculator")
          .WithCommand("self_healing=\"uia,opencv\", video=\"external store from config loc\", custom_attr=\"x,x\"")
          .WithReuseApp(true)
          .WithKeepAppOpen(true);
        tc.AddStep("ASSERT_VAR", "video", "external store from config loc");
        tc.AddStep("ASSERT_VAR", "custom_attr", "x,x");
        tc.AddStep("ASSERT_VAR", "ra", "1");
        tc.AddStep("ASSERT_VAR", "kao", "1");

        // Disable actual process launch for unit test isolation
        var calcBed = ConfigManager.GetTestBed("Calculator");
        bool origStart = calcBed?.StartIfNotRunning ?? true;
        if (calcBed != null) calcBed.StartIfNotRunning = false;
        try
        {
            var engine = new KeywordFramework(repo, cfg);
            var runRes = engine.ExecuteTest(tc).EnsurePassed();
            Assert.Equal(8, runRes.PassedSteps);
            Assert.Equal("external store from config loc", runRes.OutputVariables["video"]);
            Assert.Equal("x,x", runRes.OutputVariables["custom_attr"]);
        }
        finally
        {
            if (calcBed != null) calcBed.StartIfNotRunning = origStart;
        }
    }
}

