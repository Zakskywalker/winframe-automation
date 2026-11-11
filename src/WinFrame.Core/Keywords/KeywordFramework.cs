using WinFrame.Core.Config;
using WinFrame.Core.Controls;
using WinFrame.Core.Domain;
using WinFrame.Core.Execution;
using WinFrame.Core.Logging;
using WinFrame.Core.Processes;
using WinFrame.Core.Reporting;
using WinFrame.Core.Vision;

namespace WinFrame.Core.Keywords;

/// <summary>
/// Keyword-Driven &amp; OOP Execution Engine.
/// Parses CSV / XLSX / JSON test sheets or in-memory <see cref="ITestExecutable"/> objects and routes
/// commands across Win32, UI Automation, OpenCV Vision, and Process Management with isolated execution,
/// failure thresholds, step/test retries, and optional second-pass OpenCV control snapshot recording.
/// </summary>
public class KeywordFramework
{
    public ControlRepository Controls { get; set; }
    public AppConfig Config { get; set; }
    public List<ITestExecutionListener> Listeners { get; } = new();

    public event Action<string>? StatusNotification;
    public event Action<TestCase, StepResult>? StepFinished;
    public event Action<TestRunResult>? TestFinished;

    public KeywordFramework(ControlRepository? controls = null, AppConfig? config = null)
    {
        Config = config ?? ConfigManager.Current;
        Controls = controls ?? new ControlRepository(null, Config.ControlStore).Load();
        KeywordRegistry.EnsureInitialized();
    }

    /// <summary>
    /// Original entry point: executes a CSV/XLSX/JSON keyword sheet file and returns the <see cref="TestRunResult"/>.
    /// </summary>
    public TestRunResult ExecuteTestSheet(string csvFilePath)
    {
        SystemLogger.Log($"Starting execution of Keyword Sheet: {csvFilePath}");
        if (!File.Exists(csvFilePath))
        {
            SystemLogger.Log($"Test sheet not found at {csvFilePath}", SystemLogger.LogLevel.ERROR);
            var missing = new TestRunResult { TestName = Path.GetFileName(csvFilePath), SourcePath = csvFilePath, Error = "File not found" };
            missing.MarkFinished(TestStatus.Error);
            return missing;
        }

        var tests = TestSheetLoader.LoadFromFile(csvFilePath);
        if (tests.Count == 0)
        {
            var empty = new TestRunResult { TestName = Path.GetFileNameWithoutExtension(csvFilePath), SourcePath = csvFilePath };
            empty.MarkFinished(TestStatus.Passed);
            return empty;
        }
        return ExecuteTest(tests[0]);
    }

