using System.Text.Json.Serialization;

namespace ADProductScraper.Api;

public record Selectors(string Card = "", string Title = "", string Link = "", string Image = "", string Price = "", string Store = "", string Material = "", string Next = "");
public record StartRequest(string Url, int MaxPages = 0, bool VisitDetails = true, Selectors? Selectors = null, bool Demo = false, bool BrowserMode = false, bool RespectRobots = true, string Currency = "", string Source = "website", string Keywords = "", long? EtsyShopId = null);
public record Product(string Title, string Url, string[] Images, string Material, string Store, string Price, string Currency, string SourcePage, string OldPrice = "");
public class CrawlJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public StartRequest Request { get; set; } = new("");
    public string Status { get; set; } = "Queued";
    public string Message { get; set; } = "Waiting to collect products";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    public string? Cursor { get; set; }
    public List<string> Visited { get; set; } = [];
    public List<Product> Products { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public string? FailureDetails { get; set; }
    public int? ExpectedProducts { get; set; }
    [JsonIgnore] public CancellationTokenSource Cancellation { get; set; } = new();
}
public record ParsedPage(List<Product> Products, List<string> DetailLinks, string? Next, bool IsCollection = false, int? ExpectedProducts = null, bool PaginationEnded = false, bool RequiresPriceRendering = false);
