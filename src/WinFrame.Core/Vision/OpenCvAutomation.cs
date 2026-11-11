using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Automation;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using WinFrame.Core.Config;
using WinFrame.Core.Controls;
using WinFrame.Core.Keywords;
using WinFrame.Core.Logging;
using WinFrame.Core.Native;
using WinFrame.Core.Uia;

namespace WinFrame.Core.Vision;

/// <summary>Result of an OpenCvSharp template match or image comparison.</summary>
public sealed record VisionMatchResult(
    bool Found,
    double Confidence,
    Rectangle Bounds,
    System.Drawing.Point Center)
{
    public static readonly VisionMatchResult Empty = new(false, 0, Rectangle.Empty, System.Drawing.Point.Empty);
}

/// <summary>
/// OpenCvSharp Vision &amp; Snapshot Engine.
/// Provides Windows Snipping-Tool style captures (full virtual desktop, specific monitor, application window,
/// individual control by HWND/UIA/ControlRepository, or arbitrary region), template matching, visual diffing,
/// image-driven clicks/waits, and automatic or second-pass recording of OpenCV snapshots into the control sheet.
/// All methods are exposed as keyword-sheet commands via <see cref="KeywordAttribute"/>.
/// </summary>
[KeywordLibrary("OpenCV Vision & Snapshots")]
public static class OpenCvAutomation
{
    // --------------------------------------------------------------------------------------------
    // Screen, Window, Region & Control Snapshots (Snipping Tool style)
    // --------------------------------------------------------------------------------------------

    /// <summary>Captures the entire virtual desktop across all monitors as a PNG byte array.</summary>
    public static byte[] CaptureFullScreenPng() => CaptureRegionPng(ScreenLocationManager.GetVirtualScreenBounds());

    /// <summary>Captures a specific monitor (0 = primary, 1 = second, ...) as a PNG byte array.</summary>
    public static byte[] CaptureMonitorPng(int screenIndex = 0)
    {
        var screens = ScreenLocationManager.GetScreensOrdered();
        if (screenIndex < 0 || screenIndex >= screens.Count) screenIndex = 0;
        return CaptureRegionPng(screens[screenIndex].Bounds);
    }

