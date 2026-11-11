using System.Drawing;
using System.Windows.Automation;
using WinFrame.Core.Controls;
using WinFrame.Core.Execution;
using WinFrame.Core.Keywords;
using WinFrame.Core.Native;
using WinFrame.Core.Processes;
using WinFrame.Core.Uia;
using WinFrame.Core.Vision;

namespace WinFrame.Core.Domain;

/// <summary>
/// Observer interface for hooking into test queue, test case, and step lifecycle events in an OOP fashion.
/// Inherit <see cref="TestExecutionListenerBase"/> to override only the events you care about.
/// </summary>
public interface ITestExecutionListener
{
    void OnQueueStarted(QueueRunResult queue);
    void OnTestStarted(TestCase test, TestRunResult result);
    void OnStepStarted(TestCase test, TestStep step, int attempt);
    void OnStepCompleted(TestCase test, TestStep step, StepResult result);
    void OnTestCompleted(TestCase test, TestRunResult result);
    void OnQueueCompleted(QueueRunResult queue);
}

/// <summary>No-op base implementation of <see cref="ITestExecutionListener"/> for easy subclassing.</summary>
public abstract class TestExecutionListenerBase : ITestExecutionListener
{
    public virtual void OnQueueStarted(QueueRunResult queue) { }
    public virtual void OnTestStarted(TestCase test, TestRunResult result) { }
    public virtual void OnStepStarted(TestCase test, TestStep step, int attempt) { }
    public virtual void OnStepCompleted(TestCase test, TestStep step, StepResult result) { }
    public virtual void OnTestCompleted(TestCase test, TestRunResult result) { }
    public virtual void OnQueueCompleted(QueueRunResult queue) { }
}

/// <summary>
/// Extensible contract for an automation driver (Win32, UIA, OpenCV, or custom user drivers).
/// </summary>
public interface IAutomationDriver
{
    string Name { get; }
    bool CanResolve(string target, KeywordContext context);
    bool Click(string target, KeywordContext context);
    bool SetValue(string target, string value, KeywordContext context);
    string GetValue(string target, KeywordContext context);
    bool VerifyValue(string target, string expected, KeywordContext context);
    byte[] CaptureSnapshot(string target, KeywordContext context);
}

/// <summary>Abstract base class for building custom automation drivers.</summary>
public abstract class AutomationDriverBase : IAutomationDriver
{
    public abstract string Name { get; }
    public virtual bool CanResolve(string target, KeywordContext context) => true;
    public abstract bool Click(string target, KeywordContext context);
    public abstract bool SetValue(string target, string value, KeywordContext context);
    public abstract string GetValue(string target, KeywordContext context);
    public virtual bool VerifyValue(string target, string expected, KeywordContext context) =>
        GetValue(target, context).Contains(expected, StringComparison.OrdinalIgnoreCase);
    public virtual byte[] CaptureSnapshot(string target, KeywordContext context) =>
        OpenCvAutomation.CaptureWindowPng(context.CurrentWindowHwnd);
}

public sealed class Win32Driver : AutomationDriverBase
{
    public override string Name => "Win32";
    public override bool Click(string target, KeywordContext context)
    {
        var h = context.ResolveHwnd(target);
        if (h == IntPtr.Zero) return false;
        Win32Automation.SendNativeInput(h, "CLICK");
        return true;
    }
    public override bool SetValue(string target, string value, KeywordContext context)
    {
        var h = context.ResolveHwnd(target);
        if (h == IntPtr.Zero) return false;
        Win32Automation.SendNativeInput(h, "TYPE", value);
        return true;
    }
    public override string GetValue(string target, KeywordContext context) =>
        Win32Automation.GetControlText(context.ResolveHwnd(target));
    public override bool VerifyValue(string target, string expected, KeywordContext context) =>
        Win32Automation.VerifyDataValue(context.ResolveHwnd(target), expected);
    public override byte[] CaptureSnapshot(string target, KeywordContext context) =>
        OpenCvAutomation.CaptureControlPng(context.ResolveHwnd(target));
}

