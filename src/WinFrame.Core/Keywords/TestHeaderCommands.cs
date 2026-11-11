using System.Text;
using WinFrame.Core.Config;
using WinFrame.Core.Execution;
using WinFrame.Core.Logging;
using WinFrame.Core.Processes;

namespace WinFrame.Core.Keywords;

/// <summary>
/// Represents a single parsed token from the reserved <c>COMMAND</c> header keyword's comma-separated command list.
/// Supports both standalone command flags (e.g. <c>self_healing</c>, <c>video</c>, <c>highlight</c>) and
/// <c>name="x,x"</c> / <c>name=value</c> key-value commands with quote-aware comma parsing
/// (e.g. <c>video="external store from config loc"</c> or <c>args="--mode=test,fast"</c>).
/// </summary>
public sealed record ParsedTestCommand(string Name, string Value, bool HasExplicitValue, string RawToken)
{
    public string EffectiveValue => HasExplicitValue ? Value : "1";

    public string[] SplitValueItems() =>
        string.IsNullOrWhiteSpace(Value)
            ? Array.Empty<string>()
            : Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public override string ToString() => HasExplicitValue ? $"{Name}=\"{Value}\"" : Name;
}

/// <summary>
/// Quote-aware parser and formatter for the reserved <c>COMMAND</c> test header keyword.
/// </summary>
public static class TestCommandParser
{
    /// <summary>
    /// Parses a comma-separated command list where individual commands may be standalone names
    /// (<c>self_healing, highlight, video</c>) or <c>name="x,x"</c> / <c>name=value</c> assignments
    /// whose quoted values can themselves contain commas or spaces
    /// (for example: <c>self_healing, video="external store from config loc", args="x,y"</c>).
    /// </summary>
    public static List<ParsedTestCommand> Parse(string? commandList)
    {
        var result = new List<ParsedTestCommand>();
        if (string.IsNullOrWhiteSpace(commandList)) return result;

        var tokens = SplitTopLevelCommas(commandList);
        foreach (var raw in tokens)
        {
            string token = raw.Trim();
            if (string.IsNullOrEmpty(token)) continue;

            int eq = IndexOfUnquotedEquals(token);
            if (eq < 0)
            {
                string name = Unquote(token).Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    result.Add(new ParsedTestCommand(name, "1", HasExplicitValue: false, RawToken: token));
                }
            }
            else
            {
                string name = token[..eq].Trim();
                string valPart = token[(eq + 1)..].Trim();
                string unquotedVal = Unquote(valPart);
                if (!string.IsNullOrEmpty(name))
                {
                    result.Add(new ParsedTestCommand(name, unquotedVal, HasExplicitValue: true, RawToken: token));
                }
            }
        }

        return result;
    }

    public static List<string> SplitTopLevelCommas(string text)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        char quoteChar = '\0';

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoteChar != '\0')
            {
                if (c == quoteChar)
                {
                    // Support escaped doubled quotes "" inside "..."
                    if (i + 1 < text.Length && text[i + 1] == quoteChar)
                    {
                        sb.Append(c);
                        sb.Append(text[++i]);
                    }
                    else
                    {
                        quoteChar = '\0';
                        sb.Append(c);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c is '"' or '\'')
                {
                    quoteChar = c;
                    sb.Append(c);
                }
                else if (c == ',')
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        if (sb.Length > 0)
            list.Add(sb.ToString());

        return list;
    }

    private static int IndexOfUnquotedEquals(string token)
    {
        char quoteChar = '\0';
        for (int i = 0; i < token.Length; i++)
        {
            char c = token[i];
            if (quoteChar != '\0')
            {
                if (c == quoteChar) quoteChar = '\0';
            }
            else if (c is '"' or '\'')
            {
                quoteChar = c;
            }
            else if (c == '=')
            {
                return i;
            }
        }
        return -1;
    }

    public static string Unquote(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        value = value.Trim();
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) ||
             (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            char q = value[0];
            string inner = value[1..^1];
            return inner.Replace(new string(q, 2), q.ToString());
        }
        return value;
    }
}

