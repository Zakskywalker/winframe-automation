namespace WinFrame.Core.Execution;

/// <summary>What to do when a step fails (after its own step-level retries are exhausted).</summary>
public enum FailureAction
{
    /// <summary>Record the failure and continue with the next step.</summary>
    Continue,
    /// <summary>Retry the failing step up to <see cref="FailurePolicy.StepRetryCount"/> times, then apply <see cref="FailurePolicy.AfterRetriesExhausted"/>.</summary>
    RetryStep,
    /// <summary>Restart the whole test from step 1, up to <see cref="FailurePolicy.TestRetryCount"/> times.</summary>
    RetryFromStart,
    /// <summary>Stop this test, move on to the next test in the queue.</summary>
    StopTest,
    /// <summary>Stop the whole queue (graceful: current test is finalised, report is generated).</summary>
    StopQueue
}

/// <summary>
/// Threshold-based failure handling. The engine never lets a failing test crash the host application;
/// these values decide how far a failure propagates.
/// </summary>
public sealed class FailurePolicy
{
    /// <summary>Primary action on step failure.</summary>
    public FailureAction OnStepFailure { get; set; } = FailureAction.RetryStep;

    /// <summary>Action once step retries are used up (when <see cref="OnStepFailure"/> is RetryStep).</summary>
    public FailureAction AfterRetriesExhausted { get; set; } = FailureAction.StopTest;

    public int StepRetryCount { get; set; } = 2;
    public int TestRetryCount { get; set; } = 1;
    public int RetryDelayMs { get; set; } = 500;

    /// <summary>Failed steps allowed within one test before it is stopped (0 = unlimited).</summary>
    public int MaxFailuresPerTest { get; set; } = 5;

    /// <summary>Failed tests allowed in a queue run before the queue is stopped (0 = unlimited).</summary>
    public int MaxFailedTestsPerQueue { get; set; } = 0;

    /// <summary>
    /// Full-stop indicator: once the total number of failed steps across the queue reaches this value,
    /// everything stops immediately no matter what other settings say (0 = disabled).
    /// A step flagged <c>critical=true</c> that fails is always a full stop.
    /// </summary>
    public int HardStopThreshold { get; set; } = 50;

    /// <summary>Default per-step timeout; a hung step is abandoned and failed.</summary>
    public int StepTimeoutMs { get; set; } = 60000;

    /// <summary>
    /// When &gt; 0, automatically executes the inverse of the last N steps (via <see cref="StepInversionEngine"/>)
    /// whenever a step fails (e.g. clearing entered text, unchecking toggled controls).
    /// </summary>
    public int AutoInverseRollbackSteps { get; set; } = 0;

    public FailurePolicy Clone() => (FailurePolicy)MemberwiseClone();
}

