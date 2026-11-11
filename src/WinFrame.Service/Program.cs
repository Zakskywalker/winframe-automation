using WinFrame.Core.Config;
using WinFrame.Core.Keywords;
using WinFrame.Core.Native;
using WinFrame.Core.Processes;
using WinFrame.Core.Service;

namespace WinFrame.Service;

public static class Program
{
    public static async Task Main(string[] args)
    {
        NativeMethods.EnsureDpiAwareness();
        string url = args.FirstOrDefault() ?? ConfigManager.Current.Service.Urls;

        ProcessManagementEngine.Shared.StartWatchdog();
        using var server = new WinFrameApiServer(new KeywordFramework(), url);
        server.Start();

        Console.WriteLine($"WinFrame Remote Service active on {server.ListenUrl}");
        Console.WriteLine("Endpoints: /api/status, /api/keywords, /api/controls, /api/controls/automation-ids,");
        Console.WriteLine("           /api/queue/run, /api/step-queue/load, /api/step-queue/next, /api/step-queue/prev, /api/step-queue/range");
        Console.WriteLine("Press Ctrl+C to stop.");

        var tcs = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(); };
        await tcs.Task;
    }
}
