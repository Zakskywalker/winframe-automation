using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using WinFrame.Core.Config;
using WinFrame.Core.Controls;
using WinFrame.Core.Execution;
using WinFrame.Core.Keywords;
using WinFrame.Core.Logging;
using WinFrame.Core.Processes;

namespace WinFrame.Core.Service;

/// <summary>DTO for running a step range or queue over the remote API.</summary>
public sealed class StepRangeRequest
{
    public string Range { get; set; } = "1-1";
    public bool UseInverse { get; set; }
    public bool StopOnError { get; set; } = true;
}

public sealed class QueueRunRequest
{
    public string QueueName { get; set; } = "RemoteQueue";
    public bool RecordOpenCvSnapshots { get; set; }
    public List<string> TestFiles { get; set; } = new();
    public List<TestCase> InlineTests { get; set; } = new();
}

/// <summary>
/// Embedded REST API server for hosting WinFrame as a local or remote automation service.
/// Supports running queues, developer-controlled step-by-step forward/inverse execution,
/// step range execution (e.g. <c>1-3</c>, <c>3-4</c>, <c>4-1</c>), control scraping, and log streaming.
/// </summary>
public sealed class WinFrameApiServer : IDisposable
{
    private readonly HttpListener listener = new();
    private CancellationTokenSource? cts;

    public KeywordFramework Engine { get; }
    public StepQueueController StepController { get; private set; }
    public QueueRunResult? LastQueueResult { get; private set; }
    public string ListenUrl { get; }
    public bool IsRunning => listener.IsListening;

    public WinFrameApiServer(KeywordFramework? engine = null, string? url = null)
    {
        Engine = engine ?? new KeywordFramework();
        StepController = Engine.CreateStepController(new TestCase("InteractiveSession"));
        ListenUrl = NormalizePrefix(url ?? ConfigManager.Current.Service.Urls);
    }

