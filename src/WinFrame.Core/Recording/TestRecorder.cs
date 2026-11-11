using System.Drawing;
using System.Text;
using System.Windows.Automation;
using System.Windows.Forms;
using WinFrame.Core.Config;
using WinFrame.Core.Controls;
using WinFrame.Core.Keywords;
using WinFrame.Core.Logging;
using WinFrame.Core.Native;
using WinFrame.Core.Uia;
using WinFrame.Core.Vision;

namespace WinFrame.Core.Recording;

/// <summary>
/// Live summary of the control currently under the mouse cursor, combining
/// UIA Mouse-Point Scrape (<see cref="UiaAutomation.ElementFromPoint"/>) and
/// Win32 Window-to-Control Position (<see cref="ScreenLocationManager.GetWindowToControlPosition"/>).
/// </summary>
public sealed record HoveredControlInfo(
    string SuggestedName,
    string ControlType,
    string AutomationId,
    string UiaName,
    string WindowTitle,
    Point ScreenPoint,
    Point WindowRelativePoint,
    Point ControlRelativePoint,
    Rectangle ControlRelativeBounds,
    IntPtr RootWindowHwnd,
    IntPtr ControlHwnd);

/// <summary>
/// Live Test Recorder &amp; Control Scraper ("Create Tests" pipeline).
/// <para>
/// Designed to be <b>Antivirus-safe (no global <c>WH_KEYBOARD_LL</c> / <c>WH_MOUSE_LL</c> keylogger hooks)</b>.
/// While recording is active, combines:
/// <list type="number">
/// <item><b>Mouse-Position Scrape</b> (<see cref="UiaAutomation.ElementFromPoint"/>) to harvest UIA patterns, AutomationId, Name, and ControlType.</item>
/// <item><b>Window-to-Control Position</b> (<see cref="ScreenLocationManager.GetWindowToControlPosition"/>) to resolve the child HWND and relative coordinates inside the target window.</item>
/// <item><b>Keyboard Data Input Capture</b> via focused UIA/Win32 control value tracking plus edge-triggered key state sampling while the target app is active.</item>
/// </list>
/// </para>
/// </summary>
public sealed class TestRecorder : IDisposable
{
    private System.Threading.Timer? pollTimer;
    private readonly object syncLock = new();
    private readonly int ownProcessId = Environment.ProcessId;

    private bool wasLeftDown;
    private bool wasRightDown;
    private DateTime lastLeftClickTime = DateTime.MinValue;
    private Point lastLeftClickPoint;
    private Point lastHoverPoint;

    private string? activeTargetControl;
    private IntPtr activeTargetHwnd;
    private AutomationElement? activeTargetElement;
    private string lastObservedControlText = "";

    public ControlRepository Controls { get; }
    public TestCase CurrentTest { get; private set; }
    public IntPtr TargetWindowHwnd { get; set; }
    public HashSet<IntPtr> IgnoredWindowHwnds { get; } = new();
    public bool CaptureOpenCvSnapshots { get; set; } = true;
    public bool IsRecording { get; private set; }

    /// <summary>Raised whenever a new <see cref="TestStep"/> is recorded or updated.</summary>
    public event Action<TestStep>? StepRecorded;

    /// <summary>Raised whenever a control is discovered or updated during scraping/recording.</summary>
    public event Action<ControlDefinition>? ControlScraped;

    /// <summary>Raised during recording when the mouse moves over a control, reporting both UIA scrape &amp; Window-to-Control coordinates.</summary>
    public event Action<HoveredControlInfo>? HoverControlChanged;

    public TestRecorder(ControlRepository controls, string testName = "RecordedTest")
    {
        Controls = controls;
        CurrentTest = new TestCase(testName);
        CaptureOpenCvSnapshots = ConfigManager.Current.RecorderCaptureImages;
    }

