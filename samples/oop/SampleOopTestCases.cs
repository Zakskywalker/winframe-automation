using WinFrame.Core.Controls;
using WinFrame.Core.Domain;
using WinFrame.Core.Execution;
using WinFrame.Core.Keywords;

namespace WinFrame.Samples.Oop;

/// <summary>
/// Example 1: Strongly-typed ScreenObject modeling a target window and its controls
/// (<see cref="UIButton"/>, <see cref="UITextBox"/>, <see cref="UICheckBox"/>).
/// </summary>
public sealed class SampleLoginScreen : ScreenObject
{
    public UITextBox UsernameBox { get; }
    public UITextBox PasswordBox { get; }
    public UICheckBox RememberMeCheck { get; }
    public UIButton SignInButton { get; }

    public SampleLoginScreen(KeywordContext context)
        : base("SampleLoginWindow", context)
    {
        UsernameBox = TextBox("edit_1");
        PasswordBox = TextBox("edit_2");
        RememberMeCheck = CheckBox("checkbox_1");
        SignInButton = Button("button_1");
    }

    public void PerformLogin(string username, string password, bool rememberMe = true)
    {
        HighlightAll("#F59E0B", 200);
        UsernameBox.EnterText(username);
        PasswordBox.EnterText(password);
        RememberMeCheck.SetChecked(rememberMe);
        SignInButton.Click();
    }
}

/// <summary>
/// Example 2: Strongly-typed OOP Test Case (<see cref="TestBase"/>) starting with the reserved
/// framework header keywords (<c>AUT</c>, <c>COMMAND</c>, <c>RA</c>, <c>KAO</c>), followed by
/// fluent step builders, lambda steps, and automatic step-queue / inverse support.
/// </summary>
public sealed class SampleCalculatorAndRollbackOopTest : TestBase
{
    public SampleCalculatorAndRollbackOopTest()
        : base("Sample_Calculator_And_Rollback_OOP")
    {
        TestBed = "Calculator";
        Variables["highlight"] = "1";

        // Reserved Framework Header Section (AUT, COMMAND, RA, KAO)
        SetAut("calc.exe", "", "Reserved: Set Application Under Test (AUT) location directly in test")
            .WithCommand("self_healing, highlight=\"1\", video=\"external store from config loc\", args=\"--mode=test,fast\"", "Reserved: Comma-separated command list")
            .WithReuseApp(true, "Reserved: Reuse App (RA) at beginning of test")
            .WithKeepAppOpen(true, "Reserved: Keep App Open (KAO) between tests");

        // Test Body Steps
        AddStep("SET_VAR", "calc_state", "ready", "", "Initialize calculator state variable");
        AddStep("HIGHLIGHT", "@button_1,@button_2", "#F59E0B|220|3", "", "Outline buttons with Win32 GDI pen");
        AddStep("SET_VAR", "accumulator", "10", "", "Set accumulator to 10");

        AddLambdaStep("OOP_COMPUTE_TRIPLE", ctx =>
        {
            int current = int.Parse(ctx.Variables["accumulator"]);
            int tripled = current * 3;
            ctx.Variables["accumulator"] = tripled.ToString();
            return StepResult.Pass(Steps.Count, "OOP_COMPUTE_TRIPLE", "accumulator", tripled.ToString(), $"Tripled {current} -> {tripled}");
        });

        AddStep("ASSERT_VAR", "accumulator", "30", "", "Verify accumulator equals 30");
    }

    public override void OnBeforeTest(KeywordContext context)
    {
        context.Variables["started_by"] = "SampleCalculatorAndRollbackOopTest";
    }

    public override void OnAfterTest(KeywordContext context, TestRunResult result)
    {
        context.Variables["final_status"] = result.Status.ToString();
    }
}
