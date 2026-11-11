using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinFrame.Core.Controls;
using WinFrame.Core.Execution;

namespace WinFrame.Core.Config;

/// <summary>Resolves the WinFrame home folder (config, logs, reports, snapshots, control sheets).</summary>
public static class WinFramePaths
{
    private static string? root;

    /// <summary>
    /// Home folder. Priority:
    /// 1. WINFRAME_HOME env var (if writable)
    /// 2. Local "winframe_data" folder in the workspace/app directory (if writable)
    /// 3. %LOCALAPPDATA%\WinFrameStudio (if writable)
    /// 4. %TEMP%\WinFrameStudio fallback
    /// </summary>
    public static string Root
    {
        get
        {
            if (root != null) return root;

            var candidates = new List<string>();
            var env = Environment.GetEnvironmentVariable("WINFRAME_HOME");
            if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env);

            // Walk up from AppContext.BaseDirectory to see if we are inside the WinFrame repo
            string? walk = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(walk) && !File.Exists(Path.Combine(walk, "WinFrame.sln")))
            {
                walk = Path.GetDirectoryName(walk);
            }
            if (!string.IsNullOrEmpty(walk))
                candidates.Add(Path.Combine(walk, "winframe_data"));

            candidates.Add(Path.Combine(AppContext.BaseDirectory, "winframe_data"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinFrameStudio"));
            candidates.Add(Path.Combine(Path.GetTempPath(), "WinFrameStudio"));

            foreach (var candidate in candidates)
            {
                if (TryEnsureWritableDirectory(candidate))
                    return root = candidate;
            }

            return root = Path.GetTempPath();
        }
        set => root = value;
    }

    public static string ConfigDir => Ensure(Path.Combine(Root, "config"));
    public static string TestBedDir => Ensure(Path.Combine(ConfigDir, "testbeds"));
    public static string ThemeDir => Ensure(Path.Combine(ConfigDir, "themes"));
    public static string ConfigFile => Path.Combine(ConfigDir, "winframe.config.json");

    public static string Ensure(string dir)
    {
        try { Directory.CreateDirectory(dir); }
        catch { }
        return dir;
    }

    private static bool TryEnsureWritableDirectory(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string probeDir = Path.Combine(dir, "config", "testbeds");
            Directory.CreateDirectory(probeDir);
            string probeFile = Path.Combine(probeDir, ".write_probe_" + Environment.ProcessId);
            File.WriteAllText(probeFile, "ok");
            File.Delete(probeFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Resolves a path relative to <see cref="Root"/> unless already absolute.</summary>
    public static string Resolve(string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Root, path));
}

/// <summary>Where control-repository data lives.</summary>
public enum ControlStoreFormat { Json, Csv, Xlsx, MySql }

/// <summary>Test isolation strategy.</summary>
public enum IsolationMode
{
    /// <summary>Steps run on a guarded worker thread; exceptions/timeouts never reach the host.</summary>
    InProcess,
    /// <summary>Each test runs in a child WinFrame.Cli process; even native crashes cannot stop the host.</summary>
    OutOfProcess
}

/// <summary>Credentials. The password is DPAPI-encrypted for the current Windows user.</summary>
public sealed class CredentialSet
{
    public string Username { get; set; } = Environment.UserName;
    public string Domain { get; set; } = Environment.UserDomainName;

    /// <summary>Base64 DPAPI (CurrentUser) protected password. Never stored in plain text.</summary>
    public string ProtectedPassword { get; set; } = "";

    [JsonIgnore]
    public string Password
    {
        get => Unprotect(ProtectedPassword);
        set => ProtectedPassword = Protect(value);
    }

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Encoding.UTF8.GetBytes("WinFrame"), DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string protectedB64)
    {
        if (string.IsNullOrEmpty(protectedB64)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedB64), Encoding.UTF8.GetBytes("WinFrame"), DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return ""; }
    }
}

