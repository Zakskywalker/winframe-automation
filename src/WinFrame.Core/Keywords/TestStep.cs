using System.Text.Json.Serialization;
using WinFrame.Core.Execution;

namespace WinFrame.Core.Keywords;

/// <summary>
/// Polymorphic contract for a single executable step. Inherit <see cref="TestStepBase"/> or use
/// <see cref="TestStep"/> / <see cref="DelegateTestStep"/> to author custom OOP steps in C#.
/// </summary>
public interface ITestStep
{
    int Index { get; set; }
    string Action { get; }
    string Target { get; }
    string Value { get; }
    string Description { get; }
    IDictionary<string, string> Options { get; }
    bool Critical { get; }
    bool ContinueOnFail { get; }
    int? TimeoutMs { get; }
    int? RetryCount { get; }

    /// <summary>Optional custom C# execution hook. When non-null, called instead of the keyword table lookup.</summary>
    StepResult? ExecuteCustom(KeywordContext context);
    TestStep ToTestStep();
}

/// <summary>Abstract base class for authoring custom C# OOP test steps.</summary>
public abstract class TestStepBase : ITestStep
{
    public int Index { get; set; }
    public virtual string Action => GetType().Name;
    public virtual string Target { get; set; } = "";
    public virtual string Value { get; set; } = "";
    public virtual string Description { get; set; } = "";
    public IDictionary<string, string> Options { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore] public bool Critical => GetBool("critical");
    [JsonIgnore] public bool ContinueOnFail => GetBool("continueOnFail");
    [JsonIgnore] public int? TimeoutMs => Options.TryGetValue("timeout", out var t) && int.TryParse(t, out var v) ? v : null;
    [JsonIgnore] public int? RetryCount => Options.TryGetValue("retry", out var t) && int.TryParse(t, out var v) ? v : null;

    public bool GetBool(string key) =>
        Options.TryGetValue(key, out var v) &&
        (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase));

    public abstract StepResult? ExecuteCustom(KeywordContext context);

    /// <summary>Converts this custom step to a serializable <see cref="TestStep"/>.</summary>
    public virtual TestStep ToTestStep() => new()
    {
        Index = Index,
        Action = Action,
        Target = Target,
        Value = Value,
        Description = Description,
        Options = new Dictionary<string, string>(Options, StringComparer.OrdinalIgnoreCase),
        CustomExecutor = ExecuteCustom
    };
}

/// <summary>A lambda/delegate-backed OOP step for inline C# steps inside a <see cref="TestCase"/>.</summary>
public sealed class DelegateTestStep : TestStepBase
{
    private readonly Func<KeywordContext, StepResult> callback;

    public DelegateTestStep(string name, Func<KeywordContext, StepResult> callback, string target = "", string value = "")
    {
        ActionName = name;
        Target = target;
        Value = value;
        this.callback = callback;
    }

    public string ActionName { get; }
    public override string Action => ActionName;
    public override StepResult? ExecuteCustom(KeywordContext context) => callback(context);
}

/// <summary>A single keyword-sheet row or fluent step: Action, Target, Value, Options.</summary>
public class TestStep : TestStepBase
{
    public new string Action { get; set; } = "";
    public override string Target { get; set; } = "";
    public override string Value { get; set; } = "";
    public override string Description { get; set; } = "";
    public new Dictionary<string, string> Options
    {
        get => (Dictionary<string, string>)base.Options;
        set => base.Options = value;
    }

    [JsonIgnore] public int SourceLine { get; set; }

    /// <summary>Optional C# delegate allowing a <see cref="TestStep"/> to execute arbitrary OOP logic.</summary>
    [JsonIgnore] public Func<KeywordContext, StepResult?>? CustomExecutor { get; set; }

    public override StepResult? ExecuteCustom(KeywordContext context) => CustomExecutor?.Invoke(context);

    public string OptionsToString() => string.Join(";", Options.Select(kv => $"{kv.Key}={kv.Value}"));

    public static Dictionary<string, string> ParseOptions(string? text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return d;
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq < 0) d[part] = "true";
            else d[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
        return d;
    }

    public TestStep WithOption(string key, string value)
    {
        Options[key] = value;
        return this;
    }

    public TestStep AsCritical(bool critical = true) => WithOption("critical", critical ? "true" : "false");
    public TestStep WithRetries(int retries) => WithOption("retry", retries.ToString());
    public TestStep WithTimeout(int timeoutMs) => WithOption("timeout", timeoutMs.ToString());
    public TestStep StoreResultAs(string variableName) => WithOption("store", variableName);

    public TestStep Clone() => new()
    {
        Index = Index, Action = Action, Target = Target, Value = Value, Description = Description,
        SourceLine = SourceLine, CustomExecutor = CustomExecutor,
        Options = new Dictionary<string, string>(Options, StringComparer.OrdinalIgnoreCase)
    };

