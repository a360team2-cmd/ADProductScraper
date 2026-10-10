using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Playwright;

namespace ADProductScraper.Api;

// Used only for pages that plain HTTP cannot read: it waits for the person to solve any CAPTCHA, scrolls to load every product, and returns the page HTML.
public sealed class BrowserCollector : IAsyncDisposable
{
    private readonly IPlaywright playwright;
    private readonly IBrowser browser;
    private readonly Channel<IPage> pool;
    private readonly PublicNetworkProxy proxy;
    private readonly IPage listingPage;
    private readonly SemaphoreSlim listingGate = new(1, 1);
    private BrowserCollector(IPlaywright playwright, IBrowser browser, Channel<IPage> pool, PublicNetworkProxy proxy, IPage listingPage)
        => (this.playwright, this.browser, this.pool, this.proxy, this.listingPage) = (playwright, browser, pool, proxy, listingPage);
    public static async Task<BrowserCollector> Start(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var playwright = await Playwright.CreateAsync();
        IBrowser? browser = null;
        var proxy = new PublicNetworkProxy();
        try
        {
            try { browser = await playwright.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = false, Proxy = new() { Server = proxy.Address, Bypass = "<-loopback>" } }); }
            catch (PlaywrightException) { browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = false, Proxy = new() { Server = proxy.Address, Bypass = "<-loopback>" } }); }
            var context = await browser.NewContextAsync(new() { AcceptDownloads = false, ServiceWorkers = ServiceWorkerPolicy.Block });
            // Playwright 1.58 assumes close events always contain code/reason.
            // Supply both when blocking sockets so its dispatcher can read them.
            await context.RouteWebSocketAsync("**/*", async socket => await socket.CloseAsync(new() { Code = 1000, Reason = "WebSockets disabled during collection" }));
            var pool = Channel.CreateUnbounded<IPage>();
            for (var i = 0; i < 3; i++) pool.Writer.TryWrite(await context.NewPageAsync());
            return new(playwright, browser, pool, proxy, await context.NewPageAsync());
        }
        catch (Exception e)
        {
            try { if (browser != null) await browser.CloseAsync(); } catch { }
            try { await proxy.DisposeAsync(); } catch { }
            try { playwright.Dispose(); } catch { }
            ct.ThrowIfCancellationRequested();
            throw new HttpRequestException("The browser could not open. " + e.Message, e);
        }
    }
    public async Task<string> Read(Uri uri, bool scroll, Action<string> waiting, CancellationToken ct, string currency = "")
    {
        SafeWeb.Validate(uri.AbsoluteUri);
        if (scroll) await listingGate.WaitAsync(ct);
        var tab = scroll ? listingPage : await pool.Reader.ReadAsync(ct);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try { return await ReadOn(tab, uri, scroll, waiting, ct, currency); }
                catch (Exception error) when (!ct.IsCancellationRequested && !tab.IsClosed && attempt < 2 && error is TimeoutException or PlaywrightException or TemporaryPageException)
                {
                    await Task.Delay(1000, ct);
                }
            }
        }
        finally { if (scroll) listingGate.Release(); else pool.Writer.TryWrite(tab); }
    }
    private async Task<string> ReadOn(IPage page, Uri uri, bool scroll, Action<string> waiting, CancellationToken ct, string currency)
    {
        using var registration = ct.Register(() => { _ = CloseQuietly(); });
        try
        {
            var referer = Uri.TryCreate(page.Url, UriKind.Absolute, out var previous) && previous.Host == uri.Host ? page.Url : null;
            try { await page.GotoAsync(uri.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000, Referer = referer }); }
            catch (TimeoutException)
            {
                // A timed out navigation can still leave useful HTML, but never
                // accept the previous product's document as this product's data.
                if (page.Url != uri.AbsoluteUri || string.IsNullOrWhiteSpace(await Html(page))) throw;
            }
            var deadline = DateTime.UtcNow.AddMinutes(3);
            var told = false;
            while (SafeWeb.IsChallenge(200, await Html(page)))
            {
                ct.ThrowIfCancellationRequested();
                if (page.IsClosed) throw new HttpRequestException("The browser window was closed before the check was completed.");
                if (!told) { waiting("CAPTCHA detected. Solve it in the opened browser window; collection continues automatically (waits up to 3 minutes)."); told = true; }
                if (DateTime.UtcNow > deadline) throw new HttpRequestException("The CAPTCHA was not solved within 3 minutes.");
                await Task.Delay(700, ct);
            }
            if (SafeWeb.IsTemporaryError(await Html(page))) throw new TemporaryPageException();
            await CurrencySelection.Apply(page, currency, ct);
            if (scroll) await ScrollAll(page, ct);
            else { try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 4000 }); } catch (Exception error) when (!page.IsClosed && error is PlaywrightException or TimeoutException) { } }
            var html = await page.ContentAsync();
            if (SafeWeb.IsTemporaryError(html)) throw new TemporaryPageException();
            return html;
        }
        catch (PlaywrightException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
    }
    // Reads the page HTML; a page that is mid-navigation counts as "not ready yet" instead of an error.
    private static async Task<string> Html(IPage page)
    {
        try { return await page.ContentAsync(); }
        catch (PlaywrightException) when (!page.IsClosed) { return ""; }
    }
    private static async Task ScrollAll(IPage page, CancellationToken ct)
    {
        var stable = 0; var last = ""; var end = DateTime.UtcNow.AddSeconds(90);
        for (var i = 0; i < 250 && stable < 6 && DateTime.UtcNow < end; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Move through the grid so IntersectionObserver sentinels above
                // the footer are reached instead of jumping straight past them.
                await page.EvaluateAsync("() => window.scrollBy(0, Math.max(300, window.innerHeight * 0.8))");
                try
                {
                    var more = page.Locator("button:visible, a:visible", new() { HasTextRegex = new Regex("^\\s*(load|show|view)\\s+more", RegexOptions.IgnoreCase) }).First;
                    if (await more.CountAsync() > 0) await more.ClickAsync(new() { Timeout = 1500 });
                }
                catch (Exception error) when (!page.IsClosed && error is PlaywrightException or TimeoutException) { }
                await Task.Delay(900, ct);
                var signature = await page.EvaluateAsync<string>("() => window.scrollY + ':' + document.documentElement.scrollHeight + ':' + location.href + ':' + Array.from(document.querySelectorAll('a[href]'), a => a.getAttribute('href')).join('|')");
                if (signature == last) stable++; else { stable = 0; last = signature; }
            }
            catch (Exception error) when (!page.IsClosed && error is PlaywrightException or TimeoutException) { stable++; await Task.Delay(500, ct); }
        }
    }
    private async Task CloseQuietly() { try { await browser.CloseAsync(); } catch { } }
    public async ValueTask DisposeAsync() { await CloseQuietly(); try { await proxy.DisposeAsync(); } catch { } try { playwright.Dispose(); } catch { } }
}

sealed class TemporaryPageException() : HttpRequestException("eBay returned a temporary error page instead of search results. Resume to retry this page; collection is not complete.");
