using ADProductScraper.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

static class ErrorHandlingChecks
{
    public static async Task Run()
    {
        var passed = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); passed++; }
        foreach (var (error, status) in new (Exception, int)[] {
            (new ArgumentException("Invalid URL"),400), (new JobConflictException("Already running"),409),
            (new BadHttpRequestException("Bad JSON"),400), (new IOException("Disk secret"),503),
            (new UnauthorizedAccessException("Private path"),503), (new HttpRequestException("Remote secret"),502),
            (new TimeoutException(),504), (new KeyNotFoundException("Internal key"),500) })
        {
            var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
            context.TraceIdentifier = "test-reference";
            var handler = new ApiExceptionMiddleware(_ => Task.FromException(error), NullLogger<ApiExceptionMiddleware>.Instance);
            await handler.InvokeAsync(context);
            context.Response.Body.Position = 0;
            using var body = await JsonDocument.ParseAsync(context.Response.Body);
            Check(context.Response.StatusCode == status && body.RootElement.GetProperty("traceId").GetString() == "test-reference", $"API {error.GetType().Name} maps to {status} with reference");
            if (status >= 500) Check(!body.RootElement.GetProperty("error").GetString()!.Contains(error.Message), "Internal exception details are not exposed");
        }
        var missing = new DefaultHttpContext(); missing.Request.Path = "/api/jobs/missing"; missing.Response.Body = new MemoryStream();
        await new ApiExceptionMiddleware(c => { c.Response.StatusCode = 404; return Task.CompletedTask; }, NullLogger<ApiExceptionMiddleware>.Instance).InvokeAsync(missing);
        Check(missing.Response.StatusCode == 404 && missing.Response.Body.Length > 0, "Missing collections return JSON errors");

        var root = Path.Combine(AppContext.BaseDirectory, "error-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var web = new SafeWeb();
        using var worker = new Jobs(new TestEnvironment(root), web, NullLogger<Jobs>.Instance);
        var data = Path.Combine(root, "data");
        Directory.Delete(data); // empty directory created by Jobs
        File.WriteAllText(data, "Storage unavailable");
        var rejected = false;
        try { worker.Create(new StartRequest("", Demo: true)); } catch (JobStorageException) { rejected = true; }
        Check(rejected && worker.List().Length == 0, "Failed initial save does not enqueue a phantom collection");
        File.Delete(data); Directory.CreateDirectory(data);

        var broken = new CrawlJob { Id = "broken", Request = new StartRequest("", Demo:true), Status = "Paused", Cursor = "invalid URI" };
        File.WriteAllText(Path.Combine(data, "broken.json"), JsonSerializer.Serialize(broken, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var incomplete = new CrawlJob { Id = "incomplete", Request = new StartRequest("", Demo:true), Status = "Paused", Cursor = "https://demo.adproductscraper.test/search?page=1", ExpectedProducts = 1000 };
        File.WriteAllText(Path.Combine(data, "incomplete.json"), JsonSerializer.Serialize(incomplete, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var ebay = new CrawlJob { Id = "ebay-repeat", Request = new StartRequest("https://www.ebay.com/sch/i.html?_nkw=leather+jackets", Demo: true), Status = "Paused", Cursor = "https://www.ebay.com/sch/i.html?_nkw=leather+jackets" };
        File.WriteAllText(Path.Combine(data, ebay.Id + ".json"), JsonSerializer.Serialize(ebay, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var jobs = new Jobs(new TestEnvironment(root), web, NullLogger<Jobs>.Instance);
        jobs.Resume("broken");
        jobs.Resume("incomplete");
        jobs.Resume(ebay.Id);
        var storageFailure = jobs.Create(new StartRequest("", Demo:true));
        Directory.CreateDirectory(Path.Combine(data, storageFailure + ".json.tmp"));
        var demo = jobs.Create(new StartRequest("", Demo:true));
        await jobs.StartAsync(CancellationToken.None);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (jobs.Status(demo) is "Queued" or "Running") await Task.Delay(50, timeout.Token);
            Check(jobs.Status("broken") == "Failed", "Unexpected crawler exception marks only its own job failed");
            Check(jobs.Status("incomplete") == "Paused" && jobs.Products("incomplete")!.Length == 6, "Pagination cycle below the advertised count never reports Completed");
            var incompleteState = JsonSerializer.Deserialize<CrawlJob>(File.ReadAllText(Path.Combine(data, "incomplete.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Check(incompleteState?.Cursor != null && !incompleteState.Visited.Contains(incompleteState.Cursor), "Incomplete collection retains a retryable cursor");
            var ebayState = JsonSerializer.Deserialize<CrawlJob>(File.ReadAllText(Path.Combine(data, ebay.Id + ".json")), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Check(ebayState?.Status == "Paused" && ebayState.Products.Count == 6, "eBay repeated results never report Completed without a confirmed last page");
            Check(ebayState?.Cursor?.Contains("_pgn=3") == true && !ebayState.Visited.Contains(ebayState.Cursor), "eBay retains the failed third-page cursor for Resume");
            Check(jobs.Status(storageFailure) == "Failed", "Checkpoint storage error marks the collection failed");
            var storageSummary = JsonSerializer.Serialize(jobs.Get(storageFailure));
            Check(storageSummary.Contains("could not be saved to disk"), "Final-state save failure warns about unsaved progress without crashing the worker");
            Check(jobs.Status(demo) == "Completed" && jobs.Products(demo)!.Length == 6, "Worker processes the next collection after a failure");
            var saved = JsonSerializer.Deserialize<CrawlJob>(File.ReadAllText(Path.Combine(data, "broken.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Check(saved?.FailureDetails?.Contains("UriFormatException") == true, "Original stack trace is saved for diagnosis");
        }
        finally { await jobs.StopAsync(CancellationToken.None); }
        Console.WriteLine($"{passed} error handling checks passed.");
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ErrorHandlingChecks";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
