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
/// Supports both ClickOnce foreground updates (from GitHub Pages <c>setup.exe</c> / <c>.application</c> manifest)
/// and standalone self-update checks against <c>version.json</c> hosted on GitHub Pages.
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

    /// <summary>Downloads and launches the latest <c>setup.exe</c> or <c>WinFrame.Installer.exe</c> from GitHub Pages.</summary>
    public static async Task<bool> DownloadAndRunInstallerAsync(string? installerUrl = null)
    {
        try
        {
            string url = string.IsNullOrWhiteSpace(installerUrl) ? DefaultSetupUrl : installerUrl;
            string tempExe = Path.Combine(Path.GetTempPath(), $"WinFrame_Update_{DateTime.Now:yyyyMMdd_HHmmss}.exe");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            byte[] bytes = await http.GetByteArrayAsync(url).ConfigureAwait(false);
            await File.WriteAllBytesAsync(tempExe, bytes).ConfigureAwait(false);

            Process.Start(new ProcessStartInfo(tempExe) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            SystemLogger.Log($"AutoUpdate download failed: {ex.Message}", SystemLogger.LogLevel.WARNING);
            return false;
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
