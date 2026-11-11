using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinFrame.Core.Config;
using WinFrame.Core.Logging;

namespace WinFrame.Core.Controls;

/// <summary>
/// A named control in the control sheet (object repository):
/// <c>controlname =&gt; every captured attribute</c>, plus an optional control image and window image.
/// </summary>
public sealed class ControlDefinition
{
    /// <summary>Well-known attribute keys used for locating a control.</summary>
    public static class Keys
    {
        public const string AutomationId = "AutomationId";
        public const string Name = "Name";
        public const string ControlType = "ControlType";
        public const string LocalizedControlType = "LocalizedControlType";
        public const string ClassName = "ClassName";
        public const string FrameworkId = "FrameworkId";
        public const string ProcessName = "ProcessName";
        public const string WindowTitle = "WindowTitle";
        public const string BoundingRectangle = "BoundingRectangle";
        public const string RelativeBounds = "RelativeBounds";
        public const string NativeWindowHandle = "NativeWindowHandle";
        public const string TreePath = "TreePath";
        public const string Index = "Index";
        public const string Patterns = "Patterns";
        public const string Win32CtrlId = "Win32.CtrlId";
    }

    public string Name { get; set; } = "";

    /// <summary>Every captured attribute (UIA properties, Win32 data, geometry, patterns, tree path…).</summary>
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Encoded control image (file= / b= / c= / 7zc=).</summary>
    public string? Image { get; set; }

    /// <summary>Encoded image of the whole application window at capture time.</summary>
    public string? WindowImage { get; set; }

    public DateTime Updated { get; set; } = DateTime.Now;

    public string Get(string key) => Attributes.TryGetValue(key, out var v) ? v : "";

    /// <summary>Builds a locator string (<c>id=..;name=..;type=..;class=..;window=..</c>) for UI Automation lookup.</summary>
    public string ToLocator()
    {
        var parts = new List<string>();
        void Add(string k, string attr) { var v = Get(attr); if (!string.IsNullOrWhiteSpace(v)) parts.Add($"{k}={v.Replace(";", "\\;")}"); }
        Add("id", Keys.AutomationId);
        if (string.IsNullOrWhiteSpace(Get(Keys.AutomationId))) Add("name", Keys.Name);
        Add("type", Keys.ControlType);
        Add("class", Keys.ClassName);
        Add("window", Keys.WindowTitle);
        if (string.IsNullOrWhiteSpace(Get(Keys.AutomationId)) && string.IsNullOrWhiteSpace(Get(Keys.Name))) Add("path", Keys.TreePath);
        return string.Join(";", parts);
    }

    /// <summary>Doc-style attribute block: one <c>Key: Value</c> per line.</summary>
    public string AttributesToDoc() => string.Join("\n", Attributes.Select(kv => $"{kv.Key}: {kv.Value.Replace("\r", " ").Replace("\n", " ")}"));

    public static Dictionary<string, string> AttributesFromDoc(string doc)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in doc.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            int i = l.IndexOf(": ", StringComparison.Ordinal);
            if (i > 0) d[l[..i].Trim()] = l[(i + 2)..];
        }
        return d;
    }

    public ControlDefinition Clone() => new()
    {
        Name = Name, Image = Image, WindowImage = WindowImage, Updated = Updated,
        Attributes = new Dictionary<string, string>(Attributes, StringComparer.OrdinalIgnoreCase)
    };
}

/// <summary>Persistence back-end for the control sheet.</summary>
public interface IControlStore
{
    string Description { get; }
    IEnumerable<ControlDefinition> Load();
    void Save(IEnumerable<ControlDefinition> controls);
}

/// <summary>
/// In-memory control sheet backed by a pluggable <see cref="IControlStore"/> (JSON, CSV, XLSX or MySQL).
/// Thread-safe. Provides default naming (<c>controltype_n</c>), matching of live controls to existing
/// entries, image storage and export of all automation ids.
/// </summary>
public sealed class ControlRepository
{
    private readonly object sync = new();
    private readonly Dictionary<string, ControlDefinition> controls = new(StringComparer.OrdinalIgnoreCase);

    public ControlRepository(IControlStore? store = null, ControlStoreSettings? settings = null)
    {
        Settings = settings ?? ConfigManager.Current.ControlStore;
        Store = store ?? CreateStore(Settings);
    }

    public IControlStore Store { get; set; }
    public ControlStoreSettings Settings { get; }
    public string ImageFolder => WinFramePaths.Resolve(Settings.ImageFolder);

    public event Action<ControlDefinition>? ControlChanged;