    public override TestStep ToTestStep() => this;

    public override string ToString() => $"{Action} | {Target} | {Value}";
}

/// <summary>
/// Polymorphic contract for any runnable test (either a data-driven <see cref="TestCase"/> or a
/// strongly-typed C# subclass of <see cref="TestBase"/>).
/// </summary>
public interface ITestExecutable
{
    string Name { get; }
    string? TestBed { get; }
    FailurePolicy? Policy { get; }
    TestCase ToTestCase();
    void OnBeforeTest(KeywordContext context);
    void OnAfterTest(KeywordContext context, TestRunResult result);
    void OnStepFailed(KeywordContext context, TestStep step, StepResult result);
}

/// <summary>
/// Strongly-typed OOP base class for authoring tests in C# (or via keyword sheets).
/// Supports virtual lifecycle hooks (<see cref="OnBeforeTest"/>, <see cref="OnAfterTest"/>, <see cref="OnStepFailed"/>)
/// and a fluent step-builder API.
/// </summary>
public abstract class TestBase : ITestExecutable
{
    public virtual string Name { get; set; }
    public virtual string? SourcePath { get; set; }
    public virtual string? TestBed { get; set; }
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<TestStep> Steps { get; set; } = new();
    public virtual FailurePolicy? Policy { get; set; }

    protected TestBase(string? name = null)
    {
        Name = name ?? GetType().Name;
    }

    /// <summary>Override in C# subclasses to configure steps or run custom setup before execution.</summary>
    public virtual void OnBeforeTest(KeywordContext context) { }

    /// <summary>Override in C# subclasses to run custom assertions or cleanup after execution.</summary>
    public virtual void OnAfterTest(KeywordContext context, TestRunResult result) { }

    /// <summary>Override in C# subclasses to react when an individual step fails.</summary>
    public virtual void OnStepFailed(KeywordContext context, TestStep step, StepResult result) { }

    // ---- Fluent OOP Step Builders ----

    public TestStep AddStep(string action, string target = "", string value = "", string options = "", string description = "")
    {
        var step = new TestStep
        {
            Index = Steps.Count + 1,
            Action = action.ToUpperInvariant(),
            Target = target,
            Value = value,
            Options = TestStep.ParseOptions(options),
            Description = description
        };
        Steps.Add(step);
        return step;
    }

    public TestStep AddStep(ITestStep customStep)
    {
        var step = customStep.ToTestStep();
        step.Index = Steps.Count + 1;
        Steps.Add(step);
        return step;
    }

    public TestStep AddLambdaStep(string name, Func<KeywordContext, StepResult> action, string description = "")
    {
        var step = new TestStep
        {
            Index = Steps.Count + 1,
            Action = name,
            Description = description,
            CustomExecutor = action
        };
        Steps.Add(step);
        return step;
    }

    // ---- Reserved Framework Header & Application Command Builders (AUT, COMMAND, RA, KAO) ----

    /// <summary>
    /// Reserved Header Keyword 1: <c>AUT</c> — Sets the Application Under Test location (executable path or TestBed name)
    /// directly in the test case (overriding any queue/config TestBed).
    /// </summary>
    public TestBase SetAut(string appOrTestBed, string args = "", string description = "Set Application Under Test (AUT) location directly in test")
    {
        AddStep("AUT", appOrTestBed, args, "", description);
        return this;
    }

    /// <summary>
    /// Reserved Header Keyword 2: <c>COMMAND</c> — Runs a comma-separated command list for app config, app arguments,
    /// or per-test framework attributes (e.g. <c>self_healing, video="external store from config loc", args="x,x"</c>).
    /// </summary>
    public TestBase WithCommand(string commandList, string description = "Framework & application comma-separated command list")
    {
        AddStep("COMMAND", "", commandList, "", description);
        return this;
    }

    /// <summary>
    /// Reserved Header Keyword 3: <c>RA</c> (<c>REUSE_APP</c>) — Controls whether to reuse an already-running instance
    /// of the AUT at the beginning of the test (<c>true</c> = <c>"1"</c>, <c>false</c> = <c>"0"</c>, <c>null</c> = <c>"config"</c>).
    /// </summary>
    public TestBase WithReuseApp(bool? reuse = true, string description = "Reuse App (RA) at beginning of test (1=reuse, 0=fresh, config=from config)")
    {
        string val = reuse == null ? "config" : reuse.Value ? "1" : "0";
        AddStep("RA", "", val, "", description);
        return this;
    }

    /// <summary>
    /// Reserved Header Keyword 4: <c>KAO</c> (<c>KEEP_APP_OPEN</c>) — Controls whether to keep the AUT open between tests
    /// (<c>true</c> = <c>"1"</c>, <c>false</c> = <c>"0"</c>, <c>null</c> = <c>"config"</c>).
    /// </summary>
    public TestBase WithKeepAppOpen(bool? keepOpen = true, string description = "Keep App Open (KAO) between tests (1=keep open, 0=close after test, config=from config)")
    {
        string val = keepOpen == null ? "config" : keepOpen.Value ? "1" : "0";
        AddStep("KAO", "", val, "", description);
        return this;
    }