    /// <summary>
    /// Executes a single <see cref="ITestExecutable"/> (a <see cref="TestCase"/> or custom <see cref="TestBase"/> subclass)
    /// inside an isolated guard so that no step exception or timeout can crash the host application.
    /// </summary>
    public TestRunResult ExecuteTest(
        ITestExecutable executable,
        bool recordOpenCvSnapshots = false,
        CancellationToken cancellationToken = default)
    {
        var test = executable.ToTestCase();
        var policy = (test.Policy ?? Config.FailurePolicy).Clone();
        var bed = ConfigManager.GetTestBed(test.TestBed ?? Config.DefaultTestBed);
        int maxTestAttempts = policy.OnStepFailure == FailureAction.RetryFromStart || policy.AfterRetriesExhausted == FailureAction.RetryFromStart
            ? Math.Max(1, policy.TestRetryCount + 1)
            : 1;

        TestRunResult result = new() { TestName = test.Name, SourcePath = test.SourcePath, TestBed = bed?.Name };

        for (int testAttempt = 1; testAttempt <= maxTestAttempts; testAttempt++)
        {
            result = new TestRunResult
            {
                TestName = test.Name,
                SourcePath = test.SourcePath,
                TestBed = bed?.Name,
                Attempts = testAttempt
            };
            result.MarkStarted();

            using var scope = SystemLogger.BeginScope(result.RunId, test.Name);
            Notify($"Running test '{test.Name}' (attempt {testAttempt}/{maxTestAttempts})...");
            foreach (var l in Listeners) SafeInvoke(() => l.OnTestStarted(test, result));

            var ctx = new KeywordContext(Controls, Config, bed?.Clone())
            {
                RecordOpenCvSnapshots = recordOpenCvSnapshots || Config.CaptureControlImagesDuringRun
            };
            foreach (var kv in test.Variables) ctx.Variables[kv.Key] = kv.Value;

            // Pre-scan any leading reserved header steps (AUT, COMMAND, RA, KAO) so RA and COMMAND
            // are known before the first AUT or default TestBed harness runs.
            PreApplyHeaderOverrides(test, ctx);

            bool hasExplicitAutStep = test.Steps.Any(s =>
                s.Action.Equals("AUT", StringComparison.OrdinalIgnoreCase) ||
                s.Action.Equals("SET_AUT", StringComparison.OrdinalIgnoreCase) ||
                s.Action.Equals("APP_UNDER_TEST", StringComparison.OrdinalIgnoreCase));

            // If no explicit AUT step is in the test and a TestBed is configured with StartIfNotRunning,
            // harness it at the start of the test honoring EffectiveReuseApp (RA).
            if (!hasExplicitAutStep && ctx.TestBed != null && ctx.TestBed.StartIfNotRunning)
            {
                try
                {
                    var app = ProcessManagementEngine.Shared.Harness(ctx.TestBed, reuseExisting: ctx.EffectiveReuseApp);
                    ctx.CurrentWindowHwnd = app.MainWindowHandle;
                }
                catch (Exception ex)
                {
                    SystemLogger.Test($"TestBed '{ctx.TestBed.Name}' harness warning: {ex.Message}", SystemLogger.LogLevel.WARNING);
                }
            }

            SafeInvoke(() => test.OnBeforeTest(ctx));

            bool retryWholeTest = false;
            int stepIndex = 0;

            foreach (var step in test.Steps)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.Error = "Cancelled by user.";
                    result.MarkFinished(TestStatus.Stopped);
                    break;
                }

                stepIndex++;
                step.Index = stepIndex;

                var stepResult = ExecuteStepWithRetries(test, step, ctx, policy, cancellationToken);
                result.AddStep(stepResult);
                StepFinished?.Invoke(test, stepResult);
                foreach (var l in Listeners) SafeInvoke(() => l.OnStepCompleted(test, step, stepResult));

                if (!stepResult.IsSuccess)
                {
                    SafeInvoke(() => test.OnStepFailed(ctx, step, stepResult));

                    if (policy.AutoInverseRollbackSteps > 0)
                    {
                        int rollbackEnd = Math.Max(1, stepIndex - policy.AutoInverseRollbackSteps + 1);
                        SystemLogger.Test($"Auto-rolling back steps {stepIndex}..{rollbackEnd} via StepInversionEngine...", SystemLogger.LogLevel.WARNING);
                        for (int r = stepIndex; r >= rollbackEnd; r--)
                        {
                            var invStep = StepInversionEngine.CreateInverseStep(test.Steps[r - 1]);
                            ExecuteSingleAttempt(invStep, ctx, 1, policy.StepTimeoutMs);
                        }
                    }

                    if (step.Critical || (policy.HardStopThreshold > 0 && result.FailedSteps >= policy.HardStopThreshold))
                    {
                        result.HardStop = true;
                        result.Error = $"Hard stop triggered at step {stepIndex} ({step.Action}): {stepResult.Message}";
                        result.MarkFinished(TestStatus.Failed);
                        break;
                    }

                    if (step.ContinueOnFail) continue;

                    FailureAction effectiveAction = policy.OnStepFailure == FailureAction.RetryStep
                        ? policy.AfterRetriesExhausted
                        : policy.OnStepFailure;

                    if (policy.MaxFailuresPerTest > 0 && result.FailedSteps >= policy.MaxFailuresPerTest)
                        effectiveAction = FailureAction.StopTest;

                    if (effectiveAction == FailureAction.RetryFromStart && testAttempt < maxTestAttempts)
                    {
                        SystemLogger.Test($"Retrying test '{test.Name}' from start (attempt {testAttempt + 1}/{maxTestAttempts})...", SystemLogger.LogLevel.WARNING);
                        retryWholeTest = true;
                        break;
                    }
                    if (effectiveAction is FailureAction.StopTest or FailureAction.StopQueue)
                    {
                        if (effectiveAction == FailureAction.StopQueue) result.HardStop = true;
                        result.Error = stepResult.Message;
                        result.MarkFinished(TestStatus.Failed);
                        break;
                    }
                }

                int delayMs = ctx.StepDelayOverrideMs ?? Config.StepDelayMs;
                if (delayMs > 0) Thread.Sleep(delayMs);
            }

