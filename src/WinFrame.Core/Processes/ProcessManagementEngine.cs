using System.Diagnostics;
using System.Text.RegularExpressions;
using WinFrame.Core.Config;
using WinFrame.Core.Keywords;
using WinFrame.Core.Logging;
using WinFrame.Core.Native;

namespace WinFrame.Core.Processes;

/// <summary>Live state of a harnessed application.</summary>
public sealed class HarnessedApp
{
    public string Key { get; init; } = "";
    public string ExecutablePath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string WindowTitlePattern { get; set; } = "";
    public Process? Process { get; set; }
    public int ProcessId { get; set; }
    public IntPtr MainWindowHandle { get; set; }
    public DateTime AttachedAt { get; set; } = DateTime.Now;
    public int RecoveryCount { get; set; }

    public bool IsAlive
    {
        get
        {
            try
            {
                if (MainWindowHandle != IntPtr.Zero && NativeMethods.IsWindow(MainWindowHandle)) return true;
                return Process is { HasExited: false };
            }
            catch { return false; }
        }
    }

    public bool IsResponding
    {
        get
        {
            if (MainWindowHandle == IntPtr.Zero || !NativeMethods.IsWindow(MainWindowHandle)) return false;
            return NativeMethods.SendMessageTimeout(MainWindowHandle, 0, IntPtr.Zero, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 1500, out _) != IntPtr.Zero;
        }
    }
}

/// <summary>
/// Integrated process management engine.
/// Starts, attaches to, monitors and re-harnesses applications under test so that Win32, UIA and OpenCV
/// calls keep working even if the application spawns a new process (e.g. UWP / Calculator frame host),
/// recreates its main window, or crashes and needs to be restarted.
/// </summary>
[KeywordLibrary("Process Management")]
public sealed class ProcessManagementEngine : IDisposable
{
    public static ProcessManagementEngine Shared { get; } = new();

    private readonly object sync = new();
    private readonly Dictionary<string, HarnessedApp> apps = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? watchdogCts;

    public string? ActiveKey { get; set; }

    public HarnessedApp? Active => ActiveKey != null && apps.TryGetValue(ActiveKey, out var a) ? a : apps.Values.LastOrDefault();

    public IReadOnlyList<HarnessedApp> All { get { lock (sync) return apps.Values.ToList(); } }

    public event Action<HarnessedApp, string>? AppStateChanged;

    // --------------------------------------------------------------------------------------------
    // Core operations
    // --------------------------------------------------------------------------------------------

    /// <summary>Starts or attaches to the application described by a <see cref="TestBedConfig"/>.</summary>
    public HarnessedApp Harness(TestBedConfig bed) =>
        StartOrAttach(bed.Name, bed.ApplicationPath, bed.Arguments, bed.ProcessName, bed.WindowTitlePattern, bed.WorkingDirectory, bed.StartupTimeoutMs, bed.StartIfNotRunning);

    /// <summary>Attaches to a running window/process or starts the executable if not running.</summary>
    public HarnessedApp StartOrAttach(string key, string executablePath, string arguments = "", string processName = "", string windowTitlePattern = "", string workingDir = "", int timeoutMs = 15000, bool startIfNotRunning = true)
    {
        var app = new HarnessedApp
        {
            Key = string.IsNullOrWhiteSpace(key) ? Path.GetFileNameWithoutExtension(executablePath) : key,
            ExecutablePath = executablePath, Arguments = arguments, WorkingDirectory = workingDir,
            ProcessName = string.IsNullOrWhiteSpace(processName) ? Path.GetFileNameWithoutExtension(executablePath) : processName,
            WindowTitlePattern = windowTitlePattern
        };

        if (!TryLocateExisting(app) && startIfNotRunning && !string.IsNullOrWhiteSpace(executablePath))
            LaunchProcess(app, timeoutMs);

        lock (sync) apps[app.Key] = app;
        ActiveKey = app.Key;
        AppStateChanged?.Invoke(app, "Harnessed");
        return app;
    }