/// <summary>
/// Reserved Framework Header &amp; Application Command Structure Keywords.
/// <para>
/// Designed to be placed in the header section (first steps) of a test case:
/// <list type="number">
/// <item><c>AUT</c> (<c>SET_AUT</c> / <c>APP_UNDER_TEST</c>) — Sets the Application Under Test location or TestBed directly in the test case (overriding the queue/config TestBed).</item>
/// <item><c>COMMAND</c> (<c>CMD</c> / <c>COMMANDS</c>) — Runs a comma-separated command list for app config, app arguments, or per-test framework attributes such as <c>self_healing</c>, <c>highlight</c>, or <c>video="external store from config loc"</c> / <c>name="x,x"</c>.</item>
/// <item><c>RA</c> (<c>REUSE_APP</c>) — Controls whether to reuse an already-running instance of the AUT at the beginning of the test, or falls back to Config (<see cref="AppConfig.ReuseApp"/>) if not set in the test.</item>
/// <item><c>KAO</c> (<c>KEEP_APP_OPEN</c>) — Controls whether to keep the AUT open between tests when the test finishes, or falls back to Config (<see cref="AppConfig.KeepAppOpen"/>) if not set in the test.</item>
/// </list>
/// </para>
/// </summary>
[KeywordLibrary("Framework Header & App Commands")]
public static class TestHeaderCommands
{
    public static readonly HashSet<string> ReservedHeaderKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AUT", "SET_AUT", "APP_UNDER_TEST",
        "COMMAND", "CMD", "COMMANDS",
        "RA", "REUSE_APP",
        "KAO", "KEEP_APP_OPEN"
    };

    public static bool IsReservedHeaderKeyword(string? action) =>
        !string.IsNullOrWhiteSpace(action) && ReservedHeaderKeywords.Contains(action.Trim());

    /// <summary>
    /// Reserved Keyword 1: <c>AUT</c> — Sets the Application Under Test location (executable path, command, or TestBed name)
    /// directly in the test case in case of override from the queue set.
    /// If <paramref name="target"/> is empty, falls back to the configured TestBed or <see cref="AppConfig.DefaultTestBed"/>.
    /// </summary>
    [Keyword("AUT", Description = "Reserved header keyword: sets the Application Under Test location or TestBed directly in the test case (overriding queue/config). Value: optional launch arguments|titlePattern.")]
    [Keyword("SET_AUT")]
    [Keyword("APP_UNDER_TEST")]
    public static string KeywordAut(KeywordContext ctx, string target = "", string args = "", string titlePattern = "")
    {
        string autSpec = !string.IsNullOrWhiteSpace(target) ? target.Trim() : (args ?? "").Trim();
        string effectiveArgs = !string.IsNullOrWhiteSpace(target) ? (args ?? "").Trim() : "";

        if (!string.IsNullOrWhiteSpace(ctx.AppArgumentsOverride) && string.IsNullOrWhiteSpace(effectiveArgs))
        {
            effectiveArgs = ctx.AppArgumentsOverride;
        }

        if (string.IsNullOrWhiteSpace(autSpec))
        {
            var fallbackBed = ctx.TestBed ?? ConfigManager.GetTestBed(ctx.Config.DefaultTestBed);
            if (fallbackBed != null)
            {
                ctx.TestBed = fallbackBed;
                ctx.Variables["aut"] = fallbackBed.ApplicationPath;
                ctx.Variables["testbed"] = fallbackBed.Name;
                SystemLogger.Test($"[AUT] Using TestBed '{fallbackBed.Name}' ({fallbackBed.ApplicationPath}) from queue/config.");
                return fallbackBed.ApplicationPath;
            }
            SystemLogger.Test("[AUT] No AUT override specified in test; using active window context.");
            return "";
        }

        ctx.AutOverride = autSpec;
        ctx.Variables["aut"] = autSpec;

        // Check if autSpec matches a named TestBed in ConfigManager
        var existingBed = ConfigManager.GetTestBed(autSpec);
        if (existingBed != null)
        {
            var bedClone = existingBed.Clone();
            if (!string.IsNullOrWhiteSpace(effectiveArgs))
                bedClone.Arguments = effectiveArgs;
            if (!string.IsNullOrWhiteSpace(titlePattern))
                bedClone.WindowTitlePattern = titlePattern;
            ctx.TestBed = bedClone;
            ctx.Variables["testbed"] = bedClone.Name;
        }
        else
        {
            string exeName = Path.GetFileNameWithoutExtension(autSpec);
            if (string.IsNullOrWhiteSpace(exeName)) exeName = autSpec;
            string procName = exeName.Equals("calc", StringComparison.OrdinalIgnoreCase) ? "CalculatorApp" : exeName;
            string winPattern = !string.IsNullOrWhiteSpace(titlePattern)
                ? titlePattern
                : exeName.Equals("calc", StringComparison.OrdinalIgnoreCase) ? "Calculator" : exeName;

            ctx.TestBed = new TestBedConfig
            {
                Name = exeName,
                Description = $"Test-level AUT override ({autSpec})",
                ApplicationPath = autSpec,
                Arguments = effectiveArgs,
                WorkingDirectory = ctx.WorkingDirectoryOverride ?? "",
                ProcessName = procName,
                WindowTitlePattern = winPattern,
                StartIfNotRunning = true,
                ReuseApp = ctx.EffectiveReuseApp,
                KeepAppOpen = ctx.EffectiveKeepAppOpen
            };
            ctx.Variables["testbed"] = ctx.TestBed.Name;
        }

        // Launch or attach according to EffectiveReuseApp (RA) unless running in dry/headless mode where StartIfNotRunning is false
        if (ctx.TestBed != null && ctx.AutoHarnessOnAutKeyword && ctx.TestBed.StartIfNotRunning)
        {
            try
            {
                var app = ProcessManagementEngine.Shared.Harness(ctx.TestBed, reuseExisting: ctx.EffectiveReuseApp);
                if (app.MainWindowHandle != IntPtr.Zero)
                    ctx.CurrentWindowHwnd = app.MainWindowHandle;
            }
            catch (Exception ex)
            {
                SystemLogger.Test($"[AUT] Harness note for '{autSpec}': {ex.Message}", SystemLogger.LogLevel.WARNING);
            }
        }

        SystemLogger.Test($"[AUT] Set Application Under Test -> '{autSpec}' (Args='{effectiveArgs}', RA={ctx.EffectiveReuseApp}, KAO={ctx.EffectiveKeepAppOpen})");
        return autSpec;
    }

    /// <summary>
    /// Reserved Keyword 2: <c>COMMAND</c> — Runs/applies a comma-separated command list used for app config,
    /// app arguments, or per-test framework attributes (e.g. <c>self_healing</c>, <c>video="external store from config loc"</c>,
    /// <c>args="x,x"</c>, <c>highlight="1"</c>).
    /// Commands may be placed in <c>Value</c>, <c>Target</c>, or both (<c>Target="video", Value="external store from config loc"</c>).
    /// </summary>
    [Keyword("COMMAND", Description = "Reserved header keyword: applies a comma-separated command list for app config, app arguments, or test framework attributes (e.g. self_healing, video=\"external store from config loc\", args=\"x,x\").")]
    [Keyword("CMD")]
    [Keyword("COMMANDS")]
    public static string KeywordCommand(KeywordContext ctx, string target = "", string value = "")
    {
        string combinedCommandText;
        if (!string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(value))
        {
            // If Target is a single command key without '=' and Value is its parameter, combine as target="value" unless Target already has '=' or ','
            if (!target.Contains('=') && !target.Contains(','))
            {
                string cleanVal = TestCommandParser.Unquote(value);
                combinedCommandText = $"{target.Trim()}=\"{cleanVal}\"";
            }
            else
            {
                combinedCommandText = $"{target.Trim()}, {value.Trim()}";
            }
        }
        else
        {
            combinedCommandText = !string.IsNullOrWhiteSpace(value) ? value.Trim() : (target ?? "").Trim();
        }

        var commands = TestCommandParser.Parse(combinedCommandText);
        if (commands.Count == 0)
        {
            SystemLogger.Test("[COMMAND] No test-level commands specified.");
            return "NONE";
        }

        var appliedSummaries = new List<string>();
        foreach (var cmd in commands)
        {
            ApplySingleCommand(ctx, cmd);
            appliedSummaries.Add(cmd.ToString());
        }

        string summary = string.Join(", ", appliedSummaries);
        ctx.Variables["command_list"] = summary;
        SystemLogger.Test($"[COMMAND] Applied {commands.Count} command(s): {summary}");
        return summary;
    }

    /// <summary>
    /// Reserved Keyword 3: <c>RA</c> (<c>REUSE_APP</c>) — Controls whether to reuse an already-running instance
    /// of the AUT at the beginning of the test, or falls back to Config (<see cref="AppConfig.ReuseApp"/>) if empty/default.
    /// </summary>
    [Keyword("RA", Description = "Reserved header keyword: Reuse App at the beginning of the test (1/true = attach & reuse running AUT; 0/false = launch fresh instance; empty/config = use Config default).")]
    [Keyword("REUSE_APP")]
    public static bool KeywordReuseApp(KeywordContext ctx, string target = "", string value = "")
    {
        string raw = !string.IsNullOrWhiteSpace(value) ? value.Trim() : (target ?? "").Trim();
        bool fallback = ctx.TestBed?.ReuseApp ?? ctx.Config.ReuseApp;
        bool resolved = ParseBoolWithConfigFallback(raw, fallback);

        ctx.ReuseApp = resolved;
        ctx.Variables["ra"] = resolved ? "1" : "0";
        ctx.Variables["reuse_app"] = resolved ? "true" : "false";
        if (ctx.TestBed != null) ctx.TestBed.ReuseApp = resolved;

        SystemLogger.Test($"[RA] Reuse App at test start = {resolved} (raw='{(string.IsNullOrEmpty(raw) ? "config" : raw)}')");
        return resolved;
    }

    /// <summary>
    /// Reserved Keyword 4: <c>KAO</c> (<c>KEEP_APP_OPEN</c>) — Controls whether to keep the AUT open between tests
    /// when the test finishes, or falls back to Config (<see cref="AppConfig.KeepAppOpen"/>) if empty/default.
    /// </summary>
    [Keyword("KAO", Description = "Reserved header keyword: Keep App Open between tests (1/true = keep AUT open after test; 0/false = close AUT when test completes; empty/config = use Config default).")]
    [Keyword("KEEP_APP_OPEN")]
    public static bool KeywordKeepAppOpen(KeywordContext ctx, string target = "", string value = "")
    {
        string raw = !string.IsNullOrWhiteSpace(value) ? value.Trim() : (target ?? "").Trim();
        bool fallback = ctx.TestBed != null ? ctx.TestBed.KeepAppOpen : ctx.Config.KeepAppOpen;
        bool resolved = ParseBoolWithConfigFallback(raw, fallback);

        ctx.KeepAppOpen = resolved;
        ctx.Variables["kao"] = resolved ? "1" : "0";
        ctx.Variables["keep_app_open"] = resolved ? "true" : "false";
        if (ctx.TestBed != null)
        {
            ctx.TestBed.KeepAppOpen = resolved;
            ctx.TestBed.CloseAfterRun = !resolved;
        }

        SystemLogger.Test($"[KAO] Keep App Open between tests = {resolved} (raw='{(string.IsNullOrEmpty(raw) ? "config" : raw)}')");
        return resolved;
    }

    public static bool ParseBoolWithConfigFallback(string? raw, bool configDefault)
    {
        if (string.IsNullOrWhiteSpace(raw) ||
            raw.Equals("config", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return configDefault;
        }

        raw = raw.Trim();
        if (raw == "1" ||
            raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw == "0" ||
            raw.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return configDefault;
    }

    private static void ApplySingleCommand(KeywordContext ctx, ParsedTestCommand cmd)
    {
        string key = cmd.Name.Trim();
        string val = cmd.EffectiveValue;

        ctx.ActiveCommands[key] = val;
        ctx.Variables[$"cmd.{key}"] = val;
        ctx.Variables[key] = val;

        switch (key.ToLowerInvariant())
        {
            case "self_healing":
            case "selfhealing":
            case "heal":
                ctx.SelfHealingEnabled = !val.Equals("0", StringComparison.OrdinalIgnoreCase) &&
                                         !val.Equals("false", StringComparison.OrdinalIgnoreCase) &&
                                         !val.Equals("off", StringComparison.OrdinalIgnoreCase);
                ctx.SelfHealingStrategy = val;
                ctx.Variables["self_healing"] = val;
                break;

            case "video":
            case "video_recording":
            case "record_video":
                ctx.VideoRecordingSetting = val;
                ctx.Variables["video"] = val;
                break;

            case "args":
            case "app_args":
            case "arguments":
                ctx.AppArgumentsOverride = val;
                ctx.Variables["app_args"] = val;
                if (ctx.TestBed != null) ctx.TestBed.Arguments = val;
                break;

            case "workdir":
            case "working_dir":
            case "working_directory":
                ctx.WorkingDirectoryOverride = val;
                ctx.Variables["workdir"] = val;
                if (ctx.TestBed != null) ctx.TestBed.WorkingDirectory = val;
                break;

            case "title":
            case "window_title":
                if (ctx.TestBed != null) ctx.TestBed.WindowTitlePattern = val;
                ctx.Variables["window_title"] = val;
                break;

            case "highlight":
                ctx.Variables["highlight"] = val;
                break;

            case "highlight_color":
                ctx.Variables["highlight_color"] = val;
                break;

            case "highlight_interval":
                ctx.Variables["highlight_interval"] = val;
                break;

            case "capture_images":
            case "opencv_record":
                ctx.RecordOpenCvSnapshots = ParseBoolWithConfigFallback(val, ctx.RecordOpenCvSnapshots);
                break;

            case "timeout":
            case "step_timeout":
                if (int.TryParse(val, out int tms) && tms > 0)
                    ctx.StepTimeoutOverrideMs = tms;
                break;

            case "delay":
            case "step_delay":
                if (int.TryParse(val, out int dms) && dms >= 0)
                    ctx.StepDelayOverrideMs = dms;
                break;

            case "ra":
            case "reuse_app":
                KeywordReuseApp(ctx, "", val);
                break;

            case "kao":
            case "keep_app_open":
                KeywordKeepAppOpen(ctx, "", val);
                break;
        }
    }
}