            if (retryWholeTest) continue;

            foreach (var kv in ctx.Variables) result.OutputVariables[kv.Key] = kv.Value;
            if (ctx.TestBed != null) result.TestBed = ctx.TestBed.Name;

            if (result.Status == TestStatus.Running)
            {
                result.MarkFinished(result.FailedSteps == 0 ? TestStatus.Passed : TestStatus.Failed);
            }

            SafeInvoke(() => test.OnAfterTest(ctx, result));

            if (ctx.RecordOpenCvSnapshots)
            {
                SafeInvoke(() => Controls.Save());
            }

            // Honor KAO (Keep App Open) between tests: when EffectiveKeepAppOpen is false, close the AUT.
            if (!ctx.EffectiveKeepAppOpen)
            {
                string? appKey = ctx.TestBed?.Name ?? ProcessManagementEngine.Shared.ActiveKey;
                SystemLogger.Test($"[KAO] KeepAppOpen is false -> closing application '{appKey ?? "Active"}' at end of test.");
                ProcessManagementEngine.Shared.Stop(appKey);
            }

            break;
        }

        SystemLogger.Test($"Finished test '{test.Name}' -> {result.Status} ({result.PassedSteps}/{result.Steps.Count} passed)",
            result.IsSuccess ? SystemLogger.LogLevel.PASS : SystemLogger.LogLevel.FAIL);

        Notify($"Test '{test.Name}': {result.Status} ({result.PassedSteps}/{result.Steps.Count})");
        TestFinished?.Invoke(result);
        foreach (var l in Listeners) SafeInvoke(() => l.OnTestCompleted(test, result));
        return result;
    }

    /// <summary>
    /// Executes a queue of tests with full isolation, threshold enforcement, optional second-pass OpenCV
    /// snapshot capture, and automatic dark/light HTML report generation.
    /// </summary>
    public QueueRunResult ExecuteQueue(
        IEnumerable<ITestExecutable> tests,
        string queueName = "Test Queue",
        bool recordOpenCvSnapshots = false,
        bool generateHtmlReports = true,
        CancellationToken cancellationToken = default)
    {
        var queue = new QueueRunResult { Name = queueName };
        queue.MarkStarted();
        var policy = Config.FailurePolicy;
        int totalFailedSteps = 0;

        Notify($"Queue '{queueName}' started.");
        foreach (var l in Listeners) SafeInvoke(() => l.OnQueueStarted(queue));

        foreach (var executable in tests)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                queue.Stopped = true;
                queue.StopReason = "Cancelled by user.";
                break;
            }

            var testResult = ExecuteTest(executable, recordOpenCvSnapshots, cancellationToken);
            queue.AddTestResult(testResult);
            totalFailedSteps += testResult.FailedSteps;

            if (testResult.HardStop)
            {
                queue.Stopped = true;
                queue.StopReason = $"Hard stop during test '{testResult.TestName}': {testResult.Error}";
                SystemLogger.Test(queue.StopReason, SystemLogger.LogLevel.ERROR);
                break;
            }

            if (policy.HardStopThreshold > 0 && totalFailedSteps >= policy.HardStopThreshold)
            {
                queue.Stopped = true;
                queue.StopReason = $"Hard-stop threshold ({policy.HardStopThreshold} failed steps) reached.";
                SystemLogger.Test(queue.StopReason, SystemLogger.LogLevel.ERROR);
                break;
            }

            if (policy.MaxFailedTestsPerQueue > 0 && queue.Failed >= policy.MaxFailedTestsPerQueue)
            {
                queue.Stopped = true;
                queue.StopReason = $"Max failed tests per queue ({policy.MaxFailedTestsPerQueue}) reached.";
                SystemLogger.Test(queue.StopReason, SystemLogger.LogLevel.ERROR);
                break;
            }
        }

        queue.MarkFinished(queue.Failed > 0 ? TestStatus.Failed : queue.Stopped ? TestStatus.Stopped : TestStatus.Passed);

        if (generateHtmlReports)
        {
            try
            {
                queue.ReportPath = HtmlReportGenerator.GenerateBothThemes(queue, Config.ReportPath, Config.ReportTheme);
            }
            catch (Exception ex)
            {
                SystemLogger.Log($"HTML report generation failed: {ex.Message}", SystemLogger.LogLevel.WARNING);
            }
        }

        Notify($"Queue '{queueName}' finished: {queue.Passed} passed, {queue.Failed} failed.");
        foreach (var l in Listeners) SafeInvoke(() => l.OnQueueCompleted(queue));
        return queue;
    }

    /// <summary>
    /// Second-pass OpenCV replay: replays a test or queue with <c>recordOpenCvSnapshots = true</c> so every
    /// touched control and its parent application window are captured into the <see cref="ControlRepository"/>.
    /// </summary>
    public QueueRunResult ReplayAndRecordOpenCv(IEnumerable<ITestExecutable> tests, CancellationToken cancellationToken = default) =>
        ExecuteQueue(tests, "OpenCV Snapshot Replay", recordOpenCvSnapshots: true, generateHtmlReports: true, cancellationToken);

    /// <summary>
    /// Creates a developer-controlled <see cref="StepQueueController"/> for stepping forward, stepping backward
    /// with automatic rule-based inversion, or running forward/reverse ranges like <c>"1-3"</c> or <c>"4-2"</c>.
    /// </summary>
    public StepQueueController CreateStepController(ITestExecutable test, bool autoInverseOnError = false, int autoRollbackCount = 1) =>
        new(this, test) { AutoInverseOnError = autoInverseOnError, AutoRollbackCount = autoRollbackCount };

    /// <summary>
    /// Executes a specific step range (e.g. <c>"1-3"</c> forward, or <c>"4-3"</c> / <c>useInverse: true</c> in reverse with inverse actions).
    /// </summary>
    public List<StepResult> ExecuteStepRange(ITestExecutable test, string rangeSpec, bool useInverse = false) =>
        CreateStepController(test).ExecuteRange(rangeSpec, useInverse);

    private StepResult ExecuteStepWithRetries(
        TestCase test,
        TestStep step,
        KeywordContext ctx,
        FailurePolicy policy,
        CancellationToken ct)
    {
        int maxRetries = step.RetryCount
            ?? (policy.OnStepFailure == FailureAction.RetryStep ? Math.Max(0, policy.StepRetryCount) : 0);
        int timeoutMs = step.TimeoutMs ?? ctx.StepTimeoutOverrideMs ?? policy.StepTimeoutMs;

        StepResult last = new() { Index = step.Index, Action = step.Action, Target = step.Target, Value = step.Value };

        for (int attempt = 1; attempt <= maxRetries + 1; attempt++)
        {
            if (ct.IsCancellationRequested) break;
            foreach (var l in Listeners) SafeInvoke(() => l.OnStepStarted(test, step, attempt));

            last = ExecuteSingleAttempt(step, ctx, attempt, timeoutMs);
            if (last.IsSuccess) return last;

            if (attempt <= maxRetries)
            {
                SystemLogger.Test($"Step {step.Index} ({step.Action}) failed on attempt {attempt}/{maxRetries + 1}: {last.Message}. Retrying in {policy.RetryDelayMs} ms...",
                    SystemLogger.LogLevel.WARNING);
                // Re-harness application before retry in case window handle changed
                ProcessManagementEngine.Shared.EnsureHarnessed(restartIfExited: false);
                Thread.Sleep(Math.Max(50, policy.RetryDelayMs));
            }
        }
        return last;
    }

    private StepResult ExecuteSingleAttempt(TestStep step, KeywordContext ctx, int attempt, int timeoutMs)
    {
        var sr = new StepResult
        {
            Index = step.Index,
            Action = step.Action,
            Target = ctx.Expand(step.Target),
            Value = ctx.Expand(step.Value),
            Attempts = attempt
        };
        sr.MarkStarted();

        SystemLogger.Test($"[Step {step.Index}] {step.Action} | Target='{sr.Target}' | Value='{sr.Value}' (attempt {attempt})");

        try
        {
            // When highlight=1/true in config or test variables, draw Win32 GDI outline around Target control
            if (ctx.IsHighlightEnabled && !string.IsNullOrWhiteSpace(sr.Target) &&
                !Native.Win32Highlighter.IsNonControlAction(step.Action))
            {
                Native.Win32Highlighter.HighlightTargetIfEnabled(ctx, sr.Target);
            }

            // Optionally capture OpenCV control + window snapshot before/during step
            if (ctx.RecordOpenCvSnapshots && !string.IsNullOrWhiteSpace(sr.Target))
            {
                TryCaptureControlToSheet(ctx, sr.Target);
            }

            // Custom C# OOP step delegate?
            var custom = step.ExecuteCustom(ctx);
            if (custom != null)
            {
                custom.Index = step.Index;
                custom.Attempts = attempt;
                return custom;
            }

            if (!KeywordRegistry.TryGet(step.Action, out var descriptor))
            {
                sr.Message = $"Unmapped keyword action: '{step.Action}'";
                sr.MarkFinished(TestStatus.Failed);
                SystemLogger.Test(sr.Message, SystemLogger.LogLevel.WARNING);
                return sr;
            }

            object? output = null;
            Exception? stepEx = null;

            var task = Task.Run(() =>
            {
                try
                {
                    output = KeywordRegistry.Invoke(descriptor, ctx, step.Target, step.Value, timeoutMs);
                }
                catch (Exception ex)
                {
                    stepEx = ex;
                }
            });

            if (!task.Wait(timeoutMs))
            {
                throw new TimeoutException($"Step '{step.Action}' timed out after {timeoutMs} ms.");
            }
            if (stepEx != null) throw stepEx;

            ctx.LastResult = output;
            sr.OutputValue = output;

            if (step.Options.TryGetValue("store", out var varName) && !string.IsNullOrWhiteSpace(varName))
            {
                ctx.Variables[varName] = output?.ToString() ?? "";
            }

            if (output is bool b && !b)
            {
                throw new InvalidOperationException($"Check/action '{step.Action}' on '{sr.Target}' returned false.");
            }

            if (ctx.LastScreenshotPath != null)
            {
                sr.Screenshot = ctx.LastScreenshotPath;
                ctx.LastScreenshotPath = null;
            }

            sr.Message = output?.ToString() ?? "OK";
            sr.MarkFinished(TestStatus.Passed);
            SystemLogger.Test($"[Step {step.Index}] PASSED ({sr.DurationMs:F0} ms): {sr.Message}", SystemLogger.LogLevel.PASS);
        }
        catch (Exception ex)
        {
            sr.Exception = ex;
            sr.Message = ex.Message;
            if (Config.CaptureScreenshotOnFailure)
            {
                try
                {
                    byte[] png = OpenCvAutomation.CaptureWindowPng(ctx.CurrentWindowHwnd);
                    string failPath = Path.Combine(Config.SnapshotPath, $"fail_step{step.Index}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(failPath)!);
                    File.WriteAllBytes(failPath, png);
                    sr.Screenshot = failPath;
                }
                catch { }
            }
            sr.MarkFinished(TestStatus.Failed);
            SystemLogger.Test($"[Step {step.Index}] FAILED: {ex.Message}", SystemLogger.LogLevel.FAIL);
        }
        return sr;
    }

    private void TryCaptureControlToSheet(KeywordContext ctx, string target)
    {
        try
        {
            var el = ctx.ResolveElement(target, 1200);
            if (!ctx.Controls.TryGet(target, out var def))
            {
                if (el == null) return;
                var attrs = Uia.UiaAutomation.ExtractAttributes(el);
                string ctype = attrs.GetValueOrDefault(ControlDefinition.Keys.ControlType, "control");
                def = new ControlDefinition
                {
                    Name = target.StartsWith('@') ? target[1..] : ctx.Controls.GenerateDefaultName(ctype),
                    Attributes = attrs
                };
            }
            OpenCvAutomation.RecordControlSnapshot(ctx.Controls, def, ctx.CurrentWindowHwnd, el);
        }
        catch { }
    }

    private static void PreApplyHeaderOverrides(TestCase test, KeywordContext ctx)
    {
        foreach (var step in test.Steps)
        {
            if (!TestHeaderCommands.IsReservedHeaderKeyword(step.Action))
                break;

            string act = step.Action.Trim().ToUpperInvariant();
            string target = ctx.Expand(step.Target);
            string val = ctx.Expand(step.Value);

            if (act is "RA" or "REUSE_APP")
            {
                TestHeaderCommands.KeywordReuseApp(ctx, target, val);
            }
            else if (act is "KAO" or "KEEP_APP_OPEN")
            {
                TestHeaderCommands.KeywordKeepAppOpen(ctx, target, val);
            }
            else if (act is "COMMAND" or "CMD" or "COMMANDS")
            {
                TestHeaderCommands.KeywordCommand(ctx, target, val);
            }
        }
    }

    private void Notify(string message)
    {
        try { StatusNotification?.Invoke(message); } catch { }
    }

    private static void SafeInvoke(Action action)
    {
        try { action(); } catch { }
    }
}