    /// <summary>
    /// Re-harnesses a lost application: checks the current handle, searches top-level windows by title/process
    /// (covering UWP ApplicationFrameWindow hosts), and finally restarts the executable if allowed.
    /// </summary>
    public IntPtr EnsureHarnessed(string? key = null, bool restartIfExited = true, int timeoutMs = 15000)
    {
        HarnessedApp? app;
        lock (sync)
        {
            var k = string.IsNullOrWhiteSpace(key) ? ActiveKey : key;
            app = k != null && apps.TryGetValue(k, out var found) ? found : apps.Values.LastOrDefault();
        }
        if (app == null) return IntPtr.Zero;

        if (app.MainWindowHandle != IntPtr.Zero && NativeMethods.IsWindow(app.MainWindowHandle) && NativeMethods.IsWindowVisible(app.MainWindowHandle))
            return app.MainWindowHandle;

        SystemLogger.Log($"Application '{app.Key}' handle {app.MainWindowHandle} was lost; attempting to re-harness...", SystemLogger.LogLevel.WARNING);
        if (TryLocateExisting(app))
        {
            app.RecoveryCount++;
            SystemLogger.Log($"Re-harnessed '{app.Key}' -> HWND {app.MainWindowHandle} (PID {app.ProcessId})");
            AppStateChanged?.Invoke(app, "Reharnessed");
            return app.MainWindowHandle;
        }

        if (restartIfExited && !string.IsNullOrWhiteSpace(app.ExecutablePath))
        {
            LaunchProcess(app, timeoutMs);
            app.RecoveryCount++;
            AppStateChanged?.Invoke(app, "Restarted");
            return app.MainWindowHandle;
        }
        return IntPtr.Zero;
    }

    public bool TryLocateExisting(HarnessedApp app)
    {
        // 1. Existing Process object still alive?
        try
        {
            if (app.Process is { HasExited: false })
            {
                app.Process.Refresh();
                if (app.Process.MainWindowHandle != IntPtr.Zero && NativeMethods.IsWindow(app.Process.MainWindowHandle))
                {
                    app.MainWindowHandle = app.Process.MainWindowHandle;
                    app.ProcessId = app.Process.Id;
                    return true;
                }
            }
        }
        catch { }

        // 2. Match top-level visible windows by title regex or process name.
        var hwnd = FindTopLevelWindow(app.WindowTitlePattern, app.ProcessName);
        if (hwnd != IntPtr.Zero)
        {
            app.MainWindowHandle = hwnd;
            app.ProcessId = NativeMethods.GetProcessId(hwnd);
            try { app.Process = Process.GetProcessById(app.ProcessId); } catch { }
            return true;
        }
        return false;
    }