    public void Start()
    {
        if (listener.IsListening) return;
        listener.Prefixes.Clear();
        listener.Prefixes.Add(ListenUrl);
        listener.Start();
        cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoop(cts.Token));
        SystemLogger.Log($"WinFrame Remote Service listening on {ListenUrl}");
    }

    public void Stop()
    {
        try { cts?.Cancel(); } catch { }
        try { if (listener.IsListening) listener.Stop(); } catch { }
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var ctx = await listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(ctx), token);
            }
            catch when (token.IsCancellationRequested || !listener.IsListening) { break; }
            catch (Exception ex)
            {
                SystemLogger.Log($"API listener warning: {ex.Message}", SystemLogger.LogLevel.DEBUG);
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext http)
    {
        var req = http.Request;
        var res = http.Response;
        res.Headers.Add("Access-Control-Allow-Origin", "*");
        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Api-Key");
        res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");

        if (req.HttpMethod == "OPTIONS")
        {
            res.StatusCode = 204;
            res.Close();
            return;
        }

        string path = req.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "";
        try
        {
            object? payload = path switch
            {
                "" or "/api/status" => new
                {
                    machine = Environment.MachineName,
                    version = "1.0.0",
                    vendor = "Hand of Glory Games and Software",
                    author = "Zak Millikin",
                    activeApp = ProcessManagementEngine.Shared.Active?.Key,
                    controlsCount = Engine.Controls.Count,
                    stepQueue = new
                    {
                        testName = StepController.Test.Name,
                        cursor = StepController.Cursor,
                        totalSteps = StepController.TotalSteps,
                        hasNext = StepController.HasNext,
                        hasPrevious = StepController.HasPrevious
                    },
                    lastQueue = LastQueueResult
                },

                "/api/keywords" => KeywordRegistry.All.Select(k => new { k.Name, k.Category, k.Description, k.Signature }),

                "/api/controls" => Engine.Controls.All,

                "/api/controls/automation-ids" => JsonSerializer.Deserialize<object>(Engine.Controls.ExportAutomationIdsJson()),

                "/api/logs/system" => SystemLogger.Recent(LogChannel.System, max: 200),
                "/api/logs/user" => SystemLogger.Recent(LogChannel.User, max: 200),
                "/api/logs/test" => SystemLogger.Recent(LogChannel.Test, max: 200),

                "/api/queue/run" when req.HttpMethod == "POST" => await HandleQueueRunAsync(req),

                "/api/step-queue/load" when req.HttpMethod == "POST" => await HandleStepQueueLoadAsync(req),

                "/api/step-queue/next" when req.HttpMethod == "POST" => StepController.StepNext(),

                "/api/step-queue/prev" when req.HttpMethod == "POST" => StepController.StepPrevious(useInverse: true),

                "/api/step-queue/range" when req.HttpMethod == "POST" => await HandleStepQueueRangeAsync(req),

                _ => null
            };

            if (payload == null)
            {
                res.StatusCode = 404;
                await WriteJsonAsync(res, new { error = $"Unknown endpoint '{path}'" });
                return;
            }

            res.StatusCode = 200;
            await WriteJsonAsync(res, payload);
        }
        catch (Exception ex)
        {
            res.StatusCode = 500;
            await WriteJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task<QueueRunResult> HandleQueueRunAsync(HttpListenerRequest req)
    {
        var body = await ReadBodyAsync<QueueRunRequest>(req) ?? new QueueRunRequest();
        var tests = new List<ITestExecutable>();
        foreach (var file in body.TestFiles)
            tests.AddRange(TestSheetLoader.LoadFromFile(WinFramePaths.Resolve(file)));
        tests.AddRange(body.InlineTests);

        LastQueueResult = Engine.ExecuteQueue(tests, body.QueueName, body.RecordOpenCvSnapshots);
        return LastQueueResult;
    }

    private async Task<object> HandleStepQueueLoadAsync(HttpListenerRequest req)
    {
        var tc = await ReadBodyAsync<TestCase>(req) ?? new TestCase("RemoteStepSession");
        StepController.LoadTest(tc);
        return new { loaded = tc.Name, steps = StepController.TotalSteps, cursor = StepController.Cursor };
    }

    private async Task<object> HandleStepQueueRangeAsync(HttpListenerRequest req)
    {
        var r = await ReadBodyAsync<StepRangeRequest>(req) ?? new StepRangeRequest();
        var results = StepController.ExecuteRange(r.Range, r.UseInverse, r.StopOnError);
        return new
        {
            range = r.Range,
            useInverse = r.UseInverse,
            cursor = StepController.Cursor,
            results
        };
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpListenerRequest req)
    {
        using var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
        string json = await sr.ReadToEndAsync();
        return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, ConfigManager.JsonOptions);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse res, object obj)
    {
        res.ContentType = "application/json; charset=utf-8";
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj, ConfigManager.JsonOptions));
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    private static string NormalizePrefix(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) url = "http://localhost:5880/";
        if (!url.EndsWith('/')) url += "/";
        return url;
    }

    public void Dispose() => Stop();
}

/// <summary>Client for invoking a remote <see cref="WinFrameApiServer"/> over HTTP.</summary>
public sealed class WinFrameApiClient(string baseUrl = "http://localhost:5880", string? apiKey = null)
{
    private readonly HttpClient http = CreateClient(baseUrl, apiKey);

    private static HttpClient CreateClient(string url, string? key)
    {
        var c = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/") };
        if (!string.IsNullOrWhiteSpace(key)) c.DefaultRequestHeaders.Add("X-Api-Key", key);
        return c;
    }

    public async Task<string> GetStatusJsonAsync() => await http.GetStringAsync("api/status");

    public async Task<string> StepNextJsonAsync()
    {
        using var resp = await http.PostAsync("api/step-queue/next", new StringContent("{}", Encoding.UTF8, "application/json"));
        return await resp.Content.ReadAsStringAsync();
    }

    public async Task<string> StepPreviousInverseJsonAsync()
    {
        using var resp = await http.PostAsync("api/step-queue/prev", new StringContent("{}", Encoding.UTF8, "application/json"));
        return await resp.Content.ReadAsStringAsync();
    }

    public async Task<string> ExecuteRangeJsonAsync(string rangeSpec, bool useInverse = false)
    {
        string body = JsonSerializer.Serialize(new StepRangeRequest { Range = rangeSpec, UseInverse = useInverse }, ConfigManager.JsonOptions);
        using var resp = await http.PostAsync("api/step-queue/range", new StringContent(body, Encoding.UTF8, "application/json"));
        return await resp.Content.ReadAsStringAsync();
    }
}