public sealed class UiaDriver : AutomationDriverBase
{
    public override string Name => "UIA";
    public override bool Click(string target, KeywordContext context) =>
        UiaAutomation.Invoke(context.ResolveElement(target)!);
    public override bool SetValue(string target, string value, KeywordContext context) =>
        UiaAutomation.SetValue(context.ResolveElement(target)!, value);
    public override string GetValue(string target, KeywordContext context) =>
        UiaAutomation.GetValue(context.ResolveElement(target)!);
    public override bool VerifyValue(string target, string expected, KeywordContext context) =>
        UiaAutomation.VerifyValue(context.ResolveElement(target), expected);
    public override byte[] CaptureSnapshot(string target, KeywordContext context) =>
        OpenCvAutomation.CaptureElementPng(context.ResolveElement(target));
}

public sealed class OpenCvDriver : AutomationDriverBase
{
    public override string Name => "OpenCV";
    public override bool Click(string target, KeywordContext context) =>
        OpenCvAutomation.KeywordClickImage(context, target);
    public override bool SetValue(string target, string value, KeywordContext context)
    {
        if (!OpenCvAutomation.KeywordClickImage(context, target)) return false;
        Win32Automation.TypeUnicodeText(value);
        return true;
    }
    public override string GetValue(string target, KeywordContext context) => "";
    public override bool VerifyValue(string target, string expected, KeywordContext context) =>
        OpenCvAutomation.KeywordVerifyImage(context, target, double.TryParse(expected, out var d) ? d : 0.82);
}

/// <summary>
/// Strongly-typed OOP representation of a Windows UI control.
/// Combines Win32 (<see cref="Hwnd"/>), UI Automation (<see cref="Element"/>), OpenCV vision,
/// and the <see cref="ControlDefinition"/> metadata into a single fluent object.
/// Subclass <see cref="UIControl"/> (or use <see cref="UIButton"/>, <see cref="UITextBox"/>, <see cref="UICheckBox"/>)
/// when building Page Object / Screen Object models.
/// </summary>
public class UIControl
{
    public KeywordContext Context { get; }
    public string LocatorOrName { get; }
    public ControlDefinition? Definition => Context.Controls.Get(LocatorOrName);

    public UIControl(KeywordContext context, string locatorOrName)
    {
        Context = context;
        LocatorOrName = locatorOrName;
    }

    public AutomationElement? Element => Context.ResolveElement(LocatorOrName);
    public IntPtr Hwnd => Context.ResolveHwnd(LocatorOrName);
    public bool Exists => Element != null || (Hwnd != IntPtr.Zero && NativeMethods.IsWindow(Hwnd));
    public bool IsEnabled => Element?.Current.IsEnabled ?? (Hwnd != IntPtr.Zero && NativeMethods.IsWindowEnabled(Hwnd));
    public Rectangle Bounds => Element is { } el ? UiaAutomation.GetBounds(el)
        : Hwnd != IntPtr.Zero ? ScreenLocationManager.ToRectangle(ScreenLocationManager.GetAbsoluteControlBounds(Hwnd))
        : Rectangle.Empty;

    public virtual UIControl Click()
    {
        if (Element is { } el) UiaAutomation.Invoke(el);
        else Win32Automation.SendNativeInput(Hwnd, "CLICK");
        return this;
    }

    public virtual UIControl NativeClick()
    {
        Win32Automation.SendNativeInput(Hwnd, "CLICK");
        return this;
    }

    public virtual UIControl DoubleClick()
    {
        if (Element is { } el) UiaAutomation.DoubleClick(el);
        else Win32Automation.SendNativeInput(Hwnd, "DOUBLE_CLICK");
        return this;
    }

    public virtual UIControl RightClick()
    {
        if (Element is { } el) UiaAutomation.RightClick(el);
        else Win32Automation.SendNativeInput(Hwnd, "RIGHT_CLICK");
        return this;
    }

