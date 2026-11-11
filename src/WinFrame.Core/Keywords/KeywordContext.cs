using System.Text.RegularExpressions;
using System.Windows.Automation;
using WinFrame.Core.Config;
using WinFrame.Core.Controls;
using WinFrame.Core.Domain;
using WinFrame.Core.Native;
using WinFrame.Core.Processes;
using WinFrame.Core.Uia;

namespace WinFrame.Core.Keywords;

/// <summary>
/// Runtime state shared across steps of a running test case.
/// Provides variable expansion (<c>${var}</c>), target resolution (to <see cref="IntPtr"/>,
/// <see cref="AutomationElement"/>, or <see cref="UIControl"/>), and access to the active
/// <see cref="ControlRepository"/> and <see cref="ProcessManagementEngine"/>.
/// </summary>
public sealed class KeywordContext
{
    private static readonly Regex VarPattern = new(@"\$\{(?<name>[A-Za-z0-9_.\-]+)\}", RegexOptions.Compiled);

    public KeywordContext(ControlRepository? controls = null, AppConfig? config = null, TestBedConfig? testBed = null)
    {
        Config = config ?? ConfigManager.Current;
        Controls = controls ?? new ControlRepository().Load();
        TestBed = testBed;

        foreach (var kv in Config.Variables) Variables[kv.Key] = kv.Value;
        Variables["username"] = Config.Credentials.Username;
        Variables["domain"] = Config.Credentials.Domain;
        Variables["password"] = Config.Credentials.Password;
        Variables["highlight"] = Config.Highlight ?? "";
        Variables["highlight_color"] = Config.HighlightColor;
        Variables["highlight_interval"] = Config.HighlightIntervalMs.ToString();

        if (testBed != null)
        {
            foreach (var kv in testBed.Variables) Variables[kv.Key] = kv.Value;
            if (testBed.Credentials != null)
            {
                Variables["username"] = testBed.Credentials.Username;
                Variables["domain"] = testBed.Credentials.Domain;
                Variables["password"] = testBed.Credentials.Password;
            }
        }
    }

    public AppConfig Config { get; }
    public ControlRepository Controls { get; }
    public TestBedConfig? TestBed { get; set; }
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Active window handle under test (updated by START_APP / ATTACH_APP / REHARNESS).</summary>
    public IntPtr CurrentWindowHwnd { get; set; }

    /// <summary>Return value of the most recently executed keyword.</summary>
    public object? LastResult { get; set; }

    /// <summary>Path to the most recently captured screenshot.</summary>
    public string? LastScreenshotPath { get; set; }

    /// <summary>When true, every control interaction captures OpenCV control + window snapshots into <see cref="Controls"/>.</summary>
    public bool RecordOpenCvSnapshots { get; set; }

    /// <summary>
    /// True when the <c>highlight</c> variable (or config setting) is set to <c>"1"</c> or <c>"true"</c>;
    /// false when set to <c>"0"</c>, <c>""</c>, or <c>null</c>.
    /// </summary>
    public bool IsHighlightEnabled =>
        Variables.TryGetValue("highlight", out var hv)
            ? AppConfig.IsHighlightValueOn(hv)
            : Config.IsHighlightEnabled;

    public string HighlightColor =>
        Variables.TryGetValue("highlight_color", out var hc) && !string.IsNullOrWhiteSpace(hc)
            ? hc
            : Config.HighlightColor;

    public int HighlightIntervalMs =>
        Variables.TryGetValue("highlight_interval", out var hi) && int.TryParse(hi, out int ms) && ms > 0
            ? ms
            : Config.HighlightIntervalMs;

    /// <summary>Expands <c>${var}</c> tokens using <see cref="Variables"/>.</summary>
    public string Expand(string? input)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains("${")) return input ?? "";
        return VarPattern.Replace(input, m =>
            Variables.TryGetValue(m.Groups["name"].Value, out var val) ? val : m.Value);
    }

    /// <summary>Returns a strongly-typed <see cref="UIControl"/> wrapper for the given control name or locator.</summary>
    public UIControl Control(string locatorOrName) => new(this, Expand(locatorOrName));

    /// <summary>Resolves a target string to a Win32 HWND (checking ControlRepository, UIA NativeWindowHandle, or active window).</summary>
    public IntPtr ResolveHwnd(string? target, int timeoutMs = 3000)
    {
        target = Expand(target);
        if (string.IsNullOrWhiteSpace(target))
        {
            if (CurrentWindowHwnd != IntPtr.Zero && NativeMethods.IsWindow(CurrentWindowHwnd))
                return CurrentWindowHwnd;
            var harnessed = ProcessManagementEngine.Shared.EnsureHarnessed(restartIfExited: false);
            return harnessed != IntPtr.Zero ? harnessed : NativeMethods.GetForegroundWindow();
        }

        if (UiaAutomation.TryParseHwnd(target, out IntPtr direct) && NativeMethods.IsWindow(direct))
            return direct;

        if (Controls.TryGet(target, out var def))
        {
            var hStr = def.Get(ControlDefinition.Keys.NativeWindowHandle);
            if (UiaAutomation.TryParseHwnd(hStr, out IntPtr repoHwnd) && NativeMethods.IsWindow(repoHwnd))
                return repoHwnd;
        }

        var el = ResolveElement(target, timeoutMs);
        if (el != null)
        {
            try
            {
                if (el.Current.NativeWindowHandle != 0)
                    return new IntPtr(el.Current.NativeWindowHandle);
            }
            catch { }
        }

        var win = ProcessManagementEngine.FindTopLevelWindow(target, target);
        if (win != IntPtr.Zero) return win;

        return CurrentWindowHwnd;
    }

    /// <summary>Resolves a target string to a UI Automation <see cref="AutomationElement"/>.</summary>
    public AutomationElement? ResolveElement(string? target, int timeoutMs = 5000)
    {
        target = Expand(target);
        if (CurrentWindowHwnd == IntPtr.Zero || !NativeMethods.IsWindow(CurrentWindowHwnd))
        {
            var re = ProcessManagementEngine.Shared.EnsureHarnessed(restartIfExited: false);
            if (re != IntPtr.Zero) CurrentWindowHwnd = re;
        }
        return UiaAutomation.FindElement(target, CurrentWindowHwnd, Controls, timeoutMs);
    }
}
