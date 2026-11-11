using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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
/// Live Test Recorder &amp; Control Scraper ("Create Tests" pipeline).
/// <list type="bullet">
/// <item>Scrapes a target window's controls into a mock table for preview/renaming on the accessory panel,
/// defaulting to <c>ctrltype_x</c> (<c>button_1</c>, <c>edit_2</c>, ...) when the user skips manual naming.</item>
/// <item>Hooks global mouse (<c>WH_MOUSE_LL</c>) and keyboard (<c>WH_KEYBOARD_LL</c>) input to record clicks
/// and keyboard entry into fields as <see cref="TestStep"/>s.</item>
/// <item>Optionally captures OpenCV whole-window and individual-control snapshots for each recorded interaction.</item>
/// </list>
/// </summary>
public sealed class TestRecorder : IDisposable
{
    private IntPtr mouseHook = IntPtr.Zero;
    private IntPtr keyboardHook = IntPtr.Zero;
    private NativeMethods.LowLevelProc? mouseProc;
    private NativeMethods.LowLevelProc? keyboardProc;

    private readonly StringBuilder typingBuffer = new();
    private string? typingTarget;
    private DateTime lastClickTime = DateTime.MinValue;
    private NativeMethods.POINT lastClickPoint;
    private readonly int ownProcessId = Environment.ProcessId;

    public ControlRepository Controls { get; }
    public TestCase CurrentTest { get; private set; }
    public IntPtr TargetWindowHwnd { get; set; }
    public bool CaptureOpenCvSnapshots { get; set; } = true;
    public bool IsRecording { get; private set; }

    /// <summary>Raised whenever a new <see cref="TestStep"/> is recorded.</summary>
    public event Action<TestStep>? StepRecorded;

    /// <summary>Raised whenever a control is discovered or updated during scraping/recording.</summary>
    public event Action<ControlDefinition>? ControlScraped;

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