    /// <summary>
    /// Ensures the 4 reserved framework header keywords (<c>AUT</c>, <c>COMMAND</c>, <c>RA</c>, <c>KAO</c>)
    /// are present at the very beginning of <see cref="Steps"/>, inserting or updating them as steps 1..4.
    /// </summary>
    public TestBase EnsureReservedHeader(
        string? aut = null,
        string? commandList = null,
        string? reuseApp = null,
        string? keepAppOpen = null)
    {
        string effectiveAut = aut ?? TestBed ?? "calc.exe";
        string effectiveCmd = commandList ?? "self_healing, highlight=\"1\", video=\"external store from config loc\"";
        string effectiveRa = reuseApp ?? "config";
        string effectiveKao = keepAppOpen ?? "config";

        // Remove any existing leading reserved header steps to avoid duplicates when re-seeding
        var bodySteps = Steps.SkipWhile(s => TestHeaderCommands.IsReservedHeaderKeyword(s.Action)).ToList();

        Steps.Clear();
        AddStep("AUT", effectiveAut, "", "", "Reserved: Set Application Under Test (AUT) location directly in test");
        AddStep("COMMAND", "", effectiveCmd, "", "Reserved: Comma-separated app/framework command list (e.g. name=\"x,x\" or video=\"external store from config loc\")");
        AddStep("RA", "", effectiveRa, "", "Reserved: Reuse App (RA) at beginning of test (1/true, 0/false, or config)");
        AddStep("KAO", "", effectiveKao, "", "Reserved: Keep App Open (KAO) between tests (1/true, 0/false, or config)");

        foreach (var s in bodySteps)
        {
            s.Index = Steps.Count + 1;
            Steps.Add(s);
        }

        return this;
    }

    public TestBase StartApp(string appOrTestBed, string args = "") { AddStep("START_APP", appOrTestBed, args); return this; }
    public TestBase Click(string target) { AddStep("CLICK", target); return this; }
    public TestBase UiaInvoke(string target) { AddStep("UIA_INVOKE", target); return this; }
    public TestBase Type(string target, string text) { AddStep("TYPE", target, text); return this; }
    public TestBase UiaSetValue(string target, string text) { AddStep("UIA_SET_VALUE", target, text); return this; }
    public TestBase Verify(string target, string expected) { AddStep("VERIFY", target, expected); return this; }
    public TestBase UiaVerify(string target, string expected) { AddStep("UIA_VERIFY", target, expected); return this; }
    public TestBase CvClickImage(string target, double threshold = 0.82) { AddStep("CV_CLICK_IMAGE", target, threshold.ToString("F2")); return this; }
    public TestBase CvSnapshotControl(string target, string filePath = "") { AddStep("CV_SNAPSHOT_CONTROL", target, filePath); return this; }
    public TestBase Highlight(string controlNames, string color = "", int intervalMs = 0)
    {
        string val = string.IsNullOrWhiteSpace(color) && intervalMs <= 0 ? "" : $"{color}|{(intervalMs > 0 ? intervalMs.ToString() : "")}";
        AddStep("HIGHLIGHT", controlNames, val);
        return this;
    }
    public TestBase Wait(int milliseconds) { AddStep("WAIT", "", milliseconds.ToString()); return this; }

    public virtual TestCase ToTestCase()
    {
        if (this is TestCase tc) return tc;
        return new TestCase
        {
            Name = Name,
            SourcePath = SourcePath,
            TestBed = TestBed,
            Tags = new List<string>(Tags),
            Variables = new Dictionary<string, string>(Variables, StringComparer.OrdinalIgnoreCase),
            Steps = Steps.Select(s => s.Clone()).ToList(),
            Policy = Policy?.Clone(),
            LifecycleOwner = this
        };
    }

    public override string ToString() => $"{Name} ({Steps.Count} steps)";
}

/// <summary>A named list of steps (one keyword sheet / worksheet / JSON test or fluent OOP test).</summary>
public class TestCase : TestBase
{
    public TestCase() : base("Test") { }
    public TestCase(string name) : base(name) { }

    /// <summary>When created from a custom <see cref="TestBase"/> subclass, forwards lifecycle callbacks to it.</summary>
    [JsonIgnore] public ITestExecutable? LifecycleOwner { get; set; }

    public override void OnBeforeTest(KeywordContext context) => LifecycleOwner?.OnBeforeTest(context);
    public override void OnAfterTest(KeywordContext context, TestRunResult result) => LifecycleOwner?.OnAfterTest(context, result);
    public override void OnStepFailed(KeywordContext context, TestStep step, StepResult result) => LifecycleOwner?.OnStepFailed(context, step, result);
}