    /// <summary>
    /// Scrapes all controls on <paramref name="windowHwnd"/> and returns them for the accessory naming panel.
    /// Assigns <c>ctrltype_x</c> default names automatically.
    /// </summary>
    public List<ControlDefinition> ScrapeWindowToMockTable(IntPtr windowHwnd, bool commitToRepository = true)
    {
        if (windowHwnd == IntPtr.Zero) windowHwnd = NativeMethods.GetForegroundWindow();
        TargetWindowHwnd = windowHwnd;
        var scraped = UiaAutomation.ScrapeWindow(windowHwnd, Controls);

        if (CaptureOpenCvSnapshots && scraped.Count > 0)
        {
            byte[] winPng = OpenCvAutomation.CaptureWindowPng(windowHwnd);
            foreach (var def in scraped.Take(60))
            {
                byte[]? ctrlPng = null;
                if (def.Attributes.TryGetValue(ControlDefinition.Keys.BoundingRectangle, out var bStr))
                {
                    var rect = OpenCvAutomation.ParseRect(bStr);
                    if (rect.Width > 2 && rect.Height > 2) ctrlPng = OpenCvAutomation.CaptureRegionPng(rect);
                }
                Controls.SetImages(def, ctrlPng, winPng);
            }
        }

        if (commitToRepository)
        {
            foreach (var def in scraped)
            {
                Controls.AddOrUpdate(def);
                ControlScraped?.Invoke(def);
            }
        }

        SystemLogger.User($"Scraped {scraped.Count} controls from window HWND 0x{windowHwnd.ToInt64():X} for accessory table.");
        return scraped;
    }

