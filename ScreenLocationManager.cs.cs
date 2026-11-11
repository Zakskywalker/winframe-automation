public class ScreenLocationManager
{
    /// <summary>
    /// Converts a control's relative client coordinates to absolute screen coordinates.
    /// Handles negative coordinate spaces across multi-monitor setups natively via Win32.
    /// </summary>
    public static NativeMethods.POINT ConvertRelativeToAbsolute(IntPtr parentHwnd, int relativeX, int relativeY)
    {
        NativeMethods.POINT pt = new NativeMethods.POINT(relativeX, relativeY);
        if (!NativeMethods.ClientToScreen(parentHwnd, ref pt))
        {
            SystemLogger.Log($"Failed to convert relative coordinates ({relativeX},{relativeY}) for HWND {parentHwnd}", SystemLogger.LogLevel.ERROR);
        }
        else
        {
            SystemLogger.Log($"Interpolated Relative ({relativeX},{relativeY}) to Absolute ({pt.X},{pt.Y})", SystemLogger.LogLevel.DEBUG);
        }
        return pt;
    }

    /// <summary>
    /// Retrieves the absolute bounding rectangle of a control for interaction mapping.
    /// </summary>
    public static NativeMethods.RECT GetAbsoluteControlBounds(IntPtr hwnd)
    {
        NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect);
        SystemLogger.Log($"Control HWND {hwnd} absolute bounds: L:{rect.Left} T:{rect.Top} W:{rect.Width} H:{rect.Height}", SystemLogger.LogLevel.DEBUG);
        return rect;
    }

    /// <summary>
    /// Calculates the absolute center point of a control for native mouse clicks.
    /// </summary>
    public static NativeMethods.POINT GetControlCenterPoint(IntPtr hwnd)
    {
        var rect = GetAbsoluteControlBounds(hwnd);
        return new NativeMethods.POINT(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2));
    }
}