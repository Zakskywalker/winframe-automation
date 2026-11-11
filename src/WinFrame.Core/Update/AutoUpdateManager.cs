using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using WinFrame.Core.Logging;

namespace WinFrame.Core.Update;

public sealed record UpdateManifest(
    string Version,
    string SetupUrl,
    string InstallerExeUrl,
    string ReleaseNotes,
    DateTime PublishedAt);

/// <summary>
/// Auto-Updater for WinFrame Automation.
/// Checks <c>version.json</c> hosted on GitHub Pages and launches the ClickOnce / GitHub Pages
/// update URL via the Windows Shell (avoiding raw temp-EXE dropper patterns that trigger AV heuristics).
/// </summary>
public static class AutoUpdateManager
{
    public const string DefaultPagesBaseUrl = "https://zakskywalker.github.io/winframe-automation/";
    public const string DefaultManifestUrl = DefaultPagesBaseUrl + "version.json";
    public const string DefaultSetupUrl = DefaultPagesBaseUrl + "setup.exe";

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>
    /// Checks GitHub Pages (<c>version.json</c>) for a newer version of WinFrame.
    /// Returns the <see cref="UpdateManifest"/> if a newer version is available, or null if up to date / unreachable.
    /// </summary>
    public static async Task<(bool IsNewer, UpdateManifest? Manifest)> CheckForUpdateAsync(string? manifestUrl = null)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            string json = await http.GetStringAsync(manifestUrl ?? DefaultManifestUrl).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
                return (false, null);

            bool newer = IsVersionNewer(CurrentVersion, manifest.Version);
            SystemLogger.Log($"AutoUpdate check: current={CurrentVersion}, remote={manifest.Version}, newer={newer}");
            return (newer, manifest);
        }
        catch (Exception ex)
        {
            SystemLogger.Log($"AutoUpdate check skipped/offline: {ex.Message}", SystemLogger.LogLevel.DEBUG);
            return (false, null);
        }
    }

    /// <summary>Opens the ClickOnce / GitHub Pages update URL via the system shell / browser.</summary>
    public static Task<bool> LaunchUpdateUrlAsync(string? installerUrl = null)
    {
        try
        {
            string url = string.IsNullOrWhiteSpace(installerUrl) ? DefaultPagesBaseUrl : installerUrl;
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(false);
            }

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            SystemLogger.Log($"AutoUpdate launch failed: {ex.Message}", SystemLogger.LogLevel.WARNING);
            return Task.FromResult(false);
        }
    }

    public static bool IsVersionNewer(string currentVersion, string remoteVersion)
    {
        if (Version.TryParse(NormalizeVersion(currentVersion), out var cur) &&
            Version.TryParse(NormalizeVersion(remoteVersion), out var rem))
        {
            return rem > cur;
        }
        return !string.Equals(currentVersion.Trim(), remoteVersion.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string v)
    {
        v = v.Trim().TrimStart('v', 'V');
        int dash = v.IndexOf('-');
        return dash > 0 ? v[..dash] : v;
    }
}
