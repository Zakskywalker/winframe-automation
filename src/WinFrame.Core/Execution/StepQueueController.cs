using System.Windows.Automation;
using WinFrame.Core.Keywords;
using WinFrame.Core.Logging;
using WinFrame.Core.Native;
using WinFrame.Core.Uia;

namespace WinFrame.Core.Execution;

/// <summary>Snapshot of control/variable state captured immediately before a step runs so its inverse can restore it.</summary>
public sealed class PriorStepState
{
    public int StepIndex { get; init; }
    public string? PriorControlText { get; set; }
    public string? PriorToggleState { get; set; }
    public string? PriorVariableValue { get; set; }
    public DateTime CapturedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// Rule-based Step Inversion Engine.
/// Maps any forward <see cref="TestStep"/> (and optional <see cref="PriorStepState"/>) to its logical inverse
/// so a developer or automated recovery policy can step backward or reverse a range of steps (e.g. 4-3, 3-1)
/// just as easily as stepping forward:
/// <list type="bullet">
/// <item>Enter text (<c>TYPE</c>, <c>WIN32_TYPE</c>, <c>WIN32_SET_TEXT</c>, <c>UIA_TYPE</c>, <c>UIA_SET_VALUE</c>)
///   &#8594; Delete/clear text (<c>CLEAR_TEXT</c> / <c>UIA_CLEAR_TEXT</c>, restoring prior text if any).</item>
/// <item>Delete/clear text (<c>CLEAR_TEXT</c>, <c>UIA_CLEAR_TEXT</c>)
///   &#8594; Re-enter prior text (<c>TYPE</c> / <c>UIA_SET_VALUE</c>).</item>
/// <item>Check/uncheck (<c>UIA_TOGGLE</c>) &#8594; <c>UIA_TOGGLE</c> to the opposite state (<c>On</c> &#8596; <c>Off</c>).</item>
/// <item>Expand/collapse (<c>UIA_EXPAND</c> &#8596; <c>UIA_COLLAPSE</c>).</item>
/// <item>Window state (<c>SHOW_WINDOW</c> / <c>UIA_WINDOW_STATE</c> Maximize/Minimize &#8594; Restore/Normal).</item>
/// <item>Process (<c>START_APP</c> / <c>LAUNCH</c> &#8596; <c>STOP_APP</c>).</item>
/// <item>Variable assignment (<c>SET_VAR</c> &#8594; <c>SET_VAR</c> with prior value).</item>
/// <item>Explicit per-step option <c>inverse=ACTION|TARGET|VALUE</c> on any step (e.g. <c>CLICK</c>).</item>
/// </list>
/// Custom rules can also be registered via <see cref="RegisterRule"/>.
/// </summary>
public static class StepInversionEngine
{
    public delegate TestStep InversionRule(TestStep forwardStep, PriorStepState? priorState);

    private static readonly Dictionary<string, InversionRule> customRules = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers or overrides an inversion rule for a specific keyword action.</summary>
    public static void RegisterRule(string actionName, InversionRule rule) =>
        customRules[actionName.Trim().ToUpperInvariant()] = rule;