    private void LaunchProcess(HarnessedApp app, int timeoutMs)
    {
        SystemLogger.Log($"Starting application '{app.Key}': {app.ExecutablePath} {app.Arguments}");
        var psi = new ProcessStartInfo(app.ExecutablePath, app.Arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(app.WorkingDirectory) ? "" : app.WorkingDirectory
        };
        app.Process = Process.Start(psi);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (TryLocateExisting(app) && app.MainWindowHandle != IntPtr.Zero)
            {
                app.AttachedAt = DateTime.Now;
                SystemLogger.Log($"Started '{app.Key}' -> HWND {app.MainWindowHandle} (PID {app.ProcessId}) in {sw.ElapsedMilliseconds} ms");
                return;
            }
            Thread.Sleep(200);
        }
        SystemLogger.Log($"Started '{app.Key}' but main window did not appear within {timeoutMs} ms.", SystemLogger.LogLevel.WARNING);
    }

    /// <summary>Finds a visible top-level window matching a title regex and/or process name (including UWP child processes).</summary>
    public static IntPtr FindTopLevelWindow(string? titlePattern, string? processName = null)
    {
        Regex? rx = string.IsNullOrWhiteSpace(titlePattern) ? null : new Regex(titlePattern, RegexOptions.IgnoreCase);
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            var title = NativeMethods.GetWindowTextManaged(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return true;
            if (rx != null && rx.IsMatch(title)) { found = hwnd; return false; }
            if (!string.IsNullOrWhiteSpace(processName))
            {
                if (MatchesProcess(hwnd, processName)) { found = hwnd; return false; }
                // UWP windows live inside ApplicationFrameWindow; check child window PIDs.
                bool childMatch = false;
                NativeMethods.EnumChildWindows(hwnd, (child, _) =>
                {
                    if (MatchesProcess(child, processName)) { childMatch = true; return false; }
                    return true;
                }, IntPtr.Zero);
                if (childMatch) { found = hwnd; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static bool MatchesProcess(IntPtr hwnd, string processName)
    {
        try
        {
            using var p = Process.GetProcessById(NativeMethods.GetProcessId(hwnd));
            return p.ProcessName.Contains(processName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public bool Stop(string? key = null, bool force = false)
    {
        var k = string.IsNullOrWhiteSpace(key) ? ActiveKey : key;
        if (k == null) return false;
        HarnessedApp? app;
        lock (sync) apps.TryGetValue(k, out app);
        if (app == null) return false;
        try
        {
            if (app.MainWindowHandle != IntPtr.Zero && NativeMethods.IsWindow(app.MainWindowHandle))
            {
                NativeMethods.PostMessage(app.MainWindowHandle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(300);
            }
            if (force && app.Process is { HasExited: false }) app.Process.Kill(entireProcessTree: true);
            AppStateChanged?.Invoke(app, "Stopped");
            return true;
        }
        catch (Exception ex)
        {
            SystemLogger.Log($"Failed to stop '{app.Key}': {ex.Message}", SystemLogger.LogLevel.WARNING);
            return false;
        }
    }

    /// <summary>Starts a background watchdog that re-harnesses any tracked application whose window disappears.</summary>
    public void StartWatchdog(int pollMs = 3000)
    {
        StopWatchdog();
        watchdogCts = new CancellationTokenSource();
        var token = watchdogCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var app in All)
                    if (!app.IsAlive) EnsureHarnessed(app.Key, restartIfExited: false);
                await Task.Delay(pollMs, token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }, token);
    }

    public void StopWatchdog() { try { watchdogCts?.Cancel(); } catch { } watchdogCts = null; }

    public void Dispose() => StopWatchdog();

    // --------------------------------------------------------------------------------------------
    // Keyword bindings (callable from any keyword sheet)
    // --------------------------------------------------------------------------------------------

    [Keyword("START_APP", Description = "Starts an executable (and waits for its main window) or attaches if already running. Value: args|titlePattern")]
    [Keyword("LAUNCH")]
    public static IntPtr KeywordStart(KeywordContext ctx, string target, string args = "", string titlePattern = "")
    {
        var bed = ConfigManager.GetTestBed(target);
        var app = bed != null
            ? Shared.Harness(bed)
            : Shared.StartOrAttach(Path.GetFileNameWithoutExtension(target), target, args, "", string.IsNullOrWhiteSpace(titlePattern) ? Path.GetFileNameWithoutExtension(target) : titlePattern);
        ctx.CurrentWindowHwnd = app.MainWindowHandle;
        return app.MainWindowHandle;
    }

    [Keyword("ATTACH_APP", Description = "Attaches to an already-running window by title pattern or process name.")]
    [Keyword("ATTACH")]
    public static bool KeywordAttach(KeywordContext ctx, string target, string processName = "")
    {
        var app = Shared.StartOrAttach(target, "", "", processName, target, startIfNotRunning: false);
        ctx.CurrentWindowHwnd = app.MainWindowHandle;
        return app.MainWindowHandle != IntPtr.Zero;
    }

    [Keyword("REHARNESS", Description = "Re-harnesses the active (or named) application if its window handle was lost, restarting if exited.")]
    public static bool KeywordReharness(KeywordContext ctx, string target = "")
    {
        var hwnd = Shared.EnsureHarnessed(string.IsNullOrWhiteSpace(target) ? null : target, restartIfExited: true);
        if (hwnd != IntPtr.Zero) ctx.CurrentWindowHwnd = hwnd;
        return hwnd != IntPtr.Zero;
    }

    [Keyword("RESTART_APP", Description = "Stops and restarts the active (or named) application.")]
    public static IntPtr KeywordRestart(KeywordContext ctx, string target = "")
    {
        Shared.Stop(string.IsNullOrWhiteSpace(target) ? null : target, force: true);
        Thread.Sleep(400);
        var hwnd = Shared.EnsureHarnessed(string.IsNullOrWhiteSpace(target) ? null : target, restartIfExited: true);
        ctx.CurrentWindowHwnd = hwnd;
        return hwnd;
    }

    [Keyword("STOP_APP", Description = "Closes (or kills when value=force) the active or named application.")]
    [Keyword("CLOSE_APP")]
    public static bool KeywordStop(string target = "", string mode = "") =>
        Shared.Stop(string.IsNullOrWhiteSpace(target) ? null : target, mode.Equals("force", StringComparison.OrdinalIgnoreCase));

    [Keyword("WAIT_FOR_WINDOW", Description = "Waits up to Value ms for a window matching Target (title regex) to appear.")]
    public static bool KeywordWaitForWindow(KeywordContext ctx, string target, int timeoutMs = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var h = FindTopLevelWindow(target);
            if (h != IntPtr.Zero) { ctx.CurrentWindowHwnd = h; return true; }
            Thread.Sleep(200);
        }
        return false;
    }

    [Keyword("CHECK_APP_RUNNING", Description = "Verifies that the harnessed or named application is alive and responding.")]
    public static bool KeywordCheckRunning(string target = "")
    {
        if (!string.IsNullOrWhiteSpace(target) && Shared.apps.TryGetValue(target, out var app)) return app.IsAlive && app.IsResponding;
        return Shared.Active is { IsAlive: true, IsResponding: true } || FindTopLevelWindow(target, target) != IntPtr.Zero;
    }
}