    /// <summary>
    /// Combines Mouse-Position UIA Scrape (<see cref="UiaAutomation.ElementFromPoint"/>) and
    /// Window-to-Control Position (<see cref="ScreenLocationManager.GetWindowToControlPosition"/>)
    /// at the specified screen coordinates, registering or updating the control in <see cref="Controls"/>.
    /// </summary>
    public (ControlDefinition Control, HoveredControlInfo HoverInfo, AutomationElement? Element) ScrapeControlAtMouseAndWindowPosition(
        int screenX,
        int screenY,
        bool commitToRepository = true)
    {
        var winPos = ScreenLocationManager.GetWindowToControlPosition(IntPtr.Zero, screenX, screenY);
        var el = UiaAutomation.ElementFromPoint(screenX, screenY);

        Dictionary<string, string> attrs;
        if (el != null)
        {
            attrs = UiaAutomation.ExtractAttributes(el, windowTitle: winPos.WindowTitle);
        }
        else
        {
            attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ControlDefinition.Keys.Name] = winPos.ControlText,
                [ControlDefinition.Keys.ClassName] = winPos.ControlClassName,
                [ControlDefinition.Keys.ControlType] = string.IsNullOrWhiteSpace(winPos.ControlClassName) ? "Custom" : winPos.ControlClassName,
                [ControlDefinition.Keys.WindowTitle] = winPos.WindowTitle,
                [ControlDefinition.Keys.NativeWindowHandle] = $"0x{winPos.ControlHwnd.ToInt64():X}",
                [ControlDefinition.Keys.BoundingRectangle] = winPos.ControlScreenRect.ToString()
            };
        }

        // Enrich with Window-to-Control relative position metadata from ScreenLocationManager
        attrs[ControlDefinition.Keys.RelativeBounds] =
            $"{winPos.ControlRelativeBounds.Left},{winPos.ControlRelativeBounds.Top},{winPos.ControlRelativeBounds.Width},{winPos.ControlRelativeBounds.Height}";
        attrs["Win32.WindowRelX"] = winPos.WindowRelativePoint.X.ToString();
        attrs["Win32.WindowRelY"] = winPos.WindowRelativePoint.Y.ToString();
        attrs["Win32.ControlRelX"] = winPos.ControlRelativePoint.X.ToString();
        attrs["Win32.ControlRelY"] = winPos.ControlRelativePoint.Y.ToString();
        attrs["Win32.CtrlId"] = winPos.ControlId.ToString();
        if (!string.IsNullOrWhiteSpace(winPos.ControlClassName) && !attrs.ContainsKey(ControlDefinition.Keys.ClassName))
            attrs[ControlDefinition.Keys.ClassName] = winPos.ControlClassName;

        var existing = Controls.FindMatch(attrs);
        ControlDefinition def;
        if (existing != null)
        {
            foreach (var kv in attrs) existing.Attributes[kv.Key] = kv.Value;
            def = existing;
        }
        else
        {
            string ctype = attrs.GetValueOrDefault(ControlDefinition.Keys.ControlType, "control");
            string name = Controls.GenerateDefaultName(ctype);
            def = new ControlDefinition { Name = name, Attributes = attrs };
            if (commitToRepository)
            {
                Controls.AddOrUpdate(def);
                if (CaptureOpenCvSnapshots && winPos.RootWindowHwnd != IntPtr.Zero)
                {
                    OpenCvAutomation.RecordControlSnapshot(Controls, def, winPos.RootWindowHwnd, el);
                }
                ControlScraped?.Invoke(def);
            }
        }

        var hover = new HoveredControlInfo(
            SuggestedName: def.Name,
            ControlType: def.Get(ControlDefinition.Keys.ControlType),
            AutomationId: def.Get(ControlDefinition.Keys.AutomationId),
            UiaName: def.Get(ControlDefinition.Keys.Name),
            WindowTitle: winPos.WindowTitle,
            ScreenPoint: new Point(screenX, screenY),
            WindowRelativePoint: new Point(winPos.WindowRelativePoint.X, winPos.WindowRelativePoint.Y),
            ControlRelativePoint: new Point(winPos.ControlRelativePoint.X, winPos.ControlRelativePoint.Y),
            ControlRelativeBounds: winPos.ControlRelativeBounds,
            RootWindowHwnd: winPos.RootWindowHwnd,
            ControlHwnd: winPos.ControlHwnd);

        return (def, hover, el);
    }

    /// <summary>Starts recording mouse clicks, window-to-control coordinates, and keyboard data input.</summary>
    public void StartRecording(string? testName = null, IntPtr targetWindowHwnd = default)
    {
        if (IsRecording) return;
        CurrentTest = new TestCase(string.IsNullOrWhiteSpace(testName) ? $"Recorded_{DateTime.Now:HHmmss}" : testName);
        if (targetWindowHwnd != IntPtr.Zero) TargetWindowHwnd = targetWindowHwnd;

        // Seed the 4 reserved framework header keywords (AUT, COMMAND, RA, KAO) as the first steps of the recorded test
        CurrentTest.EnsureReservedHeader(
            aut: ConfigManager.Current.DefaultTestBed ?? "calc.exe",
            commandList: "self_healing, highlight=\"1\", video=\"external store from config loc\"",
            reuseApp: "config",
            keepAppOpen: "config");

        activeTargetControl = null;
        activeTargetHwnd = IntPtr.Zero;
        activeTargetElement = null;
        lastObservedControlText = "";
        wasLeftDown = (Control.MouseButtons & MouseButtons.Left) != 0;
        wasRightDown = (Control.MouseButtons & MouseButtons.Right) != 0;

        IsRecording = true;
        pollTimer = new System.Threading.Timer(PollInputCallback, null, 40, 45);

        SystemLogger.User($"Started recording test '{CurrentTest.Name}' (Mouse Scrape + Window-to-Control Position + Keyboard Input).");
    }

    /// <summary>Flushes any pending keyboard input and stops recording.</summary>
    public TestCase StopRecording()
    {
        if (!IsRecording) return CurrentTest;
        IsRecording = false;
        pollTimer?.Dispose();
        pollTimer = null;

        lock (syncLock)
        {
            FlushTypingBuffer();
        }

        Controls.Save();
        SystemLogger.User($"Stopped recording test '{CurrentTest.Name}' ({CurrentTest.Steps.Count} steps).");
        return CurrentTest;
    }

    /// <summary>Allows manually appending a step (e.g., from the live Recording Bar or Step Editor).</summary>
    public TestStep AppendStep(string action, string target, string value, string description)
    {
        lock (syncLock)
        {
            var step = CurrentTest.AddStep(action, target, value, "", description);
            StepRecorded?.Invoke(step);
            return step;
        }
    }

    private void PollInputCallback(object? state)
    {
        if (!IsRecording) return;
        if (!Monitor.TryEnter(syncLock)) return;
        try
        {
            Point pt = Cursor.Position;
            IntPtr hwndUnderMouse = NativeMethods.WindowFromPoint(new NativeMethods.POINT(pt.X, pt.Y));
            IntPtr rootUnderMouse = hwndUnderMouse != IntPtr.Zero
                ? NativeMethods.GetAncestor(hwndUnderMouse, NativeMethods.GA_ROOT)
                : IntPtr.Zero;
            if (rootUnderMouse == IntPtr.Zero) rootUnderMouse = hwndUnderMouse;

            bool isOwnWindow = hwndUnderMouse != IntPtr.Zero &&
                               (NativeMethods.GetProcessId(hwndUnderMouse) == ownProcessId ||
                                IgnoredWindowHwnds.Contains(rootUnderMouse) ||
                                IgnoredWindowHwnds.Contains(hwndUnderMouse));

            // 1. Live Hover Preview (every >= 6px cursor move outside WinFrame's own UI)
            if (!isOwnWindow && (Math.Abs(pt.X - lastHoverPoint.X) >= 6 || Math.Abs(pt.Y - lastHoverPoint.Y) >= 6))
            {
                lastHoverPoint = pt;
                try
                {
                    var (_, hover, _) = ScrapeControlAtMouseAndWindowPosition(pt.X, pt.Y, commitToRepository: false);
                    HoverControlChanged?.Invoke(hover);
                }
                catch { }
            }

            // 2. Mouse Button Edge Detection (Left / Right / Double Click)
            bool leftDown = (Control.MouseButtons & MouseButtons.Left) != 0;
            bool rightDown = (Control.MouseButtons & MouseButtons.Right) != 0;

            if (!isOwnWindow)
            {
                if (leftDown && !wasLeftDown)
                {
                    FlushTypingBuffer();
                    bool isDouble = (DateTime.Now - lastLeftClickTime).TotalMilliseconds <= SystemInformation.DoubleClickTime &&
                                    Math.Abs(pt.X - lastLeftClickPoint.X) <= 5 &&
                                    Math.Abs(pt.Y - lastLeftClickPoint.Y) <= 5;
                    lastLeftClickTime = DateTime.Now;
                    lastLeftClickPoint = pt;

                    RecordMouseInteraction(isDouble ? "DOUBLE_CLICK" : "CLICK", pt);
                }
                else if (rightDown && !wasRightDown)
                {
                    FlushTypingBuffer();
                    RecordMouseInteraction("RIGHT_CLICK", pt);
                }
            }

            wasLeftDown = leftDown;
            wasRightDown = rightDown;

            // 3. UIA Focused Element & Text/Value Change Tracking (100% Antivirus-safe keyboard data capture)
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero && NativeMethods.GetProcessId(fg) != ownProcessId && !IgnoredWindowHwnds.Contains(fg))
            {
                CheckFocusedElementTextDelta();
            }
        }
        catch (Exception ex)
        {
            SystemLogger.Log($"Recorder poll error: {ex.Message}", SystemLogger.LogLevel.DEBUG);
        }
        finally
        {
            Monitor.Exit(syncLock);
        }
    }

    private void RecordMouseInteraction(string action, Point pt)
    {
        var (def, hover, el) = ScrapeControlAtMouseAndWindowPosition(pt.X, pt.Y, commitToRepository: true);
        activeTargetControl = "@" + def.Name;
        activeTargetHwnd = hover.ControlHwnd != IntPtr.Zero ? hover.ControlHwnd : hover.RootWindowHwnd;
        activeTargetElement = el;
        lastObservedControlText = ReadActiveControlText();

        string relCoords = $"{hover.WindowRelativePoint.X}|{hover.WindowRelativePoint.Y}";
        string desc = $"{action} on @{def.Name} ({def.Get(ControlDefinition.Keys.ControlType)} '{def.Get(ControlDefinition.Keys.Name)}') " +
                      $"[WinRel: {hover.WindowRelativePoint.X},{hover.WindowRelativePoint.Y} | CtrlRel: {hover.ControlRelativePoint.X},{hover.ControlRelativePoint.Y}]";

        AppendStep(action, "@" + def.Name, relCoords, desc);
    }

    private void CheckFocusedElementTextDelta()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused != null && focused.Current.ProcessId != ownProcessId)
            {
                if (activeTargetElement == null || !Automation.Compare(focused, activeTargetElement))
                {
                    FlushTypingBuffer();
                    var rect = focused.Current.BoundingRectangle;
                    if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
                    {
                        int cx = (int)(rect.Left + rect.Width / 2);
                        int cy = (int)(rect.Top + rect.Height / 2);
                        var (def, hover, el) = ScrapeControlAtMouseAndWindowPosition(cx, cy, commitToRepository: true);
                        activeTargetControl = "@" + def.Name;
                        activeTargetHwnd = hover.ControlHwnd != IntPtr.Zero ? hover.ControlHwnd : hover.RootWindowHwnd;
                        activeTargetElement = el ?? focused;
                        lastObservedControlText = ReadActiveControlText();
                    }
                }
            }
        }
        catch { }
    }

    private string ReadActiveControlText()
    {
        if (activeTargetElement != null)
        {
            string val = UiaAutomation.GetValue(activeTargetElement);
            if (!string.IsNullOrEmpty(val)) return val;
        }
        if (activeTargetHwnd != IntPtr.Zero)
        {
            return Win32Automation.GetControlText(activeTargetHwnd);
        }
        return "";
    }

    private void FlushTypingBuffer()
    {
        string currentText = ReadActiveControlText();
        if (!string.IsNullOrEmpty(currentText) &&
            !string.Equals(currentText, lastObservedControlText, StringComparison.Ordinal))
        {
            lastObservedControlText = currentText;
            AppendStep("TYPE", activeTargetControl ?? "", currentText, $"Entered keyboard text '{currentText}' into {activeTargetControl ?? "focused control"}");
        }
    }

    public void Dispose() => StopRecording();
}
