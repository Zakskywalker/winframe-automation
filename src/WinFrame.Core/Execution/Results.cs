using System.Text.Json;
using System.Text.Json.Serialization;
using WinFrame.Core.Config;

namespace WinFrame.Core.Execution;

public enum TestStatus { Pending, Running, Passed, Failed, Error, Skipped, Stopped }

/// <summary>Base class for any execution result in the WinFrame object model.</summary>
public abstract class ExecutionResultBase
{
    public TestStatus Status { get; set; } = TestStatus.Pending;
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore] public TimeSpan Duration => Finished > Started ? Finished - Started : TimeSpan.Zero;
    public double DurationMs => Duration.TotalMilliseconds;
    [JsonIgnore] public bool IsSuccess => Status == TestStatus.Passed;
    [JsonIgnore] public bool IsTerminalFailure => Status is TestStatus.Failed or TestStatus.Error or TestStatus.Stopped;

    public virtual void MarkStarted()
    {
        Started = DateTime.Now;
        Status = TestStatus.Running;
    }

    public virtual void MarkFinished(TestStatus status)
    {
        Finished = DateTime.Now;
        Status = status;
    }

    public string ToJson() => JsonSerializer.Serialize(this, GetType(), ConfigManager.JsonOptions);
}

/// <summary>Strongly-typed result of a single step execution.</summary>
public class StepResult : ExecutionResultBase
{
    public int Index { get; set; }
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Value { get; set; } = "";
    public string Message { get; set; } = "";
    public int Attempts { get; set; }
    public string? Screenshot { get; set; }
    public object? OutputValue { get; set; }
    [JsonIgnore] public Exception? Exception { get; set; }

    public static StepResult Pass(int index, string action, string target, string value, string message = "OK", object? output = null)
    {
        var r = new StepResult { Index = index, Action = action, Target = target, Value = value, Message = message, Attempts = 1, OutputValue = output };
        r.Started = r.Finished = DateTime.Now;
        r.Status = TestStatus.Passed;
        return r;
    }

    public static StepResult Fail(int index, string action, string target, string value, string error, Exception? ex = null, string? screenshot = null)
    {
        var r = new StepResult { Index = index, Action = action, Target = target, Value = value, Message = error, Exception = ex, Screenshot = screenshot, Attempts = 1 };
        r.Started = r.Finished = DateTime.Now;
        r.Status = TestStatus.Failed;
        return r;
    }

    public override string ToString() => $"[Step {Index}] {Action} ({Target}) -> {Status} ({DurationMs:F0} ms): {Message}";
}

/// <summary>Strongly-typed result of a complete test case execution.</summary>
public class TestRunResult : ExecutionResultBase
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string TestName { get; set; } = "";
    public string? SourcePath { get; set; }
    public string? TestBed { get; set; }
    public int Attempts { get; set; }
    public string Error { get; set; } = "";
    public List<StepResult> Steps { get; set; } = new();
    public List<string> Attachments { get; set; } = new();
    public Dictionary<string, string> OutputVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HardStop { get; set; }

    public int FailedSteps => Steps.Count(s => s.Status is TestStatus.Failed or TestStatus.Error);
    public int PassedSteps => Steps.Count(s => s.Status == TestStatus.Passed);
    public double PassRate => Steps.Count == 0 ? 1.0 : (double)PassedSteps / Steps.Count;

    public void AddStep(StepResult step)
    {
        Steps.Add(step);
        if (!string.IsNullOrWhiteSpace(step.Screenshot) && !Attachments.Contains(step.Screenshot))
            Attachments.Add(step.Screenshot);
    }

    /// <summary>Throws an <see cref="InvalidOperationException"/> if the test did not pass (useful in unit/integration tests).</summary>
    public TestRunResult EnsurePassed()
    {
        if (!IsSuccess)
            throw new InvalidOperationException($"Test '{TestName}' did not pass (Status={Status}): {Error}");
        return this;
    }

    public override string ToString() => $"{TestName}: {Status} ({PassedSteps}/{Steps.Count} steps passed in {Duration.TotalSeconds:F2}s)";
}

/// <summary>Strongly-typed result of a test queue / suite execution.</summary>
public class QueueRunResult : ExecutionResultBase
{
    public string QueueId { get; set; } = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    public string Name { get; set; } = "Queue";
    public string Machine { get; set; } = Environment.MachineName;
    public bool Stopped { get; set; }
    public string StopReason { get; set; } = "";
    public List<TestRunResult> Tests { get; set; } = new();
    public string? ReportPath { get; set; }

    public int Passed => Tests.Count(t => t.Status == TestStatus.Passed);
    public int Failed => Tests.Count(t => t.Status is TestStatus.Failed or TestStatus.Error);
    public int Skipped => Tests.Count(t => t.Status is TestStatus.Skipped or TestStatus.Stopped);
    public int TotalSteps => Tests.Sum(t => t.Steps.Count);
    public double PassRate => Tests.Count == 0 ? 1.0 : (double)Passed / Tests.Count;

    public void AddTestResult(TestRunResult result)
    {
        Tests.Add(result);
        Status = Failed > 0 ? TestStatus.Failed : Stopped ? TestStatus.Stopped : TestStatus.Passed;
    }

    public QueueRunResult EnsureAllPassed()
    {
        if (Failed > 0 || Stopped)
            throw new InvalidOperationException($"Queue '{Name}' had {Failed} failed test(s). StopReason='{StopReason}'.");
        return this;
    }

    public override string ToString() => $"Queue '{Name}': {Passed} passed, {Failed} failed, {Skipped} skipped ({Duration.TotalSeconds:F1}s)";
}
