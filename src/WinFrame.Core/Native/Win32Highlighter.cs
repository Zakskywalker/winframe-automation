using System.Drawing;
using WinFrame.Core.Controls;
using WinFrame.Core.Keywords;
using WinFrame.Core.Logging;
using WinFrame.Core.Uia;
using WinFrame.Core.Vision;

namespace WinFrame.Core.Native;

/// <summary>
/// Win32 GDI Control Outline Highlighter.
/// Draws a colored border directly onto the desktop DC around one or more controls for a configurable
/// short interval using native <c>gdi32.dll</c> / <c>user32.dll</c> calls.
/// Controlled by the <c>highlight</c> variable in <see cref="Config.AppConfig"/> / <see cref="KeywordContext"/>
/// (<c>1</c> or <c>true</c> = ON; <c>0</c>, <c>""</c>, or <c>null</c> = OFF), and exposed as a special
/// keyword category (<c>HIGHLIGHT</c>, <c>WIN32_HIGHLIGHT</c>, <c>OUTLINE_CONTROL</c>) in keyword sheets.
/// </summary>
[KeywordLibrary("Highlight & Control Outline")]
public static class Win32Highlighter
{
    /// <summary>
    /// Converts a hex string (e.g. <c>#F59E0B</c>) or color name (e.g. <c>Red</c>, <c>Lime</c>, <c>Cyan</c>)
    /// to a Win32 GDI <c>COLORREF</c> (<c>0x00BBGGRR</c>).
    /// </summary>
    public static uint ParseColorRef(string? colorSpec)
    {
        Color c = Color.FromArgb(245, 158, 11); // Default amber/gold
        if (!string.IsNullOrWhiteSpace(colorSpec))
        {
            try
            {
                colorSpec = colorSpec.Trim();
                c = colorSpec.StartsWith('#')
                    ? ColorTranslator.FromHtml(colorSpec)
                    : Color.FromName(colorSpec);
                if (c.A == 0 && !colorSpec.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
                    c = ColorTranslator.FromHtml(colorSpec);
            }
            catch
            {
                c = Color.FromArgb(245, 158, 11);
            }
        }
        return (uint)(c.R | (c.G << 8) | (c.B << 16));
    }

    /// <summary>
    /// Draws a Win32 GDI hollow rectangle outline on the screen DC around <paramref name="bounds"/>
    /// using the specified color, thickness, and duration (interval in ms), then invalidates the desktop to erase it.
    /// </summary>
    public static void DrawOutline(Rectangle bounds, string? color = null, int durationMs = 280, int thickness = 3)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        uint colorRef = ParseColorRef(color);
        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return;

        IntPtr pen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, Math.Max(1, thickness), colorRef);
        IntPtr nullBrush = NativeMethods.GetStockObject(NativeMethods.NULL_BRUSH);

        IntPtr oldPen = NativeMethods.SelectObject(screenDc, pen);
        IntPtr oldBrush = NativeMethods.SelectObject(screenDc, nullBrush);

        try
        {
            NativeMethods.Rectangle(screenDc, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            if (durationMs > 0)
            {
                Thread.Sleep(durationMs);
            }
        }
        finally
        {
            NativeMethods.SelectObject(screenDc, oldPen);
            NativeMethods.SelectObject(screenDc, oldBrush);
            NativeMethods.DeleteObject(pen);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            // Trigger repaint so the transient outline is cleanly removed after the interval
            NativeMethods.InvalidateRect(IntPtr.Zero, IntPtr.Zero, true);
        }
    }

    /// <summary>Resolves the screen bounding rectangle for a control name, UIA locator, or HWND.</summary>
    public static Rectangle ResolveControlBounds(KeywordContext ctx, string controlNameOrTarget)
    {
        if (string.IsNullOrWhiteSpace(controlNameOrTarget)) return Rectangle.Empty;

        var el = ctx.ResolveElement(controlNameOrTarget, timeoutMs: 800);
        if (el != null)
        {
            var r = UiaAutomation.GetBounds(el);
            if (r.Width > 1 && r.Height > 1) return r;
        }

        var h = ctx.ResolveHwnd(controlNameOrTarget, timeoutMs: 600);
        if (h != IntPtr.Zero && NativeMethods.IsWindow(h))
        {
            var r = ScreenLocationManager.ToRectangle(ScreenLocationManager.GetAbsoluteControlBounds(h));
            if (r.Width > 1 && r.Height > 1) return r;
        }

        if (ctx.Controls.TryGet(controlNameOrTarget, out var def) &&
            def.Attributes.TryGetValue(ControlDefinition.Keys.BoundingRectangle, out var bStr))
        {
            return OpenCvAutomation.ParseRect(bStr);
        }

        return Rectangle.Empty;
    }