    public static IControlStore CreateStore(ControlStoreSettings s) => s.Format switch
    {
        ControlStoreFormat.Csv => new CsvControlStore(WinFramePaths.Resolve(s.Path)),
        ControlStoreFormat.Xlsx => new XlsxControlStore(WinFramePaths.Resolve(s.Path)),
        ControlStoreFormat.MySql => new MySqlControlStore(s.ConnectionString, s.MySqlTable),
        _ => new JsonControlStore(WinFramePaths.Resolve(s.Path))
    };

    public IReadOnlyList<ControlDefinition> All { get { lock (sync) return controls.Values.ToList(); } }
    public int Count { get { lock (sync) return controls.Count; } }

    public ControlRepository Load()
    {
        try
        {
            var loaded = Store.Load().ToList();
            lock (sync)
            {
                controls.Clear();
                foreach (var c in loaded) controls[c.Name] = c;
            }
            SystemLogger.Log($"Loaded {loaded.Count} controls from {Store.Description}");
        }
        catch (Exception ex)
        {
            SystemLogger.Log($"Control store load failed ({Store.Description}): {ex.Message}", SystemLogger.LogLevel.WARNING);
        }
        return this;
    }

    public void Save()
    {
        Store.Save(All);
        SystemLogger.Log($"Saved {Count} controls to {Store.Description}");
    }

    /// <summary>Saves to a different store (export / format conversion) without changing the active one.</summary>
    public void SaveAs(IControlStore target) => target.Save(All);

    public bool TryGet(string name, out ControlDefinition def)
    {
        lock (sync) return controls.TryGetValue(name.TrimStart('@'), out def!);
    }

    public ControlDefinition? Get(string name) => TryGet(name, out var d) ? d : null;

    public void AddOrUpdate(ControlDefinition def)
    {
        def.Updated = DateTime.Now;
        lock (sync) controls[def.Name] = def;
        ControlChanged?.Invoke(def);
    }

    public bool Remove(string name) { lock (sync) return controls.Remove(name); }

    public bool Rename(string oldName, string newName)
    {
        lock (sync)
        {
            if (!controls.TryGetValue(oldName, out var def) || controls.ContainsKey(newName)) return false;
            controls.Remove(oldName);
            def.Name = newName;
            controls[newName] = def;
        }
        return true;
    }

