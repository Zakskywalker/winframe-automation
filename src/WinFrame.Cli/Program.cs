using WinFrame.Core.Config;
using WinFrame.Core.Controls;
using WinFrame.Core.Keywords;
using WinFrame.Core.Logging;
using WinFrame.Core.Native;
using WinFrame.Core.Processes;
using WinFrame.Core.Service;
using WinFrame.Core.Uia;
using WinFrame.Core.Vision;

namespace WinFrame.Cli;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        NativeMethods.EnsureDpiAwareness();
        Console.WriteLine("=================================================================");
        Console.WriteLine(" WinFrame Automation CLI v1.0.0");
        Console.WriteLine(" Hand of Glory Games and Software | Author: Zak Millikin | GPLv3");
        Console.WriteLine("=================================================================");

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        string cmd = args[0].ToLowerInvariant();
        var engine = new KeywordFramework();

        switch (cmd)
        {
            case "run":
            {
                bool replayCv = args.Any(a => a.Equals("--replay-opencv", StringComparison.OrdinalIgnoreCase));
                var files = args.Skip(1).Where(a => !a.StartsWith("--")).ToList();
                var tests = new List<ITestExecutable>();
                foreach (var f in files) tests.AddRange(TestSheetLoader.LoadFromFile(f));
                if (tests.Count == 0)
                {
                    Console.WriteLine("No test steps found to run.");
                    return 1;
                }
                var qResult = replayCv
                    ? engine.ReplayAndRecordOpenCv(tests)
                    : engine.ExecuteQueue(tests, "CLI Queue");
                Console.WriteLine(qResult);
                if (!string.IsNullOrEmpty(qResult.ReportPath))
                    Console.WriteLine($"HTML Report: {qResult.ReportPath}");
                return qResult.Failed == 0 ? 0 : 2;
            }

            case "range":
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: WinFrame.Cli range <testSheet> <range e.g. 1-3 or 4-1> [--inverse]");
                    return 1;
                }
                string file = args[1];
                string rangeSpec = args[2];
                bool inverse = args.Any(a => a.Equals("--inverse", StringComparison.OrdinalIgnoreCase));
                var tests = TestSheetLoader.LoadFromFile(file);
                if (tests.Count == 0)
                {
                    Console.WriteLine($"No test found in '{file}'.");
                    return 1;
                }
                var results = engine.ExecuteStepRange(tests[0], rangeSpec, inverse);
                foreach (var r in results) Console.WriteLine(r);
                return results.All(r => r.IsSuccess) ? 0 : 2;
            }

            case "scrape":
            {
                string titleOrProc = args.ElementAtOrDefault(1) ?? "";
                IntPtr hwnd = string.IsNullOrWhiteSpace(titleOrProc)
                    ? NativeMethods.GetForegroundWindow()
                    : ProcessManagementEngine.FindTopLevelWindow(titleOrProc, titleOrProc);
                var list = UiaAutomation.ScrapeWindow(hwnd, engine.Controls);
                foreach (var c in list) engine.Controls.AddOrUpdate(c);
                engine.Controls.Save();
                Console.WriteLine($"Scraped and saved {list.Count} controls to {engine.Controls.Store.Description}.");
                return 0;
            }

            case "export-ids":
            {
                string outFile = args.ElementAtOrDefault(1) ?? "automation_ids.json";
                string json = engine.Controls.ExportAutomationIdsJson();
                File.WriteAllText(outFile, json);
                Console.WriteLine($"Exported {engine.Controls.Count} control Automation IDs to {Path.GetFullPath(outFile)}");
                return 0;
            }

            case "keywords":
            {
                foreach (var k in KeywordRegistry.All)
                    Console.WriteLine($"[{k.Category}] {k.Signature} - {k.Description}");
                return 0;
            }

            case "serve":
            {
                string url = args.ElementAtOrDefault(1) ?? ConfigManager.Current.Service.Urls;
                using var server = new WinFrameApiServer(engine, url);
                server.Start();
                Console.WriteLine($"Remote API Server running at {server.ListenUrl}. Press Enter to stop...");
                Console.ReadLine();
                return 0;
            }

            default:
                PrintUsage();
                return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Commands:
              WinFrame.Cli run <sheet1.csv|xlsx|json> [...] [--replay-opencv]
                  Executes one or more test sheets in a queue (add --replay-opencv to record OpenCV snapshots).

              WinFrame.Cli range <sheet> <start-end> [--inverse]
                  Executes a step range forward (e.g. 1-3, 3-4) or in reverse with rule-based inversion (e.g. 4-1 or --inverse).

              WinFrame.Cli scrape [windowTitleOrProcess]
                  Scrapes UIA + Win32 controls from a window into the control sheet.

              WinFrame.Cli export-ids [outputPath.json]
                  Exports all created control Automation IDs and locators to JSON.

              WinFrame.Cli keywords
                  Lists every registered Win32, UIA, OpenCV, Process, and Flow keyword.

              WinFrame.Cli serve [http://localhost:5880/]
                  Hosts the WinFrame REST API & remote step-queue service.
            """);
    }
}