/// <summary>Control-sheet (object repository) storage settings.</summary>
public sealed class ControlStoreSettings
{
    public ControlStoreFormat Format { get; set; } = ControlStoreFormat.Json;
    /// <summary>File path for Json/Csv/Xlsx (relative to WinFrame home).</summary>
    public string Path { get; set; } = "controls/controls.json";
    /// <summary>MySQL connection string (Format = MySql).</summary>
    public string ConnectionString { get; set; } = "";
    public string MySqlTable { get; set; } = "winframe_controls";
    /// <summary>How control/window images are stored: file=, b=, c=, 7zc=.</summary>
    public ImageStorageMode ImageStorage { get; set; } = ImageStorageMode.File;
    /// <summary>Folder for File-mode images (relative to WinFrame home).</summary>
    public string ImageFolder { get; set; } = "controls/images";
}

/// <summary>Remote service / API host settings.</summary>
public sealed class ServiceSettings
{
    public bool Enabled { get; set; }
    public string Urls { get; set; } = "http://localhost:5880";
    /// <summary>Required in the X-Api-Key header for every API call. Generated on first run.</summary>
    public string ApiKey { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public bool AllowRemoteExecution { get; set; }
}

/// <summary>General application configuration (File → Settings → General / Config).</summary>
public sealed class AppConfig
{
    // ---- General ----
    public string Theme { get; set; } = "Dark";
    public bool ShowSplash { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool TrayNotifications { get; set; } = true;
    public bool ScrollTrayMessages { get; set; } = true;
    public string ReportTheme { get; set; } = "dark";

    // ---- Paths ----
    public string LogDirectory { get; set; } = "logs";
    public string ReportDirectory { get; set; } = "reports";
    public string SnapshotDirectory { get; set; } = "snapshots";
    public string TestDirectory { get; set; } = "tests";

    // ---- Execution ----
    public int DefaultTimeoutMs { get; set; } = 10000;
    public int StepDelayMs { get; set; } = 150;
    public IsolationMode Isolation { get; set; } = IsolationMode.InProcess;
    public FailurePolicy FailurePolicy { get; set; } = new();
    public bool CaptureScreenshotOnFailure { get; set; } = true;
    public bool CaptureControlImagesDuringRun { get; set; }
    public string? DefaultTestBed { get; set; }

    // ---- Win32 Control Outline Highlighting ----
    /// <summary>Set to "1" or "true" to enable Win32 GDI outline highlighting during test runs; "0", "", or null to turn off.</summary>
    public string? Highlight { get; set; } = "1";
    /// <summary>Default outline color (hex like #F59E0B or named color like Red, Lime, Cyan, Gold).</summary>
    public string HighlightColor { get; set; } = "#F59E0B";
    /// <summary>Default short interval in milliseconds to display the Win32 control outline.</summary>
    public int HighlightIntervalMs { get; set; } = 280;
    /// <summary>Pen width in pixels for the Win32 control outline.</summary>
    public int HighlightThickness { get; set; } = 3;

    [JsonIgnore]
    public bool IsHighlightEnabled => IsHighlightValueOn(Highlight);

    public static bool IsHighlightValueOn(string? val) =>
        !string.IsNullOrWhiteSpace(val) &&
        (val.Trim() == "1" || val.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || val.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));

    // ---- Base local data ----
    public CredentialSet Credentials { get; set; } = new();
    /// <summary>Base variables available to every test as ${name}.</summary>
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["machine"] = Environment.MachineName,
        ["user"] = Environment.UserName
    };

    // ---- Storage / service ----
    public ControlStoreSettings ControlStore { get; set; } = new();
    public ServiceSettings Service { get; set; } = new();

    // ---- Recorder ----
    public bool RecorderPreviewAndName { get; set; } = true;
    public bool RecorderCaptureImages { get; set; } = true;
    public int RecorderTypingFlushMs { get; set; } = 1500;

    [JsonIgnore] public string LogPath => WinFramePaths.Resolve(LogDirectory);
    [JsonIgnore] public string ReportPath => WinFramePaths.Resolve(ReportDirectory);
    [JsonIgnore] public string SnapshotPath => WinFramePaths.Resolve(SnapshotDirectory);
    [JsonIgnore] public string TestPath => WinFramePaths.Resolve(TestDirectory);
}