    /// <summary>Creates the inverse <see cref="TestStep"/> for <paramref name="step"/>.</summary>
    public static TestStep CreateInverseStep(TestStep step, PriorStepState? prior = null)
    {
        // 1. Check explicit per-step option: inverse=ACTION|TARGET|VALUE or inverse=ACTION
        if (step.Options.TryGetValue("inverse", out var invSpec) && !string.IsNullOrWhiteSpace(invSpec))
        {
            var parts = invSpec.Split('|');
            return new TestStep
            {
                Index = step.Index,
                Action = parts[0].Trim().ToUpperInvariant(),
                Target = parts.Length > 1 && !string.IsNullOrEmpty(parts[1]) ? parts[1].Trim() : step.Target,
                Value = parts.Length > 2 ? parts[2].Trim() : "",
                Description = $"[Inverse of Step {step.Index}: {step.Action}]"
            };
        }

        // 2. Check custom registered rules
        string act = step.Action.Trim().ToUpperInvariant();
        if (customRules.TryGetValue(act, out var customRule))
        {
            return customRule(step, prior);
        }

        // 3. Built-in inversion rules
        return act switch
        {
            // Enter text -> Delete/clear text (or restore prior text)
            "TYPE" or "WIN32_TYPE" or "WIN32_SET_TEXT" => new TestStep
            {
                Index = step.Index,
                Action = "CLEAR_TEXT",
                Target = step.Target,
                Value = prior?.PriorControlText ?? "",
                Description = $"[Inverse of {act}: delete text '{step.Value}']"
            },

            "UIA_TYPE" or "UIA_SET_VALUE" => new TestStep
            {
                Index = step.Index,
                Action = "UIA_CLEAR_TEXT",
                Target = step.Target,
                Value = prior?.PriorControlText ?? "",
                Description = $"[Inverse of {act}: delete text '{step.Value}']"
            },

            // Delete/clear text -> Re-enter text
            "CLEAR_TEXT" or "DELETE_TEXT" or "WIN32_CLEAR_TEXT" => new TestStep
            {
                Index = step.Index,
                Action = "WIN32_SET_TEXT",
                Target = step.Target,
                Value = prior?.PriorControlText ?? step.Value,
                Description = $"[Inverse of {act}: restore text]"
            },

            "UIA_CLEAR_TEXT" or "UIA_DELETE_TEXT" => new TestStep
            {
                Index = step.Index,
                Action = "UIA_SET_VALUE",
                Target = step.Target,
                Value = prior?.PriorControlText ?? step.Value,
                Description = $"[Inverse of {act}: restore text]"
            },

            // Check / Uncheck -> Opposite toggle state
            "UIA_TOGGLE" => new TestStep
            {
                Index = step.Index,
                Action = "UIA_TOGGLE",
                Target = step.Target,
                Value = prior?.PriorToggleState ?? InvertToggleValue(step.Value),
                Description = $"[Inverse of UIA_TOGGLE on {step.Target}]"
            },

            // Expand <-> Collapse
            "UIA_EXPAND" => new TestStep
            {
                Index = step.Index,
                Action = "UIA_COLLAPSE",
                Target = step.Target,
                Description = $"[Inverse of UIA_EXPAND on {step.Target}]"
            },

            "UIA_COLLAPSE" => new TestStep
            {
                Index = step.Index,
                Action = "UIA_EXPAND",
                Target = step.Target,
                Description = $"[Inverse of UIA_COLLAPSE on {step.Target}]"
            },

            // Start App <-> Stop App
            "START_APP" or "LAUNCH" => new TestStep
            {
                Index = step.Index,
                Action = "STOP_APP",
                Target = step.Target,
                Description = $"[Inverse of {act}: stop {step.Target}]"
            },

            "STOP_APP" or "CLOSE_APP" => new TestStep
            {
                Index = step.Index,
                Action = "START_APP",
                Target = step.Target,
                Description = $"[Inverse of {act}: start {step.Target}]"
            },

            // Window state -> Restore / Normal
            "SHOW_WINDOW" => new TestStep
            {
                Index = step.Index,
                Action = "SHOW_WINDOW",
                Target = step.Target,
                Value = step.Value.Equals("RESTORE", StringComparison.OrdinalIgnoreCase) ? "MINIMIZE" : "RESTORE",
                Description = $"[Inverse of SHOW_WINDOW {step.Value}]"
            },

            "UIA_WINDOW_STATE" => new TestStep
            {
                Index = step.Index,
                Action = "UIA_WINDOW_STATE",
                Target = step.Target,
                Value = "Normal",
                Description = $"[Inverse of UIA_WINDOW_STATE {step.Value}]"
            },

            // Variable set -> restore previous value
            "SET_VAR" => new TestStep
            {
                Index = step.Index,
                Action = "SET_VAR",
                Target = step.Target,
                Value = prior?.PriorVariableValue ?? "",
                Description = $"[Inverse of SET_VAR {step.Target}]"
            },

            // Checks/waits/logs/highlights are read-only; their inverse is a no-op/re-highlight
            "WAIT" or "SLEEP" or "LOG" or "VERIFY" or "WIN32_VERIFY" or "UIA_VERIFY" or "UIA_CHECK_TEXT"
                or "UIA_CHECK_VALUE" or "UIA_EXISTS" or "CV_VERIFY_IMAGE"
                or "HIGHLIGHT" or "WIN32_HIGHLIGHT" or "OUTLINE_CONTROL" or "HIGHLIGHT_CONTROLS" => new TestStep
            {
                Index = step.Index,
                Action = "LOG",
                Target = $"Inverse skip (read-only step {step.Index}: {act})",
                Description = $"[Inverse no-op for {act}]"
            },

            // Default fallback for CLICK / UIA_INVOKE etc.: re-invoke (toggles button/state) or log
            _ => new TestStep
            {
                Index = step.Index,
                Action = step.Action,
                Target = step.Target,
                Value = step.Value,
                Description = $"[Inverse replay of {step.Action}]"
            }
        };
    }