    /// <summary>Starts global mouse and keyboard recording hooks.</summary>
    public void StartRecording(string? testName = null, IntPtr targetWindowHwnd = default)
    {
        if (IsRecording) return;
        if (!string.IsNullOrWhiteSpace(testName)) CurrentTest = new TestCase(testName);
        if (targetWindowHwnd != IntPtr.Zero) TargetWindowHwnd = targetWindowHwnd;

        mouseProc = MouseHookCallback;
        keyboardProc = KeyboardHookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        IntPtr hMod = NativeMethods.GetModuleHandle(curModule.ModuleName);

        mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, mouseProc, hMod, 0);
        keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, keyboardProc, hMod, 0);
        IsRecording = true;

        SystemLogger.User($"Started recording test '{CurrentTest.Name}'.");
    }

    /// <summary>Flushes any pending keyboard buffer and unhooks mouse/keyboard listeners.</summary>
    public TestCase StopRecording()
    {
        if (!IsRecording) return CurrentTest;
        FlushTypingBuffer();

        if (mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        if (keyboardHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
        IsRecording = false;

        Controls.Save();
        SystemLogger.User($"Stopped recording test '{CurrentTest.Name}' ({CurrentTest.Steps.Count} steps).");
        return CurrentTest;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            uint msg = (uint)wParam.ToInt64();
            if (msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var pt = hookStruct.pt;

                IntPtr hwndAtPt = NativeMethods.WindowFromPoint(pt);
                if (hwndAtPt != IntPtr.Zero && NativeMethods.GetProcessId(hwndAtPt) != ownProcessId)
                {
                    IntPtr rootHwnd = NativeMethods.GetAncestor(hwndAtPt, NativeMethods.GA_ROOT);
                    if (TargetWindowHwnd == IntPtr.Zero || rootHwnd == TargetWindowHwnd || hwndAtPt == TargetWindowHwnd)
                    {
                        FlushTypingBuffer();
                        bool isDouble = msg == NativeMethods.WM_LBUTTONDOWN &&
                                        (DateTime.Now - lastClickTime).TotalMilliseconds <= SystemInformation.DoubleClickTime &&
                                        Math.Abs(pt.X - lastClickPoint.X) <= 4 &&
                                        Math.Abs(pt.Y - lastClickPoint.Y) <= 4;

                        lastClickTime = DateTime.Now;
                        lastClickPoint = pt;

                        string action = msg == NativeMethods.WM_RBUTTONDOWN ? "RIGHT_CLICK" : isDouble ? "DOUBLE_CLICK" : "CLICK";
                        Task.Run(() => RecordMouseAction(action, pt, rootHwnd != IntPtr.Zero ? rootHwnd : hwndAtPt));
                    }
                }
            }
        }
        return NativeMethods.CallNextHookEx(mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && ((uint)wParam.ToInt64() == NativeMethods.WM_KEYDOWN))
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero && NativeMethods.GetProcessId(fg) != ownProcessId)
            {
                if (TargetWindowHwnd == IntPtr.Zero || fg == TargetWindowHwnd)
                {
                    var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                    var key = (Keys)kb.vkCode;

                    if (key == Keys.Return)
                    {
                        FlushTypingBuffer();
                        AppendStep("SEND_KEYS", typingTarget ?? "", "{ENTER}", "Enter key");
                    }
                    else if (key == Keys.Tab)
                    {
                        FlushTypingBuffer();
                        AppendStep("SEND_KEYS", typingTarget ?? "", "{TAB}", "Tab key");
                    }
                    else if (key == Keys.Back && typingBuffer.Length > 0)
                    {
                        typingBuffer.Length--;
                    }
                    else
                    {
                        string ch = VkToUnicode(kb.vkCode, kb.scanCode);
                        if (!string.IsNullOrEmpty(ch) && !char.IsControl(ch[0]))
                        {
                            typingBuffer.Append(ch);
                        }
                    }
                }
            }
        }
        return NativeMethods.CallNextHookEx(keyboardHook, nCode, wParam, lParam);
    }

    private void RecordMouseAction(string action, NativeMethods.POINT pt, IntPtr rootHwnd)
    {
        try
        {
            var el = UiaAutomation.ElementFromPoint(pt.X, pt.Y);
            string controlName;
            if (el != null)
            {
                var attrs = UiaAutomation.ExtractAttributes(el, windowTitle: NativeMethods.GetWindowTextManaged(rootHwnd));
                var existing = Controls.FindMatch(attrs);
                if (existing != null)
                {
                    controlName = existing.Name;
                }
                else
                {
                    string ctype = attrs.GetValueOrDefault(ControlDefinition.Keys.ControlType, "control");
                    controlName = Controls.GenerateDefaultName(ctype);
                    var def = new ControlDefinition { Name = controlName, Attributes = attrs };
                    Controls.AddOrUpdate(def);
                    if (CaptureOpenCvSnapshots)
                    {
                        OpenCvAutomation.RecordControlSnapshot(Controls, def, rootHwnd, el);
                    }
                    ControlScraped?.Invoke(def);
                }
                typingTarget = "@" + controlName;
                AppendStep(action, "@" + controlName, "", $"Recorded {action} at ({pt.X},{pt.Y})");
            }
            else
            {
                AppendStep("CLICK_POINT", "", $"{pt.X}|{pt.Y}|{(action == "RIGHT_CLICK" ? "RIGHT" : "LEFT")}", $"Coordinate click at ({pt.X},{pt.Y})");
            }
        }
        catch (Exception ex)
        {
            SystemLogger.Log($"Recorder mouse handler error: {ex.Message}", SystemLogger.LogLevel.DEBUG);
        }
    }

    private void FlushTypingBuffer()
    {
        if (typingBuffer.Length == 0) return;
        string text = typingBuffer.ToString();
        typingBuffer.Clear();
        AppendStep("TYPE", typingTarget ?? "", text, $"Typed '{text}'");
    }

    private void AppendStep(string action, string target, string value, string description)
    {
        var step = CurrentTest.AddStep(action, target, value, "", description);
        StepRecorded?.Invoke(step);
    }

    private static string VkToUnicode(uint vk, uint scan)
    {
        byte[] state = new byte[256];
        NativeMethods.GetKeyboardState(state);
        var sb = new StringBuilder(8);
        int rc = NativeMethods.ToUnicode(vk, scan, state, sb, sb.Capacity, 0);
        return rc > 0 ? sb.ToString(0, rc) : "";
    }

    public void Dispose() => StopRecording();
}

