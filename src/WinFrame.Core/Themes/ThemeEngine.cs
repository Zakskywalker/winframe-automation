using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using WinFrame.Core.Config;

namespace WinFrame.Core.Themes;

/// <summary>Per-control color/font override keyed by the control's Name.</summary>
public sealed class ControlThemeOverride
{
    public string? BackColorHex { get; set; }
    public string? ForeColorHex { get; set; }
    public bool? Bold { get; set; }
}

/// <summary>
/// Overall theme definition + per-control custom overrides (harnessing the control names).
/// Can be saved/loaded as JSON in <c>config/themes/*.json</c> and applied to any WinForms <see cref="Control"/> tree.
/// </summary>
public sealed class ThemeProfile
{
    public string Name { get; set; } = "Dark";
    public string BackgroundHex { get; set; } = "#0F141C";
    public string SurfaceHex { get; set; } = "#18202C";
    public string AccentHex { get; set; } = "#F59E0B";
    public string TextHex { get; set; } = "#E6EDF3";
    public string MutedTextHex { get; set; } = "#8B949E";
    public string BorderHex { get; set; } = "#2D3748";

    /// <summary>Per-control overrides keyed by Control.Name (e.g. "btnRunQueue", "gridSteps").</summary>
    public Dictionary<string, ControlThemeOverride> ControlOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Color Background => ColorTranslator.FromHtml(BackgroundHex);
    public Color Surface => ColorTranslator.FromHtml(SurfaceHex);
    public Color Accent => ColorTranslator.FromHtml(AccentHex);
    public Color Text => ColorTranslator.FromHtml(TextHex);
    public Color MutedText => ColorTranslator.FromHtml(MutedTextHex);
    public Color Border => ColorTranslator.FromHtml(BorderHex);
}

public static class ThemeEngine
{
    public static readonly IReadOnlyList<ThemeProfile> BuiltInThemes = new[]
    {
        new ThemeProfile
        {
            Name = "Dark",
            BackgroundHex = "#0F141C", SurfaceHex = "#18202C", AccentHex = "#F59E0B",
            TextHex = "#E6EDF3", MutedTextHex = "#8B949E", BorderHex = "#2D3748"
        },
        new ThemeProfile
        {
            Name = "HandOfGlory",
            BackgroundHex = "#110D13", SurfaceHex = "#1E1622", AccentHex = "#F59E0B",
            TextHex = "#F5E9D3", MutedTextHex = "#A794B2", BorderHex = "#3A2A42"
        },
        new ThemeProfile
        {
            Name = "Light",
            BackgroundHex = "#F8FAFC", SurfaceHex = "#FFFFFF", AccentHex = "#D97706",
            TextHex = "#0F172A", MutedTextHex = "#475569", BorderHex = "#CBD5E1"
        },
        new ThemeProfile
        {
            Name = "CyberEmerald",
            BackgroundHex = "#071714", SurfaceHex = "#0E2621", AccentHex = "#10B981",
            TextHex = "#ECFDF5", MutedTextHex = "#6EE7B7", BorderHex = "#1F4B40"
        }
    };

    public static ThemeProfile GetTheme(string name)
    {
        var customPath = Path.Combine(WinFramePaths.ThemeDir, $"{name}.json");
        if (File.Exists(customPath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<ThemeProfile>(File.ReadAllText(customPath), ConfigManager.JsonOptions);
                if (loaded != null) return loaded;
            }
            catch { }
        }
        return BuiltInThemes.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
               ?? BuiltInThemes[0];
    }

    public static void SaveCustomTheme(ThemeProfile profile)
    {
        var path = Path.Combine(WinFramePaths.ThemeDir, $"{profile.Name}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(profile, ConfigManager.JsonOptions));
    }

    /// <summary>
    /// Recursively applies <paramref name="theme"/> to <paramref name="root"/> and all child controls,
    /// honoring any per-control <see cref="ThemeProfile.ControlOverrides"/> keyed by <see cref="Control.Name"/>.
    /// </summary>
    public static void Apply(Control root, ThemeProfile theme)
    {
        void Visit(Control c)
        {
            if (c is Form or Panel or SplitContainer or TabPage or UserControl)
            {
                c.BackColor = theme.Background;
                c.ForeColor = theme.Text;
            }
            else if (c is Button btn)
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = theme.Border;
                btn.BackColor = btn.Tag as string == "accent" ? theme.Accent : theme.Surface;
                btn.ForeColor = btn.Tag as string == "accent" ? Color.Black : theme.Text;
            }
            else if (c is TextBox or RichTextBox or ListBox or ComboBox or NumericUpDown)
            {
                c.BackColor = theme.Surface;
                c.ForeColor = theme.Text;
            }
            else if (c is DataGridView dgv)
            {
                dgv.BackgroundColor = theme.Background;
                dgv.GridColor = theme.Border;
                dgv.EnableHeadersVisualStyles = false;
                dgv.ColumnHeadersDefaultCellStyle.BackColor = theme.Surface;
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = theme.Accent;
                dgv.DefaultCellStyle.BackColor = theme.Surface;
                dgv.DefaultCellStyle.ForeColor = theme.Text;
                dgv.DefaultCellStyle.SelectionBackColor = theme.Accent;
                dgv.DefaultCellStyle.SelectionForeColor = Color.Black;
            }
            else if (c is MenuStrip or ToolStrip or StatusStrip)
            {
                c.BackColor = theme.Surface;
                c.ForeColor = theme.Text;
            }
            else
            {
                c.ForeColor = theme.Text;
            }

            // Apply per-control custom override by Control.Name
            if (!string.IsNullOrWhiteSpace(c.Name) &&
                theme.ControlOverrides.TryGetValue(c.Name, out var ov))
            {
                if (!string.IsNullOrWhiteSpace(ov.BackColorHex))
                    c.BackColor = ColorTranslator.FromHtml(ov.BackColorHex);
                if (!string.IsNullOrWhiteSpace(ov.ForeColorHex))
                    c.ForeColor = ColorTranslator.FromHtml(ov.ForeColorHex);
                if (ov.Bold is true && !c.Font.Bold)
                    c.Font = new Font(c.Font, FontStyle.Bold);
            }

            foreach (Control child in c.Controls) Visit(child);
        }

        Visit(root);
    }

    /// <summary>Collects all named controls in a WinForms tree along with their AutomationId/AccessibleName.</summary>
    public static Dictionary<string, string> HarvestFormControlNames(Control root)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Walk(Control c)
        {
            if (!string.IsNullOrWhiteSpace(c.Name))
            {
                if (string.IsNullOrWhiteSpace(c.AccessibleName)) c.AccessibleName = c.Name;
                map[c.Name] = $"{c.GetType().Name} (Text='{c.Text}', AccessibleName='{c.AccessibleName}')";
            }
            foreach (Control child in c.Controls) Walk(child);
        }
        Walk(root);
        return map;
    }
}
