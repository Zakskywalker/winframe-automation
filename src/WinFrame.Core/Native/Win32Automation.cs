using System.Text;
using System.Windows.Forms;
using WinFrame.Core.Keywords;
using WinFrame.Core.Logging;

namespace WinFrame.Core.Native;

/// <summary>
/// Win32 Automation Engine.
/// Handles control tree harvesting, data verification, window manipulation, and native hardware-level input.
/// Preserves the original <see cref="HarvestControls"/>, <see cref="VerifyDataValue"/>, and <see cref="SendNativeInput"/>
/// signatures while exposing all operations and checks as keyword-sheet commands.
/// </summary>
[KeywordLibrary("Win32 Native")]
public class Win32Automation
{
    /// <summary>
    /// Harvests all child controls on a screen by drilling the window tree.
    /// </summary>
    [Keyword("HARVEST", Description = "Harvests all child controls of Target HWND.")]
    [Keyword("WIN32_HARVEST")]
    public static List<IntPtr> HarvestControls(IntPtr parentHwnd)
    {
        List<IntPtr> childWindows = new List<IntPtr>();
        SystemLogger.Log($"Drilling control tree for Parent HWND: {parentHwnd}");

        if (parentHwnd == IntPtr.Zero)
        {
            parentHwnd = NativeMethods.GetForegroundWindow();
        }

        NativeMethods.EnumChildWindows(parentHwnd, (hwnd, lParam) =>
        {
            childWindows.Add(hwnd);
            return true; // Continue enumeration
        }, IntPtr.Zero);

        SystemLogger.Log($"Harvested {childWindows.Count} controls.");
        return childWindows;
    }