    private static string InvertToggleValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value.Equals("On", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            return "Off";
        if (value.Equals("Off", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            return "On";
        return "";
    }
}

/// <summary>
/// Developer-Controlled Step Queue for both Local and Service/API execution.
/// Allows stepping through a <see cref="TestCase"/> one step at a time (<see cref="StepNext"/>),
/// stepping backward with automatic rule-based inversion (<see cref="StepPrevious"/>),
/// executing arbitrary forward or reverse step ranges such as <c>"1-3"</c>, <c>"3-4"</c>, or <c>"4-1"</c>
/// (<see cref="ExecuteRange(string, bool, bool)"/>), and rolling back steps automatically when an error occurs.
/// </summary>
public sealed class StepQueueController
{
    private readonly Dictionary<int, PriorStepState> priorStates = new();

    public KeywordFramework Engine { get; }
    public TestCase Test { get; private set; }
    public KeywordContext Context { get; private set; }
    public List<StepResult> ExecutedHistory { get; } = new();

    /// <summary>1-based index of the next step to execute when stepping forward (starts at 1).</summary>
    public int Cursor { get; set; } = 1;

    /// <summary>When true, if a step fails during forward execution, its inverse (and optionally previous steps) is called automatically.</summary>
    public bool AutoInverseOnError { get; set; }

    /// <summary>How many steps to roll back via their inverse when <see cref="AutoInverseOnError"/> is true (default 1 = the failing step).</summary>
    public int AutoRollbackCount { get; set; } = 1;

    public int TotalSteps => Test.Steps.Count;
    public bool HasNext => Cursor >= 1 && Cursor <= TotalSteps;
    public bool HasPrevious => Cursor > 1;

    public event Action<StepResult, bool>? StepExecuted;

    public StepQueueController(KeywordFramework engine, ITestExecutable test)
    {
        Engine = engine;
        Test = test.ToTestCase();
        NormalizeStepIndices();
        var bed = Config.ConfigManager.GetTestBed(Test.TestBed ?? Engine.Config.DefaultTestBed);
        Context = new KeywordContext(Engine.Controls, Engine.Config, bed);
        foreach (var kv in Test.Variables) Context.Variables[kv.Key] = kv.Value;
    }

    public void LoadTest(ITestExecutable test)
    {
        Test = test.ToTestCase();
        NormalizeStepIndices();
        priorStates.Clear();
        ExecutedHistory.Clear();
        Cursor = 1;
        var bed = Config.ConfigManager.GetTestBed(Test.TestBed ?? Engine.Config.DefaultTestBed);
        Context = new KeywordContext(Engine.Controls, Engine.Config, bed);
        foreach (var kv in Test.Variables) Context.Variables[kv.Key] = kv.Value;
    }

    /// <summary>Executes the step at <see cref="Cursor"/> and advances <see cref="Cursor"/> by 1.</summary>
    public StepResult StepNext()
    {
        if (!HasNext)
        {
            return StepResult.Fail(Cursor, "END_OF_QUEUE", "", "", $"Cursor ({Cursor}) is past the last step ({TotalSteps}).");
        }
        int idx = Cursor;
        var res = ExecuteStepAt(idx, useInverse: false);
        if (res.IsSuccess) Cursor = idx + 1;
        return res;
    }

    /// <summary>
    /// Moves <see cref="Cursor"/> back by 1 and executes the inverse of that step
    /// (e.g. entered text is deleted, checked box is unchecked).
    /// </summary>
    public StepResult StepPrevious(bool useInverse = true)
    {
        int targetIdx = Cursor > TotalSteps ? TotalSteps : Cursor > 1 ? Cursor - 1 : 1;
        Cursor = targetIdx;
        return ExecuteStepAt(targetIdx, useInverse);
    }

    /// <summary>
    /// Executes a 1-based step range such as <c>"1-3"</c>, <c>"3-4"</c>, <c>"4-3"</c>, <c>"4-1"</c>, or single step <c>"2"</c>.
    /// <list type="bullet">
    /// <item>When <c>start &lt;= end</c> and <paramref name="useInverse"/> is false: runs steps <c>start..end</c> forward.</item>
    /// <item>When <c>start &gt; end</c> (e.g. <c>"4-3"</c> or <c>"3-1"</c>): automatically walks in reverse order (<c>start</c> down to <c>end</c>) and runs each step's inverse!</item>
    /// <item>When <c>start &lt;= end</c> and <paramref name="useInverse"/> is true: walks from <c>end</c> down to <c>start</c> running each step's inverse.</item>
    /// </list>
    /// </summary>
    public List<StepResult> ExecuteRange(string rangeSpec, bool useInverse = false, bool stopOnError = true)
    {
        var (start, end) = ParseRange(rangeSpec, TotalSteps);
        return ExecuteRange(start, end, useInverse, stopOnError);
    }

    public List<StepResult> ExecuteRange(int startStep, int endStep, bool useInverse = false, bool stopOnError = true)
    {
        var results = new List<StepResult>();
        if (TotalSteps == 0) return results;

        startStep = Math.Clamp(startStep, 1, TotalSteps);
        endStep = Math.Clamp(endStep, 1, TotalSteps);

        bool reverseOrder = startStep > endStep || useInverse;
        bool applyInverse = useInverse || startStep > endStep;

        int high = Math.Max(startStep, endStep);
        int low = Math.Min(startStep, endStep);

        IEnumerable<int> indices = reverseOrder
            ? Enumerable.Range(low, high - low + 1).Reverse()
            : Enumerable.Range(low, high - low + 1);

        SystemLogger.Test($"[StepQueue] Executing range {(reverseOrder ? $"{high}..{low} (REVERSE/INVERSE)" : $"{low}..{high} (FORWARD)")}");

        foreach (int idx in indices)
        {
            var res = ExecuteStepAt(idx, applyInverse);
            results.Add(res);
            Cursor = reverseOrder ? idx : idx + 1;

            if (!res.IsSuccess && stopOnError)
            {
                break;
            }
        }
        return results;
    }

    /// <summary>Executes a single 1-based step index either forward or via its <see cref="StepInversionEngine"/> inverse.</summary>
    public StepResult ExecuteStepAt(int oneBasedIndex, bool useInverse = false)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > TotalSteps)
        {
            return StepResult.Fail(oneBasedIndex, "OUT_OF_RANGE", "", "", $"Step {oneBasedIndex} is outside 1..{TotalSteps}.");
        }

        var originalStep = Test.Steps[oneBasedIndex - 1];
        originalStep.Index = oneBasedIndex;

        if (!useInverse)
        {
            priorStates[oneBasedIndex] = CapturePriorState(originalStep);
        }

        priorStates.TryGetValue(oneBasedIndex, out var prior);
        TestStep stepToRun = useInverse
            ? StepInversionEngine.CreateInverseStep(originalStep, prior)
            : originalStep;

        var singleCase = new TestCase(useInverse ? $"{Test.Name} [Inverse #{oneBasedIndex}]" : $"{Test.Name} [Step #{oneBasedIndex}]")
        {
            TestBed = Test.TestBed,
            Steps = new List<TestStep> { stepToRun }
        };

        var res = InvokeStepDirectly(stepToRun);
        ExecutedHistory.Add(res);
        StepExecuted?.Invoke(res, useInverse);

        // If a forward step failed and AutoInverseOnError is enabled, automatically run inverse rollback!
        if (!useInverse && !res.IsSuccess && AutoInverseOnError && AutoRollbackCount > 0)
        {
            int rollbackEnd = Math.Max(1, oneBasedIndex - AutoRollbackCount + 1);
            SystemLogger.Test($"[StepQueue] Step {oneBasedIndex} failed; auto-rolling back steps {oneBasedIndex}..{rollbackEnd} via inverse rules.",
                SystemLogger.LogLevel.WARNING);
            for (int r = oneBasedIndex; r >= rollbackEnd; r--)
            {
                ExecuteStepAt(r, useInverse: true);
            }
        }

        return res;
    }

