public class Win32Automation
{
    /// <summary>
    /// Harvests all child controls on a screen by drilling the window tree.
    /// </summary>
    public static List<IntPtr> HarvestControls(IntPtr parentHwnd)
    {
        List<IntPtr> childWindows = new List<IntPtr>();
        SystemLogger.Log($"Drilling control tree for Parent HWND: {parentHwnd}");

        NativeMethods.EnumChildWindows(parentHwnd, (hwnd, lParam) =>
        {
            childWindows.Add(hwnd);
            return true; // Continue enumeration
        }, IntPtr.Zero);

        SystemLogger.Log($"Harvested {childWindows.Count} controls.");
        return childWindows;
    }

    /// <summary>
    /// Verifies the text or data value of a specific control.
    /// </summary>
    public static bool VerifyDataValue(IntPtr hwnd, string expectedValue)
    {
        int length = (int)NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETTEXTLENGTH, 0, null);
        StringBuilder sb = new StringBuilder(length + 1);
        NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETTEXT, sb.Capacity, sb);
        
        string actualValue = sb.ToString();
        bool match = actualValue.Equals(expectedValue, StringComparison.OrdinalIgnoreCase);
        
        SystemLogger.Log($"VerifyDataValue HWND {hwnd} | Expected: '{expectedValue}' | Actual: '{actualValue}' | Match: {match}", 
            match ? SystemLogger.LogLevel.INFO : SystemLogger.LogLevel.ERROR);
        
        return match;
    }

    /// <summary>
    /// Executes native input actions (mouse movement, clicking, and keyboard typing).
    /// </summary>
    public static void SendNativeInput(IntPtr hwnd, string action, string value = "")
    {
        NativeMethods.SetForegroundWindow(hwnd);
        Thread.Sleep(100); // Allow window to gain focus

        switch (action.ToUpper())
        {
            case "CLICK":
                var center = ScreenLocationManager.GetControlCenterPoint(hwnd);
                SystemLogger.Log($"Executing Native CLICK at X:{center.X}, Y:{center.Y}");
                
                NativeMethods.SetCursorPos(center.X, center.Y);
                Thread.Sleep(50); // Settle cursor

                NativeMethods.INPUT[] inputs = new NativeMethods.INPUT[2];
                inputs[0].type = NativeMethods.INPUT_MOUSE;
                inputs[0].U.mi.dwFlags = NativeMethods.MOUSEEVENTF_LEFTDOWN;
                
                inputs[1].type = NativeMethods.INPUT_MOUSE;
                inputs[1].U.mi.dwFlags = NativeMethods.MOUSEEVENTF_LEFTUP;

                NativeMethods.SendInput((uint)inputs.Length, inputs, NativeMethods.INPUT.Size);
                break;

            case "TYPE":
                SystemLogger.Log($"Executing Native TYPE string: '{value}'");
                List<NativeMethods.INPUT> keyInputs = new List<NativeMethods.INPUT>();

                foreach (char c in value)
                {
                    // Key Down
                    NativeMethods.INPUT keyDown = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
                    keyDown.U.ki.wScan = c;
                    keyDown.U.ki.dwFlags = NativeMethods.KEYEVENTF_UNICODE;
                    keyInputs.Add(keyDown);

                    // Key Up
                    NativeMethods.INPUT keyUp = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
                    keyUp.U.ki.wScan = c;
                    keyUp.U.ki.dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP;
                    keyInputs.Add(keyUp);
                }

                NativeMethods.SendInput((uint)keyInputs.Count, keyInputs.ToArray(), NativeMethods.INPUT.Size);
                break;

            default:
                SystemLogger.Log($"Unknown native input action: {action}", SystemLogger.LogLevel.WARNING);
                break;
        }
    }
}