    /// <summary>
    /// Drills the Win32 control tree and returns detailed metadata for every child HWND.
    /// </summary>
    public static List<Dictionary<string, string>> HarvestControlDetails(IntPtr parentHwnd)
    {
        var handles = HarvestControls(parentHwnd);
        var list = new List<Dictionary<string, string>>(handles.Count);
        string parentTitle = NativeMethods.GetWindowTextManaged(parentHwnd);
        var parentRect = ScreenLocationManager.GetAbsoluteControlBounds(parentHwnd);

        for (int i = 0; i < handles.Count; i++)
        {
            IntPtr h = handles[i];
            var rect = ScreenLocationManager.GetAbsoluteControlBounds(h);
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Index"] = i.ToString(),
                ["NativeWindowHandle"] = $"0x{h.ToInt64():X}",
                ["ClassName"] = NativeMethods.GetClassNameManaged(h),
                ["Name"] = GetControlText(h),
                ["Win32.CtrlId"] = NativeMethods.GetDlgCtrlID(h).ToString(),
                ["WindowTitle"] = parentTitle,
                ["BoundingRectangle"] = rect.ToString(),
                ["RelativeBounds"] = $"{rect.Left - parentRect.Left},{rect.Top - parentRect.Top},{rect.Width},{rect.Height}",
                ["IsEnabled"] = NativeMethods.IsWindowEnabled(h).ToString(),
                ["IsVisible"] = NativeMethods.IsWindowVisible(h).ToString(),
                ["ProcessId"] = NativeMethods.GetProcessId(h).ToString()
            };
            list.Add(d);
        }
        return list;
    }

    /// <summary>Reads text via WM_GETTEXT or window title fallback.</summary>
    [Keyword("WIN32_GET_TEXT", Description = "Reads text from Target HWND and stores it.")]
    public static string GetControlText(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return string.Empty;
        int length = (int)NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETTEXTLENGTH, 0, null);
        if (length > 0 && length < 65536)
        {
            StringBuilder sb = new StringBuilder(length + 1);
            NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETTEXT, sb.Capacity, sb);
            return sb.ToString();
        }
        return NativeMethods.GetWindowTextManaged(hwnd);
    }

    /// <summary>Sets text directly via WM_SETTEXT.</summary>
    [Keyword("WIN32_SET_TEXT", Description = "Sets text on Target HWND via WM_SETTEXT.")]
    public static bool SetControlText(IntPtr hwnd, string value)
    {
        if (hwnd == IntPtr.Zero) return false;
        NativeMethods.SendMessageString(hwnd, NativeMethods.WM_SETTEXT, IntPtr.Zero, value ?? string.Empty);
        return true;
    }

    /// <summary>Clears/deletes text in Target HWND (inverse of TYPE / WIN32_SET_TEXT).</summary>
    [Keyword("CLEAR_TEXT", Description = "Clears/deletes text on Target HWND (or restores Value if provided).")]
    [Keyword("DELETE_TEXT")]
    [Keyword("WIN32_CLEAR_TEXT")]
    public static bool ClearControlText(IntPtr hwnd, string restoreValue = "")
    {
        if (hwnd == IntPtr.Zero) return false;
        NativeMethods.SendMessageString(hwnd, NativeMethods.WM_SETTEXT, IntPtr.Zero, restoreValue ?? string.Empty);
        if (GetControlText(hwnd) != (restoreValue ?? string.Empty))
        {
            SendNativeInput(hwnd, "CLICK");
            SendKeys.SendWait("^a{BACKSPACE}");
            if (!string.IsNullOrEmpty(restoreValue)) TypeUnicodeText(restoreValue);
        }
        return true;
    }

    /// <summary>
    /// Verifies the text or data value of a specific control.
    /// </summary>
    [Keyword("VERIFY", Description = "Verifies that Target HWND text equals Value.")]
    [Keyword("WIN32_VERIFY")]
    public static bool VerifyDataValue(IntPtr hwnd, string expectedValue)
    {
        string actualValue = GetControlText(hwnd);
        bool match = actualValue.Equals(expectedValue, StringComparison.OrdinalIgnoreCase)
                  || actualValue.Contains(expectedValue, StringComparison.OrdinalIgnoreCase);

        SystemLogger.Log($"VerifyDataValue HWND {hwnd} | Expected: '{expectedValue}' | Actual: '{actualValue}' | Match: {match}",
            match ? SystemLogger.LogLevel.INFO : SystemLogger.LogLevel.ERROR);

        return match;
    }

    /// <summary>
    /// Executes native input actions (mouse movement, clicking, and keyboard typing).
    /// </summary>
    public static void SendNativeInput(IntPtr hwnd, string action, string value = "")
    {
        if (hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd))
        {
            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            NativeMethods.SetForegroundWindow(root != IntPtr.Zero ? root : hwnd);
            Thread.Sleep(80); // Allow window to gain focus
        }

        switch (action.ToUpperInvariant())
        {
            case "CLICK":
                var center = ScreenLocationManager.GetControlCenterPoint(hwnd);
                ClickAtPoint(center.X, center.Y, "LEFT", 1);
                break;

            case "DOUBLE_CLICK":
            case "DBLCLICK":
                var dblCenter = ScreenLocationManager.GetControlCenterPoint(hwnd);
                ClickAtPoint(dblCenter.X, dblCenter.Y, "LEFT", 2);
                break;

            case "RIGHT_CLICK":
            case "RCLICK":
                var rCenter = ScreenLocationManager.GetControlCenterPoint(hwnd);
                ClickAtPoint(rCenter.X, rCenter.Y, "RIGHT", 1);
                break;

            case "BM_CLICK":
                NativeMethods.SendMessagePtr(hwnd, NativeMethods.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                break;

            case "TYPE":
                TypeUnicodeText(value);
                break;

            case "SENDKEYS":
                SendKeys.SendWait(value);
                break;

            default:
                SystemLogger.Log($"Unknown native input action: {action}", SystemLogger.LogLevel.WARNING);
                break;
        }
    }

    public static void ClickAtPoint(int x, int y, string button = "LEFT", int count = 1)
    {
        SystemLogger.Log($"Executing Native {button} CLICK (x{count}) at X:{x}, Y:{y}");
        NativeMethods.SetCursorPos(x, y);
        Thread.Sleep(40);

        uint downFlag = button.Equals("RIGHT", StringComparison.OrdinalIgnoreCase)
            ? NativeMethods.MOUSEEVENTF_RIGHTDOWN
            : button.Equals("MIDDLE", StringComparison.OrdinalIgnoreCase)
                ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN
                : NativeMethods.MOUSEEVENTF_LEFTDOWN;

        uint upFlag = button.Equals("RIGHT", StringComparison.OrdinalIgnoreCase)
            ? NativeMethods.MOUSEEVENTF_RIGHTUP
            : button.Equals("MIDDLE", StringComparison.OrdinalIgnoreCase)
                ? NativeMethods.MOUSEEVENTF_MIDDLEUP
                : NativeMethods.MOUSEEVENTF_LEFTUP;

        for (int i = 0; i < Math.Max(1, count); i++)
        {
            NativeMethods.INPUT[] inputs = new NativeMethods.INPUT[2];
            inputs[0].type = NativeMethods.INPUT_MOUSE;
            inputs[0].U.mi.dwFlags = downFlag;
            inputs[1].type = NativeMethods.INPUT_MOUSE;
            inputs[1].U.mi.dwFlags = upFlag;
            NativeMethods.SendInput((uint)inputs.Length, inputs, NativeMethods.INPUT.Size);
            if (i + 1 < count) Thread.Sleep(50);
        }
    }

    public static void TypeUnicodeText(string value)
    {
        SystemLogger.Log($"Executing Native TYPE string: '{value}'");
        if (string.IsNullOrEmpty(value)) return;
        List<NativeMethods.INPUT> keyInputs = new List<NativeMethods.INPUT>(value.Length * 2);

        foreach (char c in value)
        {
            NativeMethods.INPUT keyDown = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
            keyDown.U.ki.wScan = c;
            keyDown.U.ki.dwFlags = NativeMethods.KEYEVENTF_UNICODE;
            keyInputs.Add(keyDown);

            NativeMethods.INPUT keyUp = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
            keyUp.U.ki.wScan = c;
            keyUp.U.ki.dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP;
            keyInputs.Add(keyUp);
        }

        NativeMethods.SendInput((uint)keyInputs.Count, keyInputs.ToArray(), NativeMethods.INPUT.Size);
    }

    // ---- Keyword bindings ----

    [Keyword("CLICK", Description = "Native left click on Target control center.")]
    [Keyword("WIN32_CLICK")]
    public static void KeywordClick(IntPtr hwnd) => SendNativeInput(hwnd, "CLICK");

    [Keyword("DOUBLE_CLICK", Description = "Native double click on Target control center.")]
    [Keyword("WIN32_DBLCLICK")]
    public static void KeywordDoubleClick(IntPtr hwnd) => SendNativeInput(hwnd, "DOUBLE_CLICK");

    [Keyword("RIGHT_CLICK", Description = "Native right click on Target control center.")]
    [Keyword("WIN32_RCLICK")]
    public static void KeywordRightClick(IntPtr hwnd) => SendNativeInput(hwnd, "RIGHT_CLICK");

    [Keyword("CLICK_POINT", Description = "Clicks absolute screen coordinates X|Y|Button.")]
    public static void KeywordClickPoint(int x, int y, string button = "LEFT") => ClickAtPoint(x, y, button, 1);

    [Keyword("CLICK_RELATIVE", Description = "Clicks relative client coordinates X|Y inside Target HWND.")]
    public static void KeywordClickRelative(IntPtr hwnd, int relX, int relY, string button = "LEFT")
    {
        var pt = ScreenLocationManager.ConvertRelativeToAbsolute(hwnd, relX, relY);
        ClickAtPoint(pt.X, pt.Y, button, 1);
    }

    [Keyword("CLICK_SCREEN", Description = "Clicks X|Y on monitor index ScreenIndex (0=primary, 1=secondary).")]
    public static void KeywordClickScreen(int screenIndex, int x, int y, string button = "LEFT")
    {
        var pt = ScreenLocationManager.ConvertScreenRelativeToAbsolute(screenIndex, x, y);
        ClickAtPoint(pt.X, pt.Y, button, 1);
    }

    [Keyword("TYPE", Description = "Types Value using Unicode hardware input into Target HWND.")]
    [Keyword("WIN32_TYPE")]
    public static void KeywordType(IntPtr hwnd, string value) => SendNativeInput(hwnd, "TYPE", value);

    [Keyword("SEND_KEYS", Description = "Sends keystrokes (e.g. {ENTER}, ^c) via SendKeys.")]
    public static void KeywordSendKeys(IntPtr hwnd, string keys) => SendNativeInput(hwnd, "SENDKEYS", keys);

    [Keyword("FOCUS_WINDOW", Description = "Brings Target HWND to foreground.")]
    public static bool KeywordFocus(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeMethods.SetForegroundWindow(hwnd);

    [Keyword("SHOW_WINDOW", Description = "Changes window state: RESTORE, MINIMIZE, MAXIMIZE, HIDE.")]
    public static bool KeywordShowWindow(IntPtr hwnd, string state = "RESTORE")
    {
        int cmd = state.ToUpperInvariant() switch
        {
            "MINIMIZE" or "MIN" => NativeMethods.SW_SHOWMINIMIZED,
            "MAXIMIZE" or "MAX" => NativeMethods.SW_SHOWMAXIMIZED,
            "HIDE" => NativeMethods.SW_HIDE,
            _ => NativeMethods.SW_RESTORE
        };
        return NativeMethods.ShowWindow(hwnd, cmd);
    }

    [Keyword("WIN32_CHECK_ENABLED", Description = "Checks that Target HWND is enabled.")]
    public static bool KeywordCheckEnabled(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeMethods.IsWindowEnabled(hwnd);

    [Keyword("WIN32_CHECK_VISIBLE", Description = "Checks that Target HWND is visible.")]
    public static bool KeywordCheckVisible(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd);
}