/// <summary>A test bed: the application under test and its environment. Stored separately in config/testbeds/*.json.</summary>
public sealed class TestBedConfig
{
    public string Name { get; set; } = "Calculator";
    public string Description { get; set; } = "";
    public string Machine { get; set; } = Environment.MachineName;
    public string ApplicationPath { get; set; } = "calc.exe";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string ProcessName { get; set; } = "CalculatorApp";
    /// <summary>Regex used to find/re-harness the main window.</summary>
    public string WindowTitlePattern { get; set; } = "Calculator";
    public int StartupTimeoutMs { get; set; } = 15000;
    public bool StartIfNotRunning { get; set; } = true;
    public bool CloseAfterRun { get; set; }
    /// <summary>Optional credential override for this bed.</summary>
    public CredentialSet? Credentials { get; set; }
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Loads/saves <see cref="AppConfig"/> and <see cref="TestBedConfig"/> files.</summary>
public static class ConfigManager
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static AppConfig? current;
    public static AppConfig Current => current ??= Load();

    public static event Action<AppConfig>? ConfigChanged;

    public static AppConfig Load(string? path = null)
    {
        path ??= WinFramePaths.ConfigFile;
        AppConfig cfg;
        if (File.Exists(path))
        {
            try { cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOptions) ?? new AppConfig(); }
            catch { cfg = new AppConfig(); }
        }
        else
        {
            cfg = new AppConfig();
            Save(cfg, path);
        }
        current = cfg;
        Logging.SystemLogger.LogDirectory = cfg.LogPath;
        return cfg;
    }

    private static readonly Dictionary<string, TestBedConfig> fallbackTestBeds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Calculator"] = new TestBedConfig
        {
            Name = "Calculator",
            Description = "Windows Calculator TestBed",
            ApplicationPath = "calc.exe",
            ProcessName = "CalculatorApp",
            WindowTitlePattern = "Calculator"
        },
        ["Notepad"] = new TestBedConfig
        {
            Name = "Notepad",
            Description = "Windows Notepad TestBed",
            ApplicationPath = "notepad.exe",
            ProcessName = "notepad",
            WindowTitlePattern = "Notepad"
        }
    };

    public static void Save(AppConfig cfg, string? path = null)
    {
        path ??= WinFramePaths.ConfigFile;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOptions));
        }
        catch (Exception ex)
        {
            Logging.SystemLogger.Log($"Could not save config to {path}: {ex.Message}", Logging.SystemLogger.LogLevel.WARNING);
        }
        current = cfg;
        Logging.SystemLogger.LogDirectory = cfg.LogPath;
        ConfigChanged?.Invoke(cfg);
    }

    public static IReadOnlyList<TestBedConfig> LoadTestBeds()
    {
        var list = new List<TestBedConfig>();
        try
        {
            var dir = WinFramePaths.TestBedDir;
            if (Directory.Exists(dir))
            {
                var files = Directory.GetFiles(dir, "*.json");
                if (files.Length == 0)
                {
                    foreach (var def in fallbackTestBeds.Values)
                        SaveTestBed(def);
                    files = Directory.GetFiles(dir, "*.json");
                }
                foreach (var f in files)
                {
                    try
                    {
                        var tb = JsonSerializer.Deserialize<TestBedConfig>(File.ReadAllText(f), JsonOptions);
                        if (tb != null)
                        {
                            list.Add(tb);
                            fallbackTestBeds[tb.Name] = tb;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.SystemLogger.Log($"Bad test bed file {f}: {ex.Message}", Logging.SystemLogger.LogLevel.WARNING);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SystemLogger.Log($"LoadTestBeds fallback activated: {ex.Message}", Logging.SystemLogger.LogLevel.WARNING);
        }

        if (list.Count == 0)
            return fallbackTestBeds.Values.ToList();

        return list;
    }

    public static TestBedConfig? GetTestBed(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : LoadTestBeds().FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static void SaveTestBed(TestBedConfig bed)
    {
        fallbackTestBeds[bed.Name] = bed;
        try
        {
            var dir = WinFramePaths.TestBedDir;
            Directory.CreateDirectory(dir);
            var safe = string.Concat(bed.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            File.WriteAllText(Path.Combine(dir, safe + ".json"), JsonSerializer.Serialize(bed, JsonOptions));
        }
        catch (Exception ex)
        {
            Logging.SystemLogger.Log($"Saved TestBed '{bed.Name}' in memory (disk write skipped: {ex.Message})", Logging.SystemLogger.LogLevel.WARNING);
        }
    }
}