    /// <summary>Captures an arbitrary screen rectangle as a PNG byte array.</summary>
    public static byte[] CaptureRegionPng(Rectangle bounds)
    {
        var virtualBounds = ScreenLocationManager.GetVirtualScreenBounds();
        bounds.Intersect(virtualBounds);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>
    /// Captures a window by HWND. Uses DWM visible frame bounds via <c>CopyFromScreen</c> when foreground,
    /// with a <c>PrintWindow(PW_RENDERFULLCONTENT)</c> fallback when occluded.
    /// </summary>
    public static byte[] CaptureWindowPng(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return CaptureFullScreenPng();

        var r = NativeMethods.GetVisibleWindowRect(hwnd);
        var rect = ScreenLocationManager.ToRectangle(r);
        if (rect.Width <= 0 || rect.Height <= 0)
            return CaptureFullScreenPng();

        // Try PrintWindow first if window is not foreground
        if (NativeMethods.GetForegroundWindow() != hwnd)
        {
            try
            {
                using var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bmp);
                IntPtr hdc = g.GetHdc();
                bool ok = NativeMethods.PrintWindow(hwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);
                g.ReleaseHdc(hdc);
                if (ok)
                {
                    using var ms = new MemoryStream();
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
            catch { }
        }
        return CaptureRegionPng(rect);
    }

    /// <summary>Captures an individual control by its Win32 HWND.</summary>
    public static byte[] CaptureControlPng(IntPtr controlHwnd)
    {
        if (controlHwnd == IntPtr.Zero || !NativeMethods.IsWindow(controlHwnd))
            return Array.Empty<byte>();
        var r = ScreenLocationManager.GetAbsoluteControlBounds(controlHwnd);
        return CaptureRegionPng(ScreenLocationManager.ToRectangle(r));
    }

    /// <summary>Captures an individual UIA control by its bounding rectangle.</summary>
    public static byte[] CaptureElementPng(AutomationElement? element)
    {
        if (element == null) return Array.Empty<byte>();
        var bounds = UiaAutomation.GetBounds(element);
        return bounds.Width > 0 && bounds.Height > 0 ? CaptureRegionPng(bounds) : Array.Empty<byte>();
    }

    /// <summary>
    /// Interactive Snipping-Tool style overlay: lets the user drag a selection rectangle or click a control
    /// on screen and returns the captured PNG bytes + selected bounds.
    /// </summary>
    public static (byte[] Png, Rectangle Bounds) InteractiveSnipRegion()
    {
        var virtualBounds = ScreenLocationManager.GetVirtualScreenBounds();
        byte[] bgPng = CaptureRegionPng(virtualBounds);
        using var bgStream = new MemoryStream(bgPng);
        using var bgBmp = new Bitmap(bgStream);

        Rectangle selected = Rectangle.Empty;
        using var overlay = new SnipOverlayForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = virtualBounds,
            TopMost = true,
            Cursor = Cursors.Cross,
            ShowInTaskbar = false
        };

        System.Drawing.Point startPt = System.Drawing.Point.Empty;
        Rectangle currentRect = Rectangle.Empty;
        bool dragging = false;

        overlay.Paint += (_, e) =>
        {
            e.Graphics.DrawImageUnscaled(bgBmp, 0, 0);
            using var dimBrush = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
            e.Graphics.FillRectangle(dimBrush, 0, 0, overlay.Width, overlay.Height);
            if (currentRect.Width > 0 && currentRect.Height > 0)
            {
                e.Graphics.DrawImage(bgBmp, currentRect, currentRect, GraphicsUnit.Pixel);
                using var pen = new Pen(Color.FromArgb(255, 245, 158, 11), 2f);
                e.Graphics.DrawRectangle(pen, currentRect);
            }
        };
        overlay.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) { overlay.Close(); return; }
            dragging = true;
            startPt = e.Location;
            currentRect = new Rectangle(e.Location, System.Drawing.Size.Empty);
        };
        overlay.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            currentRect = new Rectangle(
                Math.Min(startPt.X, e.X),
                Math.Min(startPt.Y, e.Y),
                Math.Abs(e.X - startPt.X),
                Math.Abs(e.Y - startPt.Y));
            overlay.Invalidate();
        };
        overlay.MouseUp += (_, _) =>
        {
            dragging = false;
            selected = new Rectangle(
                virtualBounds.Left + currentRect.Left,
                virtualBounds.Top + currentRect.Top,
                currentRect.Width,
                currentRect.Height);
            overlay.DialogResult = DialogResult.OK;
            overlay.Close();
        };
        overlay.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) overlay.Close(); };

        overlay.ShowDialog();
        if (selected.Width < 4 || selected.Height < 4) return (Array.Empty<byte>(), Rectangle.Empty);
        return (CaptureRegionPng(selected), selected);
    }

    // --------------------------------------------------------------------------------------------
    // Recording OpenCV Details to the Control Sheet (First-pass or Second-pass Replay)
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Captures both the whole application window snapshot and the individual control snapshot,
    /// computes OpenCV visual descriptors (size, mean color, edge density), and stores them in the
    /// <see cref="ControlRepository"/> using the configured <see cref="ImageStorageMode"/> (file=, b=, c=, 7zc=).
    /// Used both during live test recording and during a second-pass replay to backfill OpenCV details.
    /// </summary>
    public static void RecordControlSnapshot(
        ControlRepository repo,
        ControlDefinition def,
        IntPtr windowHwnd,
        AutomationElement? liveElement = null,
        IntPtr controlHwnd = default)
    {
        try
        {
            byte[]? winPng = windowHwnd != IntPtr.Zero ? CaptureWindowPng(windowHwnd) : CaptureFullScreenPng();
            byte[]? ctrlPng = null;

            if (liveElement != null)
            {
                ctrlPng = CaptureElementPng(liveElement);
            }
            else if (controlHwnd != IntPtr.Zero)
            {
                ctrlPng = CaptureControlPng(controlHwnd);
            }
            else if (def.Attributes.TryGetValue(ControlDefinition.Keys.BoundingRectangle, out var bStr))
            {
                var rect = ParseRect(bStr);
                if (rect.Width > 0 && rect.Height > 0) ctrlPng = CaptureRegionPng(rect);
            }

            if (ctrlPng is { Length: > 0 })
            {
                using var mat = Cv2.ImDecode(ctrlPng, ImreadModes.Color);
                if (!mat.Empty())
                {
                    def.Attributes["OpenCV.Size"] = $"{mat.Width}x{mat.Height}";
                    var mean = Cv2.Mean(mat);
                    def.Attributes["OpenCV.MeanBGR"] = $"{mean.Val0:F1},{mean.Val1:F1},{mean.Val2:F1}";
                    using var gray = new Mat();
                    using var edges = new Mat();
                    Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
                    Cv2.Canny(gray, edges, 60, 180);
                    double edgeRatio = (double)Cv2.CountNonZero(edges) / Math.Max(1, edges.Rows * edges.Cols);
                    def.Attributes["OpenCV.EdgeDensity"] = edgeRatio.ToString("F4");
                }
            }

            repo.SetImages(def, ctrlPng is { Length: > 0 } ? ctrlPng : null, winPng);
            SystemLogger.Log($"Recorded OpenCV control + window snapshot for '{def.Name}' (storage={repo.Settings.ImageStorage}).");
        }
        catch (Exception ex)
        {
            SystemLogger.Log($"Failed to record OpenCV snapshot for '{def.Name}': {ex.Message}", SystemLogger.LogLevel.WARNING);
        }
    }

    // --------------------------------------------------------------------------------------------
    // Template Matching & Visual Comparison
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Searches the screen (or a window) for a template PNG (from a file path, inline b=/c=/7zc=, or ControlRepository name).
    /// Returns the best match location and confidence score in [0..1].
    /// </summary>
    public static VisionMatchResult FindOnScreen(
        byte[] templatePng,
        IntPtr searchWindowHwnd = default,
        double threshold = 0.82)
    {
        if (templatePng == null || templatePng.Length == 0) return VisionMatchResult.Empty;

        Rectangle originRect = searchWindowHwnd != IntPtr.Zero && NativeMethods.IsWindow(searchWindowHwnd)
            ? ScreenLocationManager.ToRectangle(NativeMethods.GetVisibleWindowRect(searchWindowHwnd))
            : ScreenLocationManager.GetVirtualScreenBounds();

        byte[] haystackPng = CaptureRegionPng(originRect);
        using var haystack = Cv2.ImDecode(haystackPng, ImreadModes.Color);
        using var needle = Cv2.ImDecode(templatePng, ImreadModes.Color);

        if (haystack.Empty() || needle.Empty() || needle.Width > haystack.Width || needle.Height > haystack.Height)
            return VisionMatchResult.Empty;

        using var result = new Mat();
        Cv2.MatchTemplate(haystack, needle, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

        var absBounds = new Rectangle(
            originRect.Left + maxLoc.X,
            originRect.Top + maxLoc.Y,
            needle.Width,
            needle.Height);
        var center = new System.Drawing.Point(absBounds.Left + absBounds.Width / 2, absBounds.Top + absBounds.Height / 2);
        bool ok = maxVal >= threshold;

        SystemLogger.Log($"OpenCV MatchTemplate confidence={maxVal:F3} (threshold={threshold:F2}) at {absBounds} -> {(ok ? "MATCH" : "NO MATCH")}",
            ok ? SystemLogger.LogLevel.INFO : SystemLogger.LogLevel.DEBUG);

        return new VisionMatchResult(ok, maxVal, absBounds, center);
    }

    /// <summary>Computes normalized structural similarity / correlation between two PNG images in [0..1].</summary>
    public static double CompareImages(byte[] pngA, byte[] pngB, string? diffSavePath = null)
    {
        using var matA = Cv2.ImDecode(pngA, ImreadModes.Color);
        using var matB = Cv2.ImDecode(pngB, ImreadModes.Color);
        if (matA.Empty() || matB.Empty()) return 0;

        using var resizedB = new Mat();
        if (matA.Size() != matB.Size())
            Cv2.Resize(matB, resizedB, matA.Size());
        else
            matB.CopyTo(resizedB);

        using var diff = new Mat();
        Cv2.Absdiff(matA, resizedB, diff);

        if (!string.IsNullOrWhiteSpace(diffSavePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(diffSavePath))!);
            Cv2.ImWrite(diffSavePath, diff);
        }

        Scalar meanDiff = Cv2.Mean(diff);
        double avgChannelDiff = (meanDiff.Val0 + meanDiff.Val1 + meanDiff.Val2) / (3.0 * 255.0);
        return Math.Clamp(1.0 - avgChannelDiff, 0.0, 1.0);
    }

    public static byte[]? ResolveTemplateBytes(KeywordContext ctx, string targetOrValue)
    {
        if (string.IsNullOrWhiteSpace(targetOrValue)) return null;
        if (ctx.Controls.TryGet(targetOrValue, out var def))
        {
            var img = ctx.Controls.GetImage(def);
            if (img is { Length: > 0 }) return img;
        }
        return ImageCodec.Decode(targetOrValue, WinFramePaths.Root);
    }

    public static Rectangle ParseRect(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length >= 4 &&
            int.TryParse(parts[0], out int x) &&
            int.TryParse(parts[1], out int y) &&
            int.TryParse(parts[2], out int w) &&
            int.TryParse(parts[3], out int h))
        {
            return new Rectangle(x, y, w, h);
        }
        return Rectangle.Empty;
    }

    private static string SaveSnapshotFile(KeywordContext ctx, byte[] png, string prefix, string? customPath = null)
    {
        string path = string.IsNullOrWhiteSpace(customPath)
            ? Path.Combine(ConfigManager.Current.SnapshotPath, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png")
            : WinFramePaths.Resolve(customPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, png);
        ctx.LastScreenshotPath = path;
        return path;
    }

    // --------------------------------------------------------------------------------------------
    // Keyword Sheet Bindings
    // --------------------------------------------------------------------------------------------

    [Keyword("CV_SNAPSHOT_SCREEN", Description = "Captures the whole virtual screen (or monitor index) to a PNG file.")]
    [Keyword("SNAPSHOT_SCREEN")]
    [Keyword("SCREENSHOT")]
    public static string KeywordSnapshotScreen(KeywordContext ctx, string filePath = "", int monitorIndex = -1)
    {
        byte[] png = monitorIndex >= 0 ? CaptureMonitorPng(monitorIndex) : CaptureFullScreenPng();
        return SaveSnapshotFile(ctx, png, "screen", filePath);
    }

    [Keyword("CV_SNAPSHOT_WINDOW", Description = "Captures Target application window to a PNG file.")]
    [Keyword("SNAPSHOT_WINDOW")]
    public static string KeywordSnapshotWindow(KeywordContext ctx, IntPtr hwnd, string filePath = "")
    {
        byte[] png = CaptureWindowPng(hwnd != IntPtr.Zero ? hwnd : ctx.CurrentWindowHwnd);
        return SaveSnapshotFile(ctx, png, "window", filePath);
    }

    [Keyword("CV_SNAPSHOT_CONTROL", Description = "Captures Target individual control (UIA/HWND/Repo) to a PNG file and updates the control sheet.")]
    [Keyword("SNAPSHOT_CONTROL")]
    public static string KeywordSnapshotControl(KeywordContext ctx, string target, string filePath = "")
    {
        var el = UiaAutomation.FindElement(target, ctx.CurrentWindowHwnd, ctx.Controls, 2500);
        byte[] png = el != null
            ? CaptureElementPng(el)
            : UiaAutomation.TryParseHwnd(target, out var h) ? CaptureControlPng(h) : Array.Empty<byte>();

        if (png.Length == 0) png = CaptureWindowPng(ctx.CurrentWindowHwnd);

        if (ctx.Controls.TryGet(target, out var def))
        {
            RecordControlSnapshot(ctx.Controls, def, ctx.CurrentWindowHwnd, el);
        }
        return SaveSnapshotFile(ctx, png, $"ctrl_{target.TrimStart('@')}", filePath);
    }

    [Keyword("CV_SNAPSHOT_REGION", Description = "Captures screen rectangle X,Y,W,H to a PNG file.")]
    [Keyword("SNAPSHOT_REGION")]
    public static string KeywordSnapshotRegion(KeywordContext ctx, string rectSpec, string filePath = "")
    {
        var rect = ParseRect(rectSpec);
        byte[] png = CaptureRegionPng(rect);
        return SaveSnapshotFile(ctx, png, "region", filePath);
    }

    [Keyword("CV_CLICK_IMAGE", Description = "Finds Target template image/control on screen via OpenCV and clicks its center.")]
    public static bool KeywordClickImage(KeywordContext ctx, string target, double threshold = 0.82, string button = "LEFT")
    {
        var bytes = ResolveTemplateBytes(ctx, target);
        if (bytes == null) return false;
        var match = FindOnScreen(bytes, ctx.CurrentWindowHwnd, threshold);
        if (!match.Found) match = FindOnScreen(bytes, IntPtr.Zero, threshold);
        if (!match.Found) return false;
        Win32Automation.ClickAtPoint(match.Center.X, match.Center.Y, button, 1);
        return true;
    }

    [Keyword("CV_DOUBLE_CLICK_IMAGE", Description = "Finds Target template image/control on screen via OpenCV and double-clicks it.")]
    public static bool KeywordDoubleClickImage(KeywordContext ctx, string target, double threshold = 0.82)
    {
        var bytes = ResolveTemplateBytes(ctx, target);
        if (bytes == null) return false;
        var match = FindOnScreen(bytes, ctx.CurrentWindowHwnd, threshold);
        if (!match.Found) match = FindOnScreen(bytes, IntPtr.Zero, threshold);
        if (!match.Found) return false;
        Win32Automation.ClickAtPoint(match.Center.X, match.Center.Y, "LEFT", 2);
        return true;
    }

    [Keyword("CV_VERIFY_IMAGE", Description = "Verifies that Target template image/control is visible on screen with confidence >= threshold.")]
    [Keyword("CV_EXISTS")]
    public static bool KeywordVerifyImage(KeywordContext ctx, string target, double threshold = 0.82)
    {
        var bytes = ResolveTemplateBytes(ctx, target);
        if (bytes == null) return false;
        var match = FindOnScreen(bytes, ctx.CurrentWindowHwnd, threshold);
        if (!match.Found) match = FindOnScreen(bytes, IntPtr.Zero, threshold);
        return match.Found;
    }

    [Keyword("CV_WAIT_IMAGE", Description = "Waits up to TimeoutMs for Target template image/control to appear on screen.")]
    public static bool KeywordWaitImage(KeywordContext ctx, string target, int timeoutMs = 10000, double threshold = 0.82)
    {
        var bytes = ResolveTemplateBytes(ctx, target);
        if (bytes == null) return false;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (FindOnScreen(bytes, ctx.CurrentWindowHwnd, threshold).Found ||
                FindOnScreen(bytes, IntPtr.Zero, threshold).Found)
                return true;
            Thread.Sleep(250);
        }
        return false;
    }

    [Keyword("CV_COMPARE", Description = "Compares Target control/image with current screen or baseline image and checks similarity >= minScore.")]
    public static bool KeywordCompare(KeywordContext ctx, string target, double minScore = 0.90)
    {
        var baseline = ResolveTemplateBytes(ctx, target);
        if (baseline == null) return false;
        var el = UiaAutomation.FindElement(target, ctx.CurrentWindowHwnd, ctx.Controls, 1500);
        byte[] live = el != null ? CaptureElementPng(el) : CaptureWindowPng(ctx.CurrentWindowHwnd);
        double score = CompareImages(baseline, live);
        SystemLogger.Log($"CV_COMPARE '{target}' similarity={score:F4} (min={minScore:F2})");
        return score >= minScore;
    }

    private sealed class SnipOverlayForm : Form
    {
        public SnipOverlayForm() => DoubleBuffered = true;
    }
}