    /// <summary>Generates <c>{controltype}_{n}</c> (e.g. <c>button_3</c>) unique within the repository.</summary>
    public string GenerateDefaultName(string controlType, ISet<string>? reserved = null)
    {
        var baseName = new string((string.IsNullOrWhiteSpace(controlType) ? "control" : controlType)
            .Replace("ControlType.", "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (baseName.Length == 0) baseName = "control";
        lock (sync)
        {
            for (int n = 1; ; n++)
            {
                var candidate = $"{baseName}_{n}";
                if (!controls.ContainsKey(candidate) && (reserved == null || !reserved.Contains(candidate))) return candidate;
            }
        }
    }

    /// <summary>
    /// Finds an existing entry describing the same live control. Matches on AutomationId + ControlType + window
    /// when an id exists, else on Name + ControlType + window, else on tree path.
    /// </summary>
    public ControlDefinition? FindMatch(IReadOnlyDictionary<string, string> attrs)
    {
        string A(IReadOnlyDictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : "";
        var id = A(attrs, ControlDefinition.Keys.AutomationId);
        var name = A(attrs, ControlDefinition.Keys.Name);
        var type = A(attrs, ControlDefinition.Keys.ControlType);
        var win = A(attrs, ControlDefinition.Keys.WindowTitle);
        var path = A(attrs, ControlDefinition.Keys.TreePath);
        lock (sync)
        {
            foreach (var c in controls.Values)
            {
                if (!string.Equals(c.Get(ControlDefinition.Keys.ControlType), type, StringComparison.OrdinalIgnoreCase)) continue;
                bool sameWin = string.IsNullOrEmpty(win) || string.Equals(c.Get(ControlDefinition.Keys.WindowTitle), win, StringComparison.OrdinalIgnoreCase);
                if (!sameWin) continue;
                if (!string.IsNullOrEmpty(id)) { if (c.Get(ControlDefinition.Keys.AutomationId) == id) return c; continue; }
                if (!string.IsNullOrEmpty(name) && c.Get(ControlDefinition.Keys.Name) == name) return c;
                if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path) && c.Get(ControlDefinition.Keys.TreePath) == path) return c;
            }
        }
        return null;
    }

    /// <summary>Stores a control image (and optionally the window image) using the configured storage mode.</summary>
    public void SetImages(ControlDefinition def, byte[]? controlPng, byte[]? windowPng, ImageStorageMode? mode = null)
    {
        var m = mode ?? Settings.ImageStorage;
        var safe = string.Concat(def.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (controlPng != null) def.Image = ImageCodec.Encode(controlPng, m, ImageFolder, safe + ".png");
        if (windowPng != null) def.WindowImage = ImageCodec.Encode(windowPng, m, ImageFolder, safe + "_window.png");
        AddOrUpdate(def);
    }

    public byte[]? GetImage(ControlDefinition def) => ImageCodec.Decode(def.Image, WinFramePaths.Root);
    public byte[]? GetWindowImage(ControlDefinition def) => ImageCodec.Decode(def.WindowImage, WinFramePaths.Root);

    /// <summary>JSON key of every automation id / locator known to the repository.</summary>
    public string ExportAutomationIdsJson()
    {
        var data = All.OrderBy(c => c.Name).Select(c => new
        {
            name = c.Name,
            automationId = c.Get(ControlDefinition.Keys.AutomationId),
            controlType = c.Get(ControlDefinition.Keys.ControlType),
            uiaName = c.Get(ControlDefinition.Keys.Name),
            window = c.Get(ControlDefinition.Keys.WindowTitle),
            locator = c.ToLocator()
        });
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }
}

// ------------------------------------------------------------------------------------------------
// Stores
// ------------------------------------------------------------------------------------------------

/// <summary><c>{ "controlName": { "attributes": {...}, "image": "c=...", "windowImage": "file=..." } }</c></summary>
public sealed class JsonControlStore(string path) : IControlStore
{
    public string Description => $"JSON {path}";

    private sealed class Entry
    {
        public Dictionary<string, string> Attributes { get; set; } = new();
        public string? Image { get; set; }
        public string? WindowImage { get; set; }
        public DateTime Updated { get; set; }
    }

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public IEnumerable<ControlDefinition> Load()
    {
        if (!File.Exists(path)) return Array.Empty<ControlDefinition>();
        string raw = File.ReadAllText(path).Trim();
        if (raw.StartsWith('['))
        {
            return JsonSerializer.Deserialize<List<ControlDefinition>>(raw, Opts) ?? new List<ControlDefinition>();
        }
        var map = JsonSerializer.Deserialize<Dictionary<string, Entry>>(raw, Opts) ?? new();
        return map.Select(kv => new ControlDefinition
        {
            Name = kv.Key,
            Attributes = new Dictionary<string, string>(kv.Value.Attributes, StringComparer.OrdinalIgnoreCase),
            Image = kv.Value.Image, WindowImage = kv.Value.WindowImage, Updated = kv.Value.Updated
        }).ToList();
    }

    public void Save(IEnumerable<ControlDefinition> controls)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var map = controls.OrderBy(c => c.Name).ToDictionary(c => c.Name, c => new Entry
        {
            Attributes = c.Attributes.ToDictionary(k => k.Key, k => k.Value), Image = c.Image, WindowImage = c.WindowImage, Updated = c.Updated
        });
        File.WriteAllText(path, JsonSerializer.Serialize(map, Opts));
    }
}

/// <summary>CSV: <c>Name,Attributes,Image,WindowImage,Updated</c> where Attributes is a doc-style multi-line block.</summary>
public sealed class CsvControlStore(string path) : IControlStore
{
    public string Description => $"CSV {path}";

    public IEnumerable<ControlDefinition> Load()
    {
        if (!File.Exists(path)) return Array.Empty<ControlDefinition>();
        var rows = Csv.Parse(File.ReadAllText(path)).ToList();
        return rows.Skip(1).Where(r => r.Count > 0 && !string.IsNullOrWhiteSpace(r[0])).Select(r => new ControlDefinition
        {
            Name = r[0],
            Attributes = ControlDefinition.AttributesFromDoc(r.ElementAtOrDefault(1) ?? ""),
            Image = NullIfEmpty(r.ElementAtOrDefault(2)),
            WindowImage = NullIfEmpty(r.ElementAtOrDefault(3)),
            Updated = DateTime.TryParse(r.ElementAtOrDefault(4), out var d) ? d : DateTime.Now
        }).ToList();
    }

    public void Save(IEnumerable<ControlDefinition> controls)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var sb = new StringBuilder();
        sb.AppendLine("Name,Attributes,Image,WindowImage,Updated");
        foreach (var c in controls.OrderBy(c => c.Name))
            sb.AppendLine(string.Join(",", new[] { c.Name, c.AttributesToDoc(), c.Image ?? "", c.WindowImage ?? "", c.Updated.ToString("o") }.Select(Csv.Escape)));
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>XLSX: one row per control; Name, Image, WindowImage, Updated, then one column per attribute key.</summary>
public sealed class XlsxControlStore(string path) : IControlStore
{
    public string Description => $"XLSX {path}";
    private const int FixedCols = 4;

