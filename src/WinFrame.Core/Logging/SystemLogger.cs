using System.Collections.Concurrent;

namespace WinFrame.Core.Logging;

/// <summary>Separate log streams: system (engine/run), user (UI/user actions) and test (test step results).</summary>
public enum LogChannel { System, User, Test }

/// <summary>A single structured log record.</summary>
public sealed record LogEntry(
    DateTime Timestamp,
    LogChannel Channel,
    SystemLogger.LogLevel Level,
    string Message,
    string? RunId = null,
    string? TestName = null)
{
    public override string ToString() =>
        $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Channel}] [{Level}]{(TestName is null ? "" : $" [{TestName}]")} {Message}";
}

/// <summary>
/// A robust, thread-safe logger for granular traceability during automated execution.
/// Originally a single-file logger; now split into System / User / Test channels, each written to
/// its own daily file under <see cref="LogDirectory"/>, with an in-memory ring buffer and an event
/// stream for live UIs (tray tooltips, run panels) and HTML report generation.
/// </summary>
public static class SystemLogger
{
    private static readonly object lockObj = new object();
    private static readonly ConcurrentQueue<LogEntry> recent = new();
    private static readonly AsyncLocal<(string? RunId, string? TestName)> scope = new();

    public enum LogLevel { INFO, WARNING, ERROR, DEBUG, PASS, FAIL }

    /// <summary>Root folder for logs. Sub-folders: system, user, test.</summary>
    public static string LogDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>Entries below this level (DEBUG) are dropped when false.</summary>
    public static bool IncludeDebug { get; set; } = true;

    /// <summary>Echo entries to the console (CLI mode).</summary>
    public static bool ConsoleOutput { get; set; } = true;

    /// <summary>Maximum entries kept in memory for UI/report use.</summary>
    public static int RecentCapacity { get; set; } = 20000;

    /// <summary>Raised for every entry written (on the calling thread).</summary>
    public static event Action<LogEntry>? EntryLogged;

    /// <summary>Original API: writes to the System channel.</summary>
    public static void Log(string message, LogLevel level = LogLevel.INFO) => Write(LogChannel.System, level, message);

    /// <summary>Writes to the User channel (UI actions, settings changes, recorder events).</summary>
    public static void User(string message, LogLevel level = LogLevel.INFO) => Write(LogChannel.User, level, message);

    /// <summary>Writes to the Test channel (step execution and results).</summary>
    public static void Test(string message, LogLevel level = LogLevel.INFO) => Write(LogChannel.Test, level, message);

    /// <summary>Tags all entries written on this async flow with a run id / test name until disposed.</summary>
    public static IDisposable BeginScope(string? runId, string? testName)
    {
        var previous = scope.Value;
        scope.Value = (runId, testName);
        return new ScopeRestorer(previous);
    }

    public static void Write(LogChannel channel, LogLevel level, string message)
    {
        if (level == LogLevel.DEBUG && !IncludeDebug) return;
        var (runId, testName) = scope.Value;
        var entry = new LogEntry(DateTime.Now, channel, level, message, runId, testName);
        string formattedMessage = entry.ToString();

        lock (lockObj)
        {
            try
            {
                string dir = Path.Combine(LogDirectory, channel.ToString().ToLowerInvariant());
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"{channel.ToString().ToLowerInvariant()}_{entry.Timestamp:yyyyMMdd}.log");
                File.AppendAllText(file, formattedMessage + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the engine down (disk full, locked file, etc.).
            }
            if (ConsoleOutput)
            {
                var color = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = level switch
                    {
                        LogLevel.ERROR or LogLevel.FAIL => ConsoleColor.Red,
                        LogLevel.WARNING => ConsoleColor.Yellow,
                        LogLevel.PASS => ConsoleColor.Green,
                        LogLevel.DEBUG => ConsoleColor.DarkGray,
                        _ => color
                    };
                    Console.WriteLine(formattedMessage);
                }
                catch { /* no console attached */ }
                finally { try { Console.ForegroundColor = color; } catch { } }
            }
        }

        recent.Enqueue(entry);
        while (recent.Count > RecentCapacity && recent.TryDequeue(out _)) { }

        try { EntryLogged?.Invoke(entry); } catch { /* subscribers must not break logging */ }
    }

    /// <summary>Returns recent in-memory entries, optionally filtered.</summary>
    public static IReadOnlyList<LogEntry> Recent(LogChannel? channel = null, string? runId = null, int max = int.MaxValue) =>
        recent.Where(e => (channel is null || e.Channel == channel) && (runId is null || e.RunId == runId))
              .TakeLast(max).ToList();

    private sealed class ScopeRestorer((string?, string?) previous) : IDisposable
    {
        public void Dispose() => scope.Value = previous;
    }
}