    public static (int Start, int End) ParseRange(string rangeSpec, int maxSteps)
    {
        if (string.IsNullOrWhiteSpace(rangeSpec)) return (1, Math.Max(1, maxSteps));
        rangeSpec = rangeSpec.Trim();
        int dash = rangeSpec.IndexOf('-');
        if (dash < 0) dash = rangeSpec.IndexOf("..", StringComparison.Ordinal);
        if (dash < 0)
        {
            int single = int.TryParse(rangeSpec, out int s) ? s : 1;
            return (single, single);
        }
        string left = rangeSpec[..dash].Trim();
        string right = rangeSpec[(dash + (rangeSpec[dash] == '.' ? 2 : 1))..].Trim();
        int start = int.TryParse(left, out int a) ? a : 1;
        int end = int.TryParse(right, out int b) ? b : maxSteps;
        return (start, end);
    }

    private PriorStepState CapturePriorState(TestStep step)
    {
        var state = new PriorStepState { StepIndex = step.Index };
        try
        {
            string act = step.Action.ToUpperInvariant();
            if (act == "SET_VAR" && Context.Variables.TryGetValue(step.Target, out var prevVar))
            {
                state.PriorVariableValue = prevVar;
            }
            else if (!string.IsNullOrWhiteSpace(step.Target) &&
                     (act.Contains("TYPE") || act.Contains("TEXT") || act.Contains("VALUE") || act.Contains("TOGGLE")))
            {
                var el = Context.ResolveElement(step.Target, timeoutMs: 700);
                if (el != null)
                {
                    state.PriorControlText = UiaAutomation.GetValue(el);
                    if (el.TryGetCurrentPattern(TogglePattern.Pattern, out var tp))
                        state.PriorToggleState = ((TogglePattern)tp).Current.ToggleState.ToString();
                }
                else
                {
                    var h = Context.ResolveHwnd(step.Target, timeoutMs: 700);
                    if (h != IntPtr.Zero) state.PriorControlText = Win32Automation.GetControlText(h);
                }
            }
        }
        catch { }
        return state;
    }

