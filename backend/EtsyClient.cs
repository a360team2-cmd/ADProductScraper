using System.Globalization;
using System.Net;
using System.Text.Json;
namespace ADProductScraper.Api;
public sealed record EtsyPage(Product[] Products, int Total, int Returned);
public sealed class EtsyClient : IDisposable
{
    private readonly HttpClient http;
    private readonly IConfiguration configuration;
    private readonly SemaphoreSlim requests = new(1, 1);
    private DateTimeOffset nextRequest;
    public EtsyClient(IConfiguration configuration) : this(configuration, new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })) { }
    public EtsyClient(IConfiguration configuration, HttpClient http)
    {
        this.configuration = configuration; this.http = http;
        this.http.Timeout = TimeSpan.FromSeconds(35);
        this.http.MaxResponseContentBufferSize = 8 * 1024 * 1024;
    }
    public bool Configured => !string.IsNullOrWhiteSpace(configuration["Etsy:Keystring"]) && !string.IsNullOrWhiteSpace(configuration["Etsy:SharedSecret"]);
    public void EnsureConfigured()
    {
        if (!Configured) throw new JobConflictException("Etsy API credentials are not configured. Set Etsy:Keystring and Etsy:SharedSecret on the backend, then restart. Etsy must also approve the app.");
        if (new[] { configuration["Etsy:Keystring"]!, configuration["Etsy:SharedSecret"]! }.Any(value => value.Trim().Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == ':')))
            throw new JobConflictException("Etsy credentials have an invalid format. Re-enter the values in backend User Secrets.");
    }
    public async Task<EtsyPage> FetchPage(string keywords, long? shopId, int offset, CancellationToken ct)
    {
        EnsureConfigured();
        if (offset < 0 || offset > 100000) throw new ArgumentException("Invalid Etsy page offset.");
        var path = shopId is > 0 ? $"shops/{shopId}/listings/active" : "listings/active";
        using var page = await Get($"{path}?limit=100&offset={offset}&sort_on=created&sort_order=desc&keywords={Uri.EscapeDataString(keywords)}", ct);
        var root = page.RootElement;
        var results = root.GetProperty("results").EnumerateArray().ToArray();
        var total = root.GetProperty("count").GetInt32();
        if (results.Length == 0) return new([], total, 0);
        var ids = results.Select(x => x.GetProperty("listing_id").GetInt64()).Distinct().ToArray();
        using var details = await Get("listings/batch?listing_ids=" + string.Join(",", ids) + "&includes=Images,Shop", ct);
        var rows = details.RootElement.GetProperty("results").EnumerateArray().ToArray();
        if (ids.Except(rows.Select(x => x.GetProperty("listing_id").GetInt64())).Any())
            throw new HttpRequestException("Etsy returned an incomplete detail batch. Collected pages are saved; Resume retries this page.");
        var source = "https://www.etsy.com/search?q=" + Uri.EscapeDataString(keywords);
        return new(rows.Select(x => MapListing(x, source)).ToArray(), total, results.Length);
    }
    private async Task<JsonDocument> Get(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await requests.WaitAsync(ct);
            HttpResponseMessage response;
            try
            {
                var delay = nextRequest - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://openapi.etsy.com/v3/application/" + path);
                request.Headers.Add("x-api-key", configuration["Etsy:Keystring"]!.Trim() + ":" + configuration["Etsy:SharedSecret"]!.Trim());
                request.Headers.Accept.ParseAdd("application/json");
                try { response = await http.SendAsync(request, ct); }
                finally { nextRequest = DateTimeOffset.UtcNow.AddMilliseconds(250); }
            }
            finally { requests.Release(); }
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 2)
                {
                    var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(2 * (attempt + 1));
                    if (retry > TimeSpan.FromSeconds(60)) throw new HttpRequestException("Etsy API quota reached. Wait for the quota reset, then Resume; collected products remain available.");
                    await Task.Delay(retry < TimeSpan.Zero ? TimeSpan.Zero : retry, ct);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException(response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Etsy API access was denied. Check backend credentials and app approval/access level in the Etsy developer portal.",
                        HttpStatusCode.TooManyRequests => "Etsy API rate or daily quota limit reached. Wait, then Resume the collection.",
                        HttpStatusCode.BadRequest => "Etsy rejected the search or pagination parameters. Narrow the search or check the shop ID.",
                        HttpStatusCode.NotFound => "Etsy could not find the requested shop or listing resource.",
                        _ => $"Etsy API returned HTTP {(int)response.StatusCode}. Collected products are saved; Resume to retry."
                    });
                try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
                catch (JsonException) { throw new HttpRequestException("Etsy returned an invalid API response. Resume to retry."); }
            }
        }
    }
    public static Product MapListing(JsonElement listing, string source)
    {
        static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? WebUtility.HtmlDecode(property.GetString() ?? "") : "";
        static bool Association(JsonElement value, string name, out JsonElement property) => value.TryGetProperty(name, out property) || value.TryGetProperty(name.ToLowerInvariant(), out property);
        var id = listing.GetProperty("listing_id").GetInt64();
        var price = ""; var currency = "";
        if (listing.TryGetProperty("price", out var money) && money.ValueKind == JsonValueKind.Object)
        {
            currency = Text(money, "currency_code");
            if (money.TryGetProperty("amount", out var amount) && amount.TryGetDecimal(out var number) && money.TryGetProperty("divisor", out var divisor) && divisor.TryGetDecimal(out var scale) && scale > 0)
                price = (number / scale).ToString("0.############################", CultureInfo.InvariantCulture);
        }
        var images = Array.Empty<string>();
        if (Association(listing, "Images", out var imageRows) && imageRows.ValueKind == JsonValueKind.Array)
            images = imageRows.EnumerateArray().Select(x => Text(x, "url_fullxfull") is { Length: > 0 } full ? full : Text(x, "url_570xN")).Where(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https").Distinct().ToArray();
        var store = Association(listing, "Shop", out var shop) && shop.ValueKind == JsonValueKind.Object ? Text(shop, "shop_name") : "";
        var material = listing.TryGetProperty("materials", out var materials) && materials.ValueKind == JsonValueKind.Array ? string.Join("; ", materials.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => WebUtility.HtmlDecode(x.GetString() ?? ""))) : "";
        return new(Text(listing, "title"), $"https://www.etsy.com/listing/{id}", images, material, store, price, currency, source);
    }
    public void Dispose() { http.Dispose(); requests.Dispose(); }
}
