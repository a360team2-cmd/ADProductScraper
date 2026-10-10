using ADProductScraper.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;

static class EtsyChecks
{
    public static async Task Run()
    {
        var passed = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); passed++; }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Etsy:Keystring"] = "test-key", ["Etsy:SharedSecret"] = "test-secret" }).Build();
        const string listing = """{"listing_id":123,"title":"Cup &amp; saucer","materials":["clay","glaze"],"price":{"amount":1299,"divisor":100,"currency_code":"USD"},"Shop":{"shop_name":"Cup Studio"},"Images":[{"url_fullxfull":"https://images.example/cup.jpg"},{"url_fullxfull":"https://images.example/cup.jpg"},{"url_570xN":"https://images.example/back.jpg"},{"url_fullxfull":"javascript:bad"}]}""";
        using var document = JsonDocument.Parse(listing);
        var product = EtsyClient.MapListing(document.RootElement, "https://www.etsy.com/search?q=cup");
        Check(product.Price == "12.99" && product.Currency == "USD", "Money uses Etsy divisor rather than treating cents as units");
        Check(product.Title == "Cup & saucer" && product.Store == "Cup Studio" && product.Material == "clay; glaze", "Etsy published fields and HTML entities map correctly");
        Check(product.Images.Length == 2 && product.Url == "https://www.etsy.com/listing/123", "Images are deduplicated and unsafe schemes excluded");
        using var empty = JsonDocument.Parse("""{"listing_id":1,"price":{"amount":200,"divisor":0,"currency_code":"USD"}}""");
        Check(EtsyClient.MapListing(empty.RootElement, "").Price == "" && EtsyClient.MapListing(empty.RootElement, "").Store == "", "Missing associations and invalid divisor never invent values");
        using var unconfigured = new EtsyClient(new ConfigurationBuilder().Build(), new HttpClient(new Handler(_ => throw new Exception("Must not call API"))));
        try { await unconfigured.FetchPage("cup", null, 0, default); throw new Exception("Missing credentials accepted"); }
        catch (JobConflictException) { Check(!unconfigured.Configured, "Missing credentials fail before any external request"); }
        var calls = new List<string>();
        using var client = new EtsyClient(config, new HttpClient(new Handler(request =>
        {
            Check(request.RequestUri!.Host == "openapi.etsy.com" && request.Headers.GetValues("x-api-key").Single() == "test-key:test-secret", "Credentials are sent only in the official API header");
            calls.Add(request.RequestUri.PathAndQuery);
            return Json(request.RequestUri.AbsolutePath.EndsWith("/batch") ? "{\"count\":1,\"results\":[" + listing + "]}" : """{"count":2,"results":[{"listing_id":123}]}""");
        })));
        using var services = new ServiceCollection().AddSingleton<IConfiguration>(config).AddSingleton<EtsyClient>().BuildServiceProvider();
        Check(services.GetRequiredService<EtsyClient>().Configured, "Application DI resolves the configured Etsy client");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await client.FetchPage("cup", null, 0, cancellation.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Check(true, "Cancellation stops an Etsy fetch"); }
        var page = await client.FetchPage("cup & saucer", 42, 100, default);
        Check(calls[0].Contains("shops/42/listings/active") && calls[0].Contains("offset=100") && calls[0].Contains("keywords=cup%20%26%20saucer"), "Shop search preserves offset and encodes keywords");
        Check(calls[1].Contains("includes=Images,Shop") && page.Products.Length == 1 && page.Total == 2, "Batch enrichment supplies images and shop data");
        using var denied = new EtsyClient(config, new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden) { Content = new StringContent("private upstream error") })));
        try { await denied.FetchPage("cup", null, 0, default); throw new Exception("Denied request accepted"); }
        catch (HttpRequestException error) { Check(error.Message.Contains("approval") && !error.Message.Contains("private upstream"), "Pending approval produces a useful sanitized error"); }
        var retries = 0;
        using var limited = new EtsyClient(config, new HttpClient(new Handler(_ =>
        {
            if (retries++ == 0) { var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new(TimeSpan.Zero); return response; }
            return Json("""{"count":0,"results":[]}""");
        })));
        Check((await limited.FetchPage("cup", null, 0, default)).Returned == 0 && retries == 2, "429 Retry-After is retried without losing the page");
        using var incomplete = new EtsyClient(config, new HttpClient(new Handler(request => Json(request.RequestUri!.AbsolutePath.EndsWith("/batch") ? """{"results":[]}""" : """{"count":1,"results":[{"listing_id":123}]}"""))));
        try { await incomplete.FetchPage("cup", null, 0, default); throw new Exception("Incomplete batch accepted"); }
        catch (HttpRequestException error) { Check(error.Message.Contains("incomplete"), "Missing detail records do not silently discard listings"); }
        var root = Path.Combine(Path.GetTempPath(), "etsy-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var failSecond = true;
            using var api = new EtsyClient(config, new HttpClient(new Handler(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/batch"))
                    return Json("{\"results\":[" + listing.Replace("123", request.RequestUri.Query.Contains("124") ? "124" : "123") + "]}");
                if (request.RequestUri.Query.Contains("offset=1&"))
                {
                    if (failSecond) return new(HttpStatusCode.Forbidden);
                    return Json("""{"count":2,"results":[{"listing_id":124}]}""");
                }
                return Json("""{"count":2,"results":[{"listing_id":123}]}""");
            })));
            using var web = new SafeWeb();
            using var jobs = new Jobs(new EnvironmentStub(root), web, NullLogger<Jobs>.Instance, api);
            var id = jobs.Create(new StartRequest("", Source: "etsy", Keywords: "cup"));
            await jobs.StartAsync(default);
            try
            {
                await Wait(() => jobs.Status(id) == "Failed");
                Check(jobs.Products(id)!.Length == 1, "Failed later page retains earlier Etsy products");
                var saved = File.ReadAllText(Path.Combine(root, "data", id + ".json"));
                Check(!saved.Contains("test-secret") && !saved.Contains("test-key") && saved.Contains("\"cursor\": \"1\""), "Checkpoint stores resume offset without credentials");
                failSecond = false; jobs.Resume(id);
                await Wait(() => jobs.Status(id) == "Completed");
                Check(jobs.Products(id)!.Length == 2, "Resume retries failed page without duplicating previous products");
                Check(ExcelExport.Build(jobs.Products(id)!, jobs.Status(id)).Length > 1000, "Etsy products use the existing Excel exporter");
                var limitedId = jobs.Create(new StartRequest("", MaxPages: 1, Source: "etsy", Keywords: "cup"));
                await Wait(() => jobs.Status(limitedId) == "Limited");
                Check(jobs.Products(limitedId)!.Length == 1, "Etsy page limit produces a partial collection");
            }
            finally { await jobs.StopAsync(default); }
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"{passed} Etsy integration checks passed.");
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    private static async Task Wait(Func<bool> ready)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); while (!ready()) await Task.Delay(25, timeout.Token); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); } }
    private sealed class EnvironmentStub(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "EtsyChecks";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
