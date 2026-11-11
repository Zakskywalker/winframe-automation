using System.Text;
using System.Text.Json;
using WinFrame.Core.Config;
using WinFrame.Core.Controls;

namespace WinFrame.Core.Keywords;

/// <summary>Loads and saves <see cref="TestCase"/> sheets in CSV/TXT, Excel (.xlsx), or JSON format.</summary>
public static class TestSheetLoader
{
    public static List<TestCase> LoadFromFile(string path)
    {
        if (!File.Exists(path)) return new List<TestCase>();
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".xlsx" => LoadXlsx(path),
            ".json" => LoadJson(path),
            _ => new List<TestCase> { LoadCsv(path) }
        };
    }

    public static TestCase LoadCsv(string path)
    {
        var tc = new TestCase(Path.GetFileNameWithoutExtension(path)) { SourcePath = path };
        var rows = Csv.Parse(File.ReadAllText(path)).ToList();
        int startRow = 0;
        if (rows.Count > 0 && rows[0].Count > 0 &&
            rows[0][0].Trim().Equals("Action", StringComparison.OrdinalIgnoreCase))
        {
            startRow = 1;
        }

        for (int i = startRow; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Count == 0 || string.IsNullOrWhiteSpace(r[0]) || r[0].TrimStart().StartsWith('#')) continue;
            tc.Steps.Add(new TestStep
            {
                Index = tc.Steps.Count + 1,
                SourceLine = i + 1,
                Action = r[0].Trim().ToUpperInvariant(),
                Target = r.ElementAtOrDefault(1)?.Trim() ?? "",
                Value = r.ElementAtOrDefault(2)?.Trim() ?? "",
                Options = TestStep.ParseOptions(r.ElementAtOrDefault(3)),
                Description = r.ElementAtOrDefault(4)?.Trim() ?? ""
            });
        }
        return tc;
    }

    public static void SaveCsv(TestCase test, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var sb = new StringBuilder();
        sb.AppendLine("Action,Target,Value,Options,Description");
        foreach (var s in test.Steps)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                s.Action, s.Target, s.Value, s.OptionsToString(), s.Description
            }.Select(Csv.Escape)));
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static List<TestCase> LoadXlsx(string path)
    {
        var list = new List<TestCase>();
        using var wb = new ClosedXML.Excel.XLWorkbook(path);
        foreach (var ws in wb.Worksheets)
        {
            if (ws.Name.Equals("Controls", StringComparison.OrdinalIgnoreCase)) continue;
            var tc = new TestCase(ws.Name) { SourcePath = path };
            foreach (var row in ws.RowsUsed().Skip(1))
            {
                string action = row.Cell(1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(action) || action.StartsWith('#')) continue;
                tc.Steps.Add(new TestStep
                {
                    Index = tc.Steps.Count + 1,
                    SourceLine = row.RowNumber(),
                    Action = action.ToUpperInvariant(),
                    Target = row.Cell(2).GetString().Trim(),
                    Value = row.Cell(3).GetString().Trim(),
                    Options = TestStep.ParseOptions(row.Cell(4).GetString()),
                    Description = row.Cell(5).GetString().Trim()
                });
            }
            if (tc.Steps.Count > 0) list.Add(tc);
        }
        return list;
    }

    public static void SaveXlsx(IEnumerable<TestCase> tests, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var wb = new ClosedXML.Excel.XLWorkbook();
        foreach (var tc in tests)
        {
            var sheetName = string.IsNullOrWhiteSpace(tc.Name) ? "Test" : tc.Name[..Math.Min(31, tc.Name.Length)];
            var ws = wb.Worksheets.Add(sheetName);
            ws.Cell(1, 1).Value = "Action";
            ws.Cell(1, 2).Value = "Target";
            ws.Cell(1, 3).Value = "Value";
            ws.Cell(1, 4).Value = "Options";
            ws.Cell(1, 5).Value = "Description";
            ws.Row(1).Style.Font.Bold = true;

            int r = 2;
            foreach (var s in tc.Steps)
            {
                ws.Cell(r, 1).Value = s.Action;
                ws.Cell(r, 2).Value = s.Target;
                ws.Cell(r, 3).Value = s.Value;
                ws.Cell(r, 4).Value = s.OptionsToString();
                ws.Cell(r, 5).Value = s.Description;
                r++;
            }
        }
        wb.SaveAs(path);
    }

    public static List<TestCase> LoadJson(string path)
    {
        string raw = File.ReadAllText(path).Trim();
        if (raw.StartsWith('['))
        {
            var list = JsonSerializer.Deserialize<List<TestCase>>(raw, ConfigManager.JsonOptions) ?? new();
            foreach (var t in list) t.SourcePath = path;
            return list;
        }
        var single = JsonSerializer.Deserialize<TestCase>(raw, ConfigManager.JsonOptions);
        if (single != null) { single.SourcePath = path; return new List<TestCase> { single }; }
        return new List<TestCase>();
    }

    public static void SaveJson(TestCase test, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(test, ConfigManager.JsonOptions));
    }
}