    public IEnumerable<ControlDefinition> Load()
    {
        if (!File.Exists(path)) return Array.Empty<ControlDefinition>();
        using var wb = new ClosedXML.Excel.XLWorkbook(path);
        var ws = wb.Worksheets.First();
        var header = ws.Row(1).CellsUsed().ToDictionary(c => c.Address.ColumnNumber, c => c.GetString());
        var list = new List<ControlDefinition>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var name = row.Cell(1).GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            var def = new ControlDefinition
            {
                Name = name,
                Image = NullIfEmpty(row.Cell(2).GetString()),
                WindowImage = NullIfEmpty(row.Cell(3).GetString()),
                Updated = DateTime.TryParse(row.Cell(4).GetString(), out var d) ? d : DateTime.Now
            };
            foreach (var (col, key) in header.Where(h => h.Key > FixedCols))
            {
                var v = row.Cell(col).GetString();
                if (!string.IsNullOrEmpty(v)) def.Attributes[key] = v;
            }
            list.Add(def);
        }
        return list;
    }

    public void Save(IEnumerable<ControlDefinition> controls)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var items = controls.OrderBy(c => c.Name).ToList();
        var keys = items.SelectMany(c => c.Attributes.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("Controls");
        string[] fixedHeaders = { "Name", "Image", "WindowImage", "Updated" };
        for (int i = 0; i < fixedHeaders.Length; i++) ws.Cell(1, i + 1).Value = fixedHeaders[i];
        for (int i = 0; i < keys.Count; i++) ws.Cell(1, FixedCols + 1 + i).Value = keys[i];
        int r = 2;
        foreach (var c in items)
        {
            ws.Cell(r, 1).Value = c.Name;
            // Excel cells are limited to 32767 chars; large inline images fall back to the file reference.
            ws.Cell(r, 2).Value = Clip(c.Image);
            ws.Cell(r, 3).Value = Clip(c.WindowImage);
            ws.Cell(r, 4).Value = c.Updated.ToString("o");
            for (int i = 0; i < keys.Count; i++)
                if (c.Attributes.TryGetValue(keys[i], out var v)) ws.Cell(r, FixedCols + 1 + i).Value = v;
            r++;
        }
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);
        wb.SaveAs(path);
    }

    private static string Clip(string? s) => s == null ? "" : s.Length > 32000 ? "[image too large for cell – use file/c=/7zc= in JSON/MySQL]" : s;
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>MySQL: table (name PK, attributes JSON text, image LONGTEXT, window_image LONGTEXT, updated DATETIME).</summary>
public sealed class MySqlControlStore(string connectionString, string table) : IControlStore
{
    public string Description => $"MySQL table {table}";
    private string T => "`" + table.Replace("`", "") + "`";

    private MySqlConnector.MySqlConnection Open()
    {
        var con = new MySqlConnector.MySqlConnection(connectionString);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {T} (name VARCHAR(255) PRIMARY KEY, attributes LONGTEXT, image LONGTEXT, window_image LONGTEXT, updated DATETIME)";
        cmd.ExecuteNonQuery();
        return con;
    }

    public IEnumerable<ControlDefinition> Load()
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT name, attributes, image, window_image, updated FROM {T}";
        using var rd = cmd.ExecuteReader();
        var list = new List<ControlDefinition>();
        while (rd.Read())
        {
            list.Add(new ControlDefinition
            {
                Name = rd.GetString(0),
                Attributes = new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(rd.IsDBNull(1) ? "{}" : rd.GetString(1)) ?? new(), StringComparer.OrdinalIgnoreCase),
                Image = rd.IsDBNull(2) ? null : rd.GetString(2),
                WindowImage = rd.IsDBNull(3) ? null : rd.GetString(3),
                Updated = rd.IsDBNull(4) ? DateTime.Now : rd.GetDateTime(4)
            });
        }
        return list;
    }

    public void Save(IEnumerable<ControlDefinition> controls)
    {
        using var con = Open();
        using var tx = con.BeginTransaction();
        foreach (var c in controls)
        {
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"REPLACE INTO {T} (name, attributes, image, window_image, updated) VALUES (@n, @a, @i, @w, @u)";
            cmd.Parameters.AddWithValue("@n", c.Name);
            cmd.Parameters.AddWithValue("@a", JsonSerializer.Serialize(c.Attributes));
            cmd.Parameters.AddWithValue("@i", (object?)c.Image ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@w", (object?)c.WindowImage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@u", c.Updated);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}

