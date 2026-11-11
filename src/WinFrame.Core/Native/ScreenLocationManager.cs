using System.Drawing;
using System.Windows.Forms;
using WinFrame.Core.Logging;

namespace WinFrame.Core.Native;

/// <summary>
/// Accurate Screen Location &amp; Coordinate Manager.
/// This class manages the interpolation of relative control bounds to absolute screen coordinates across multiple monitors.
/// </summary>
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

    /// <summary>Converts absolute screen coordinates back to a window's client coordinates.</summary>
    public static NativeMethods.POINT ConvertAbsoluteToRelative(IntPtr parentHwnd, int absoluteX, int absoluteY)
    {
        var pt = new NativeMethods.POINT(absoluteX, absoluteY);
        NativeMethods.ScreenToClient(parentHwnd, ref pt);
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

    /// <summary>
    /// Interpolates a point expressed relative to a specific monitor (index 0 = primary, 1 = second, ...)
    /// into absolute virtual-desktop coordinates. Supports one or two (or more) screens, including
    /// monitors positioned left of / above the primary (negative coordinates).
    /// </summary>
    public static NativeMethods.POINT ConvertScreenRelativeToAbsolute(int screenIndex, int x, int y)
    {
        var screens = GetScreensOrdered();
        if (screenIndex < 0 || screenIndex >= screens.Count)
        {
            SystemLogger.Log($"Screen index {screenIndex} out of range (have {screens.Count}); using primary.", SystemLogger.LogLevel.WARNING);
            screenIndex = 0;
        }
        var b = screens[screenIndex].Bounds;
        return new NativeMethods.POINT(b.Left + x, b.Top + y);
    }

    /// <summary>
    /// Combines a harvested control's relative location (inside its parent window) with the parent's
    /// HWND to get the absolute center of the control, regardless of which monitor the window is on.
    /// </summary>
    public static NativeMethods.POINT InterpolateControlCenter(IntPtr parentHwnd, Rectangle relativeControlBounds)
    {
        return ConvertRelativeToAbsolute(parentHwnd,
            relativeControlBounds.Left + relativeControlBounds.Width / 2,
            relativeControlBounds.Top + relativeControlBounds.Height / 2);
    }

    /// <summary>Screens with the primary first, then left-to-right.</summary>
    public static List<Screen> GetScreensOrdered() =>
        Screen.AllScreens.OrderByDescending(s => s.Primary).ThenBy(s => s.Bounds.Left).ThenBy(s => s.Bounds.Top).ToList();

    /// <summary>Bounds of the entire virtual desktop (all monitors).</summary>
    public static Rectangle GetVirtualScreenBounds() => SystemInformation.VirtualScreen;

    /// <summary>Index (per <see cref="GetScreensOrdered"/>) of the monitor containing a point.</summary>
    public static int GetScreenIndexForPoint(int x, int y)
    {
        var screens = GetScreensOrdered();
        var s = Screen.FromPoint(new Point(x, y));
        return screens.FindIndex(t => t.DeviceName == s.DeviceName);
    }

    /// <summary>Converts a Win32 RECT to a System.Drawing rectangle.</summary>
    public static Rectangle ToRectangle(NativeMethods.RECT r) => new(r.Left, r.Top, r.Width, r.Height);
}

