using System.Text.Json;
using System.Threading.Channels;

namespace ADProductScraper.Api;

public sealed class Jobs : BackgroundService
{
    private readonly Dictionary<string, CrawlJob> jobs = [];
    private readonly object gate = new();
    private readonly Channel<string> queue = Channel.CreateBounded<string>(10);
    private readonly string directory;
    private readonly SafeWeb web;
    private readonly ILogger<Jobs> logger;
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public Jobs(IWebHostEnvironment env, SafeWeb web, ILogger<Jobs> logger)
    {
        this.web = web; this.logger = logger;
        directory = Path.Combine(env.ContentRootPath, "data"); Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var job = JsonSerializer.Deserialize<CrawlJob>(File.ReadAllText(file), json);
                if (job == null) continue;
                if (Path.GetFileNameWithoutExtension(file) != job.Id || job.Request == null || job.Products == null || job.Visited == null || job.Warnings == null)
                    throw new JsonException("Saved collection has invalid or missing fields.");
                if (job.Status is "Running" or "Queued" or "Stopping" or "WaitingForBrowser") { job.Status = "Paused"; job.Message = "Application restarted. Resume to continue."; }
                jobs[job.Id] = job;
            }
            catch (Exception e) { logger.LogWarning(e, "Could not load saved job {File}", file); }
        }
    }
    private static object Summary(CrawlJob j) => new { j.Id, j.Request, j.Status, j.Message, j.Created, j.Updated, Pages = j.Visited.Count, Count = j.Products.Count, j.ExpectedProducts, Warnings = j.Warnings.ToArray() };
    public object[] List() { lock (gate) return jobs.Values.OrderByDescending(j => j.Created).Take(100).Select(Summary).ToArray(); }
    public object? Get(string id) { lock (gate) return jobs.TryGetValue(id, out var j) ? Summary(j) : null; }
    public string Status(string id) { lock (gate) return jobs.TryGetValue(id, out var j) ? j.Status : "Unknown"; }
    public Product[]? Products(string id, int skip = 0, int take = 100000) { lock (gate) return jobs.TryGetValue(id, out var j) ? j.Products.Skip(skip).Take(take).ToArray() : null; }
    public string Create(StartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request = request with { Currency = CurrencySelection.Resolve(request.Currency, request.Url) };
        if (request.Currency.Length > 0 && !request.BrowserMode && !request.Demo) throw new ArgumentException("Selecting a website currency requires Browser mode.");
        if (!request.Demo) SafeWeb.Validate(request.Url);
        if (request.MaxPages is < 0 or > 10000) throw new ArgumentException("Page limit must be between 0 and 10000. Zero follows all discoverable pages.");
        Extractor.ValidateSelectors(request.Selectors ?? new());
        lock (gate)
        {
            if (jobs.Count >= 1000) throw new ArgumentException("Job history is full. Archive files in backend/data before restarting.");
            var job = new CrawlJob { Request = request, Cursor = request.Demo ? "https://demo.adproductscraper.test/search?page=1" : request.Url };
            Save(job);
            jobs.Add(job.Id, job);
            if (!queue.Writer.TryWrite(job.Id))
            {
                Finish(job, "Failed", "Queue is full. Start a new collection after an existing one finishes.");
                throw new JobConflictException(job.Message);
            }
            return job.Id;
        }
    }
    public bool Cancel(string id)
    {
        lock (gate)
        {
            if (!jobs.TryGetValue(id, out var job)) return false;
            if (job.Status is "Queued" or "Running" or "WaitingForBrowser") { job.Cancellation.Cancel(); job.Status = "Stopping"; job.Message = "Stopping and saving collected products…"; }
            return true;
        }
    }
    public bool Resume(string id)
    {
        lock (gate)
        {
            if (!jobs.TryGetValue(id, out var job)) return false;
            if (job.Status is not ("Paused" or "Failed" or "Cancelled")) throw new JobConflictException("Only paused, failed or stopped jobs can resume.");
            if (job.Cursor == null) throw new JobConflictException("There is no remaining page to resume.");
            job.Cancellation.Dispose(); job.Cancellation = new();
            var previousStatus = job.Status; var previousMessage = job.Message;
            job.Status = "Queued"; job.Message = "Queued to resume"; job.FailureDetails = null;
            try { Save(job); }
            catch { job.Status = previousStatus; job.Message = previousMessage; throw; }
            if (!queue.Writer.TryWrite(id))
            {
                Finish(job, previousStatus, previousMessage);
                throw new JobConflictException("Queue is full. Wait for an existing collection to finish.");
            }
            return true;
        }
    }
    private void Save(CrawlJob job)
    {
        job.Updated = DateTimeOffset.UtcNow;
        var path = Path.Combine(directory, job.Id + ".json");
        try
        {
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(job, json)); File.Move(path + ".tmp", path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new JobStorageException("Could not save the collection. Check disk space and data folder permissions. Keep the app open and download any collected products.", error);
        }
    }
    // A failed checkpoint must not terminate the worker or hide the original failure.
    private void Finish(CrawlJob job, string status, string message, Exception? error = null)
    {
        lock (gate)
        {
            job.Status = status; job.Message = message; job.Updated = DateTimeOffset.UtcNow;
            if (error != null) job.FailureDetails = error.ToString();
            try { Save(job); }
            catch (Exception saveError) when (saveError is JobStorageException or JsonException or NotSupportedException)
            {
                logger.LogError(saveError, "Could not save final state for collection {JobId}", job.Id);
                const string warning = "Latest progress could not be saved to disk. Keep the app open and download collected products before restarting.";
                if (!job.Warnings.Contains(warning)) job.Warnings.Add(warning);
            }
        }
    }
    private void Update(CrawlJob job, Action action, bool save = false) { lock (gate) { action(); job.Updated = DateTimeOffset.UtcNow; if (save) Save(job); } }
    private void Warn(CrawlJob job, string message) => Update(job, () => { if (!job.Warnings.Contains(message) && job.Warnings.Count < 100) job.Warnings.Add(message); });
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in queue.Reader.ReadAllAsync(stoppingToken))
        {
            CrawlJob? job; lock (gate) jobs.TryGetValue(id, out job);
            if (job == null) { logger.LogWarning("Skipped missing queued collection {JobId}", id); continue; }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cancellation.Token);
            try { await Crawl(job, linked.Token); }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { Finish(job, "Cancelled", "Stopped. Collected products are ready for Excel; resume when needed."); }
            catch (Exception e)
            {
                logger.LogWarning(e, "Collection failed for {JobId}", id);
                Finish(job, "Failed", ErrorHandling.CollectionMessage(e), e);
            }
        }
    }
    private async Task Crawl(CrawlJob job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Update(job, () => { job.Status = "Running"; job.Message = "Checking website access…"; }, true);
        var origin = new Uri(job.Cursor!).GetLeftPart(UriPartial.Authority);
        var rules = RobotsRules.Parse("");
        if (!job.Request.Demo)
        {
            try
            {
                var robots = await web.Fetch(new Uri(origin + "/robots.txt"), ct);
                if (robots.Status is 401 or 403 or 429 || robots.Status >= 500) Warn(job, "The website did not allow reading robots.txt; its rules could not be checked.");
                if (robots.Status == 200) rules = RobotsRules.Parse(robots.Html);
            }
            catch (Exception e) when (!ct.IsCancellationRequested && !job.Request.RespectRobots && e is HttpRequestException or OperationCanceledException)
            { Warn(job, "Robots file could not be read. Continuing because robots handling is set to warning only. Website access restrictions still apply."); }
        }
        await using var holder = new BrowserHolder();
        var browserOnly = !string.IsNullOrEmpty(job.Request.Currency); var listViaBrowser = false; var browserGate = new SemaphoreSlim(1, 1);
        using var browserGateLifetime = browserGate;
        var indices = job.Products.Select((p, index) => (Key: Extractor.Key(p.Url), index)).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().index);
        void Add(Product product)
        {
            Update(job, () =>
            {
                var key = Extractor.Key(product.Url);
                if (indices.TryGetValue(key, out var index)) job.Products[index] = Extractor.Merge(product, job.Products[index]);
                else { indices[key] = job.Products.Count; job.Products.Add(product); }
            });
        }
        async Task<string> Fetch(Uri uri, bool detail = false)
        {
            ct.ThrowIfCancellationRequested();
            if (uri.GetLeftPart(UriPartial.Authority) != origin) throw new HttpRequestException("A page points to another website. Collection is limited to the original website.");
            if (rules.BlockingRule(uri) is { } rule)
            {
                if (job.Request.RespectRobots) throw new HttpRequestException($"Stopped by the selected robots policy. {origin}/robots.txt matches Disallow: {rule} for {uri.AbsolutePath}. Start a new collection using Browser mode with robots handling set to warning only. This does not bypass login, CAPTCHA or actual access denials.");
                Warn(job, $"Robots advisory: Disallow: {rule}. You selected warning-only handling. This does not override the website's actual access controls.");
            }
            if (job.Request.Demo) { await Task.Delay(300, ct); return DemoPages.Get(uri); }
            if (!browserOnly)
            {
                (int Status, string Html) response;
                try { response = await web.Fetch(uri, ct); }
                catch (Exception error) when (!ct.IsCancellationRequested && error is HttpRequestException or OperationCanceledException)
                {
                    if (!job.Request.BrowserMode) throw new HttpRequestException("The website request failed or timed out. Resume to retry, or enable Browser mode.", error);
                    if (!detail) browserOnly = true;
                    return await ViaBrowser(uri, !detail, !detail);
                }
                for (var retry = 1; retry <= 2 && response.Status is 429 or 430 or 503; retry++)
                {
                    await Task.Delay(1500 * retry, ct);
                    response = await web.Fetch(uri, ct);
                }
                if (response.Status == 200 && SafeWeb.Classify(200, response.Html) == PageKind.Ok && (detail || Readable(response.Html, uri))) { if (!detail) listViaBrowser = false; return response.Html; }
                if (!job.Request.BrowserMode) throw new HttpRequestException(SafeWeb.BlockedMessage(uri, response.Status, SafeWeb.Classify(response.Status, response.Html), false));
                if (!detail) browserOnly = true;
            }
            if (!detail) listViaBrowser = true;
            return await ViaBrowser(uri, !detail, !detail);
        }
        async Task<string> ViaBrowser(Uri uri, bool scroll, bool canRestart)
        {
            if (!job.Request.BrowserMode) throw new HttpRequestException(SafeWeb.BlockedMessage(uri, 0, PageKind.Blocked, false));
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await browserGate.WaitAsync(ct);
                    try { holder.Value ??= await BrowserCollector.Start(ct); }
                    finally { browserGate.Release(); }
                    var html = await holder.Value.Read(uri, scroll, message => Update(job, () => { job.Status = "WaitingForBrowser"; job.Message = message; }), ct, job.Request.Currency);
                    Update(job, () => { job.Status = "Running"; });
                    return html;
                }
                catch (Exception e) when (e is Microsoft.Playwright.PlaywrightException or TimeoutException or IOException or ObjectDisposedException or InvalidOperationException or NullReferenceException or KeyNotFoundException)
                {
                    ct.ThrowIfCancellationRequested();
                    logger.LogWarning(e, "Browser read failed for {Url}, attempt {Attempt}", uri, attempt + 1);
                    if (canRestart)
                    {
                        await browserGate.WaitAsync(ct);
                        try { if (holder.Value != null) { var dead = holder.Value; holder.Value = null; await dead.DisposeAsync(); } }
                        catch { }
                        finally { browserGate.Release(); }
                    }
                    if (!canRestart || attempt >= 1) throw new HttpRequestException("The browser could not load this page after recovery. " + e.Message, e);
                }
            }
        }
        bool Readable(string html, Uri uri)
        {
            var parsed = Extractor.Parse(html, uri, job.Request.Selectors);
            return parsed.Products.Count > 0 || parsed.DetailLinks.Count > 0 || Extractor.Parse(html, uri, job.Request.Selectors, true).Products.Any(p => p.Title.Length > 0 && p.Price.Length > 0);
        }
        while (job.Cursor != null)
        {
            ct.ThrowIfCancellationRequested();
            if (job.Request.MaxPages > 0 && job.Visited.Count >= job.Request.MaxPages)
            { Update(job, () => { job.Status = "Limited"; job.Message = "Selected page limit reached. Excel contains the pages collected so far."; }, true); return; }
            if (job.Products.Count >= 100000) { Update(job, () => { job.Status = "Limited"; job.Message = "100,000-product safety limit reached. Narrow the search to collect more."; }, true); return; }
            var uri = new Uri(job.Cursor);
            if (job.Visited.Contains(uri.AbsoluteUri)) { Warn(job, "Pagination repeated a visited URL. Stopped to prevent a loop."); break; }
            Update(job, () => job.Message = $"Reading results page {job.Visited.Count + 1}…");
            string html;
            try
            {
                html = await Fetch(uri);
                if (job.Request.BrowserMode && !job.Request.Demo && Extractor.Parse(html, uri, job.Request.Selectors).RequiresPriceRendering)
                {
                    Update(job, () => job.Message = "Rendering original and discounted product prices…");
                    html = await ViaBrowser(uri, true, true);
                    listViaBrowser = true;
                    browserOnly = true;
                }
            }
            catch (HttpRequestException e) when (job.Products.Count > 0)
            {
                Warn(job, e.Message);
                Update(job, () => { job.Status = "Paused"; job.Message = e.Message + " Products collected so far can be downloaded; use Resume to retry."; }, true);
                return;
            }
            var page = Extractor.Parse(html, uri, job.Request.Selectors);
            if (page.Products.Count == 0 && page.DetailLinks.Count == 0 && !page.IsCollection)
            {
                var detail = Extractor.Parse(html, uri, job.Request.Selectors, true);
                page = detail.Products.Any(p => p.Price.Length > 0) ? detail : page;
            }
            if (page.ExpectedProducts is { } expected && job.ExpectedProducts == null)
                Update(job, () => job.ExpectedProducts = expected);
            if (!listViaBrowser && job.Visited.Count == 0 && page.Next == null && Math.Max(page.Products.Count, page.DetailLinks.Count) >= 12 && job.Request.BrowserMode && !job.Request.Demo)
            {
                // No Next button: the store may load more products while scrolling. Scroll once in the browser and keep whichever read has more.
                try
                {
                    Update(job, () => job.Message = "Scrolling the page to load all products…");
                    var scrolled = await ViaBrowser(uri, true, true);
                    var more = Extractor.Parse(scrolled, uri, job.Request.Selectors);
                    if (Math.Max(more.Products.Count, more.DetailLinks.Count) > Math.Max(page.Products.Count, page.DetailLinks.Count)) { page = more; listViaBrowser = true; }
                }
                catch (HttpRequestException e)
                {
                    logger.LogWarning(e, "Optional browser scrolling failed for {JobId}", job.Id);
                    Update(job, () => job.FailureDetails = e.ToString());
                    Warn(job, "Could not scroll for more products: " + e.Message);
                }
            }
            if (page.Products.Count == 0 && page.DetailLinks.Count == 0 && job.Visited.Count > 0)
            {
                if (Pagination.IsEbaySearch(uri) && !page.PaginationEnded)
                {
                    Finish(job, "Paused", "eBay returned no readable products before confirming the last page. Collected products are saved; Resume retries this page.");
                    return;
                }
                if (job.ExpectedProducts is { } total && job.Products.Count < total)
                {
                    Finish(job, "Paused", $"Collection incomplete: {job.Products.Count} of {total} products collected. The next page returned no readable products. Use Resume to retry.");
                    return;
                }
                Update(job, () => job.Cursor = null, true); break;
            }
            if (page.Products.Count == 0 && page.DetailLinks.Count == 0)
                throw new HttpRequestException("No products could be read from this website without a browser. It either loads products with JavaScript, needs a login, or blocks scraping, so it can't be collected. Try a different page or store.");
            var before = job.Products.Count;
            foreach (var product in page.Products) Add(product);
            // Infinite-scroll stores have no Next button but still serve ?page=N: keep going while new products appear.
            var known = page.Products.Select(p => Extractor.Key(p.Url)).ToHashSet();
            // Visit a product page whenever the listing did not give every field, so nothing is left out needlessly.
            var details = page.DetailLinks.Where(l => !known.Contains(Extractor.Key(l)))
                .Concat(page.Products.Where(p => p.Price.Length == 0 || p.Store.Length == 0 || p.Material.Length == 0 || p.Images.Length == 0).Select(p => p.Url))
                .DistinctBy(Extractor.Key).ToArray();
            var done = 0;
            await Parallel.ForEachAsync(details, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct }, async (link, token) =>
            {
                try
                {
                var detailUri = new Uri(link);
                if (detailUri.GetLeftPart(UriPartial.Authority) != origin) { Warn(job, "Skipped product links pointing to a different website."); return; }
                if (job.Request.RespectRobots && !rules.Allows(detailUri)) { Warn(job, "Skipped disallowed product detail pages; collection continues with listing data."); return; }
                string detailHtml;
                if (detailUri == uri) detailHtml = html;
                else
                {
                    try { detailHtml = await Fetch(detailUri, true); }
                    catch (HttpRequestException e) { Warn(job, "Some product pages could not be opened; their missing fields are marked Not available. " + e.Message); return; }
                }
                var detail = Extractor.Parse(detailHtml, detailUri, job.Request.Selectors, true);
                if (detail.Products.Count == 0) Warn(job, "Some product detail pages contained no readable product information.");
                foreach (var product in detail.Products) Add(product with { SourcePage = uri.AbsoluteUri });
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (JobStorageException) { throw; }
                catch (Exception error)
                {
                    logger.LogWarning(error, "Could not collect product {Url} for {JobId}", link, job.Id);
                    Warn(job, "A product could not be processed; collection continues with available listing data. " + ErrorHandling.CollectionMessage(error));
                }
                finally
                {
                var n = Interlocked.Increment(ref done);
                Update(job, () => job.Message = $"Page {job.Visited.Count + 1} · product details {n}/{details.Length}");
                }
            });
            Update(job, () => { }, true);
            // Some themes expose only ItemList URLs on the collection page;
            // products are added by the detail reads above, so decide afterwards.
            var next = Pagination.Next(uri, page, job.Products.Count > before);
            if (Pagination.IsEbaySearch(uri) && !page.PaginationEnded && (next == null || job.Visited.Contains(next) || next == uri.AbsoluteUri))
            {
                Finish(job, "Paused", "eBay pagination returned repeated results or stopped without a confirmed last page. Collected products are saved; Resume retries this page.");
                return;
            }
            if (job.ExpectedProducts is { } expectedTotal && job.Products.Count >= expectedTotal) next = null;
            if (next == null && job.ExpectedProducts is { } advertisedTotal && job.Products.Count < advertisedTotal)
            {
                // Keep this unreadable/repeated page retryable rather than
                // clearing the cursor and falsely reporting completion.
                Finish(job, "Paused", $"Collection incomplete: {job.Products.Count} of {advertisedTotal} products collected. Pagination stopped before the advertised total. Use Resume to retry.");
                return;
            }
            Update(job, () => { job.Visited.Add(uri.AbsoluteUri); job.Cursor = next; }, true);
        }
        if (job.ExpectedProducts is { } remainingTotal && job.Products.Count < remainingTotal)
        {
            Update(job, () =>
            {
                job.Cursor ??= job.Visited.LastOrDefault() ?? job.Request.Url;
                job.Visited.Remove(job.Cursor);
            });
            Finish(job, "Paused", $"Collection incomplete: {job.Products.Count} of {remainingTotal} products collected. Pagination ended before the advertised total. Use Resume to retry.");
            return;
        }
        if (job.Products.Any(p => p.Material.Length == 0 || p.Store.Length == 0)) Warn(job, "Some material or store fields were not published or could not be extracted and are left blank.");
        Update(job, () => { job.Status = "Completed"; job.Message = job.ExpectedProducts is { } total ? $"Collected all {total} products advertised by the collection." : "Finished discoverable pages. Review warnings; websites may hide additional results."; job.Cursor = null; }, true);
    }

}

sealed class BrowserHolder : IAsyncDisposable
{
    public BrowserCollector? Value;
    public async ValueTask DisposeAsync() { try { if (Value != null) await Value.DisposeAsync(); } catch { } }
}