    private StepResult InvokeStepDirectly(TestStep step)
    {
        var sr = new StepResult
        {
            Index = step.Index,
            Action = step.Action,
            Target = Context.Expand(step.Target),
            Value = Context.Expand(step.Value),
            Attempts = 1
        };
        sr.MarkStarted();

        try
        {
            if (Context.IsHighlightEnabled && !string.IsNullOrWhiteSpace(sr.Target) &&
                !step.Action.Equals("HIGHLIGHT", StringComparison.OrdinalIgnoreCase))
            {
                Win32Highlighter.HighlightTargetIfEnabled(Context, sr.Target);
            }

            var custom = step.ExecuteCustom(Context);
            if (custom != null) return custom;

            if (!KeywordRegistry.TryGet(step.Action, out var descriptor))
            {
                sr.Message = $"Unmapped keyword action: '{step.Action}'";
                sr.MarkFinished(TestStatus.Failed);
                return sr;
            }

            object? output = KeywordRegistry.Invoke(descriptor, Context, step.Target, step.Value, step.TimeoutMs ?? Engine.Config.DefaultTimeoutMs);
            Context.LastResult = output;
            sr.OutputValue = output;

            if (step.Options.TryGetValue("store", out var varName) && !string.IsNullOrWhiteSpace(varName))
                Context.Variables[varName] = output?.ToString() ?? "";

            if (output is bool b && !b)
                throw new InvalidOperationException($"Check/action '{step.Action}' on '{sr.Target}' returned false.");

            sr.Message = output?.ToString() ?? "OK";
            sr.MarkFinished(TestStatus.Passed);
        }
        catch (Exception ex)
        {
            sr.Exception = ex;
            sr.Message = ex.Message;
            sr.MarkFinished(TestStatus.Failed);
        }
        return sr;
    }

    private void NormalizeStepIndices()
    {
        for (int i = 0; i < Test.Steps.Count; i++)
            Test.Steps[i].Index = i + 1;
    }
}
