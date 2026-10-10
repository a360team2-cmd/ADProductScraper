namespace ADProductScraper.Api;

public static class DemoPages
{
    public static string Get(Uri uri)
    {
        if (uri.AbsolutePath.StartsWith("/products/"))
        {
            var id = int.Parse(uri.Segments.Last());
            var names = new[] { "Ribbed ceramic vase", "Linen cushion cover", "Oak serving board", "Woven cotton throw", "Stoneware coffee cup", "Brass candle holder" };
            var materials = new[] { "Ceramic", "Linen", "Oak wood", "Cotton", "Stoneware", "Brass" };
            var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["@type"] = "Product", ["name"] = names[id - 1], ["url"] = uri.AbsoluteUri,
                ["image"] = new[] { $"https://placehold.co/480x480/e9e5df/3b4946?text=Sample+{id}" },
                ["material"] = materials[id - 1],
                ["offers"] = new { price = $"{18 + id * 7}.00", priceCurrency = "USD", seller = new { name = "Demo Studio" } }
            });
            return $"<html><body><script type='application/ld+json'>{json}</script><h1>{names[id - 1]}</h1></body></html>";
        }
        var page = uri.Query.Contains("2") ? 2 : 1;
        var ids = page == 1 ? new[] { 1, 2, 3 } : new[] { 3, 4, 5, 6 };
        return "<html><body>" + string.Join("", ids.Select(id => $"<article class='product-card'><a href='/products/{id}'><h2>Sample product {id}</h2></a></article>")) + (page == 1 ? "<a rel='next' href='/search?page=2'>Next</a>" : "") + "</body></html>";
    }
}