    public static bool IsNonControlAction(string action) =>
        action.ToUpperInvariant() is "SET_VAR" or "ASSERT_VAR" or "CHECK_VAR" or "LOG" or "WAIT" or "SLEEP"
            or "HIGHLIGHT" or "WIN32_HIGHLIGHT" or "OUTLINE_CONTROL" or "HIGHLIGHT_CONTROLS"
            or "CV_SNAPSHOT_SCREEN" or "SNAPSHOT_SCREEN" or "SCREENSHOT";

    /// <summary>
    /// Automatically called before each step when <see cref="KeywordContext.IsHighlightEnabled"/> is true
    /// (<c>highlight = 1</c> or <c>true</c>).
    /// </summary>
    public static void HighlightTargetIfEnabled(KeywordContext ctx, string? target)
    {
        if (!ctx.IsHighlightEnabled || string.IsNullOrWhiteSpace(target)) return;
        try
        {
            var bounds = ResolveControlBounds(ctx, target);
            if (bounds.Width > 1 && bounds.Height > 1)
            {
                DrawOutline(bounds, ctx.HighlightColor, ctx.HighlightIntervalMs, ctx.Config.HighlightThickness);
            }
        }
        catch { }
    }

    // --------------------------------------------------------------------------------------------
    // Special Keyword Commands for Highlighting Controls by Name in Keyword Sheets
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Special keyword command that highlights one or more controls by name (comma/semicolon separated in Target)
    /// when the <c>highlight</c> variable is set to <c>1</c> or <c>true</c>.
    /// Value format: <c>[color]|[intervalMs]|[thickness]</c> (all optional; defaults come from config).
    /// </summary>
    [Keyword("HIGHLIGHT", Description = "Outlines Target control name(s) using Win32 GDI drawing when highlight=1/true. Value: color|intervalMs|thickness")]
    [Keyword("WIN32_HIGHLIGHT")]
    [Keyword("OUTLINE_CONTROL")]
    [Keyword("HIGHLIGHT_CONTROLS")]
    public static bool KeywordHighlight(
        KeywordContext ctx,
        string target,
        string color = "",
        int intervalMs = 0,
        int thickness = 0)
    {
        if (!ctx.IsHighlightEnabled)
        {
            SystemLogger.Test($"[HIGHLIGHT] Skipped for '{target}' because highlight variable is off ('{(ctx.Variables.GetValueOrDefault("highlight") ?? "null")}').");
            return true;
        }

        string effectiveColor = string.IsNullOrWhiteSpace(color) ? ctx.HighlightColor : color;
        int effectiveInterval = intervalMs > 0 ? intervalMs : ctx.HighlightIntervalMs;
        int effectiveThickness = thickness > 0 ? thickness : ctx.Config.HighlightThickness;

        var names = target.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var name in names)
        {
            var bounds = ResolveControlBounds(ctx, name);
            if (bounds.Width > 1 && bounds.Height > 1)
            {
                SystemLogger.Test($"[HIGHLIGHT] Drawing Win32 outline on '{name}' at {bounds} (color={effectiveColor}, interval={effectiveInterval}ms)");
                DrawOutline(bounds, effectiveColor, effectiveInterval, effectiveThickness);
            }
            else
            {
                SystemLogger.Test($"[HIGHLIGHT] Could not resolve bounds for control '{name}'.", SystemLogger.LogLevel.WARNING);
            }
        }
        return true;
    }

    /// <summary>
    /// Generates a list of ready-to-insert <c>HIGHLIGHT</c> <see cref="TestStep"/>s for every control currently
    /// in the <see cref="ControlRepository"/> so they can be added to a keyword sheet or control document.
    /// </summary>
    public static List<TestStep> CreateHighlightStepsForControls(ControlRepository repo, string? color = null, int? intervalMs = null)
    {
        var steps = new List<TestStep>();
        string val = string.IsNullOrWhiteSpace(color) && intervalMs == null
            ? ""
            : $"{color ?? ""}|{(intervalMs?.ToString() ?? "")}";

        foreach (var c in repo.All.OrderBy(c => c.Name))
        {
            steps.Add(new TestStep
            {
                Index = steps.Count + 1,
                Action = "HIGHLIGHT",
                Target = "@" + c.Name,
                Value = val,
                Description = $"Win32 GDI outline highlight for {c.Name} (runs when highlight=1/true)"
            });
        }
        return steps;
    }
}