    public virtual UIControl ClickByImage(double threshold = 0.82)
    {
        if (!OpenCvAutomation.KeywordClickImage(Context, LocatorOrName, threshold))
            throw new InvalidOperationException($"OpenCV template match failed for '{LocatorOrName}' at threshold {threshold:F2}.");
        return this;
    }

    public virtual UIControl Highlight(string? color = null, int? intervalMs = null)
    {
        Win32Highlighter.KeywordHighlight(Context, LocatorOrName, color ?? "", intervalMs ?? 0);
        return this;
    }

    public virtual UIControl Type(string text)
    {
        if (Element is { } el) UiaAutomation.SetValue(el, text);
        else Win32Automation.SendNativeInput(Hwnd, "TYPE", text);
        return this;
    }

    public virtual string GetText() =>
        Element is { } el ? UiaAutomation.GetValue(el) : Win32Automation.GetControlText(Hwnd);

    public virtual bool VerifyText(string expected) =>
        Element is { } el ? UiaAutomation.VerifyValue(el, expected) : Win32Automation.VerifyDataValue(Hwnd, expected);

    public virtual byte[] CaptureSnapshot(bool saveToRepository = true)
    {
        var el = Element;
        byte[] png = el != null ? OpenCvAutomation.CaptureElementPng(el) : OpenCvAutomation.CaptureControlPng(Hwnd);
        if (saveToRepository && Definition is { } def)
        {
            OpenCvAutomation.RecordControlSnapshot(Context.Controls, def, Context.CurrentWindowHwnd, el, Hwnd);
        }
        return png;
    }
}

public sealed class UIButton(KeywordContext ctx, string locator) : UIControl(ctx, locator)
{
    public UIButton Invoke() { UiaAutomation.Invoke(Element!); return this; }
}

public sealed class UITextBox(KeywordContext ctx, string locator) : UIControl(ctx, locator)
{
    public string Value { get => GetText(); set => Type(value); }
}

public sealed class UICheckBox(KeywordContext ctx, string locator) : UIControl(ctx, locator)
{
    public UICheckBox Toggle(string desiredState = "") { UiaAutomation.Toggle(Element!, desiredState); return this; }
    public bool IsChecked => UiaAutomation.CheckToggleState(Element, "On");
}

/// <summary>
/// Base class for the Screen Object / Page Object pattern in Windows desktop automation.
/// Subclass <see cref="ScreenObject"/> to encapsulate a window's controls and high-level user workflows.
/// </summary>
public abstract class ScreenObject
{
    public KeywordContext Context { get; }
    public virtual string WindowTitlePattern { get; }

    protected ScreenObject(KeywordContext context, string windowTitlePattern = "")
    {
        Context = context;
        WindowTitlePattern = windowTitlePattern;
    }

    public IntPtr Hwnd
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(WindowTitlePattern))
            {
                var found = ProcessManagementEngine.FindTopLevelWindow(WindowTitlePattern);
                if (found != IntPtr.Zero) Context.CurrentWindowHwnd = found;
            }
            return Context.CurrentWindowHwnd;
        }
    }

    public UIControl Control(string locatorOrName) => new(Context, locatorOrName);
    public UIButton Button(string locatorOrName) => new(Context, locatorOrName);
    public UITextBox TextBox(string locatorOrName) => new(Context, locatorOrName);
    public UICheckBox CheckBox(string locatorOrName) => new(Context, locatorOrName);

    public bool WaitForReady(int timeoutMs = 10000) =>
        string.IsNullOrWhiteSpace(WindowTitlePattern) || ProcessManagementEngine.KeywordWaitForWindow(Context, WindowTitlePattern, timeoutMs);

    public List<ControlDefinition> ScrapeControls() => UiaAutomation.ScrapeWindow(Hwnd, Context.Controls);
    public byte[] CaptureWindowPng() => OpenCvAutomation.CaptureWindowPng(Hwnd);
}
