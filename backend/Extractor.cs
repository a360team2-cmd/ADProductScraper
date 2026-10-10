using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ADProductScraper.Api;

public static class Extractor
{
    public static string Clean(string? value) => Regex.Replace(value ?? "", @"\s+", " ").Trim();
    public static string? Link(Uri source, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Contains("{width}")) value = Regex.Replace(value, @"\{width\}", "1024");
        if (!Uri.TryCreate(source, value, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        return new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri;
    }
    public static string Key(string url)
    {
        var uri = new Uri(url);
        var match = Regex.Match(uri.AbsolutePath, @"/(?:dp|gp/product)/([A-Z0-9]{10})|/listing/(\d+)|/itm/(?:[^/]+/)?(\d+)", RegexOptions.IgnoreCase);
        return match.Success ? uri.Host + ":" + match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value : uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + uri.Query;
    }
    public static void ValidateSelectors(Selectors s)
    {
        var doc = new HtmlParser().ParseDocument("<div></div>");
        foreach (var css in new[] { s.Card, s.Title, s.Link, s.Image, s.Price, s.Store, s.Material, s.Next })
        {
            if (css == null) throw new ArgumentException("CSS selectors must be strings, not null.");
            if (css.Length > 500) throw new ArgumentException("CSS selector must be 500 characters or shorter.");
            if (css.Length > 0) try { doc.QuerySelector(css); } catch { throw new ArgumentException($"Invalid CSS selector: {css}"); }
        }
    }
    public static ParsedPage Parse(string html, Uri source, Selectors? custom = null, bool detail = false)
    {
        var doc = new HtmlParser().ParseDocument(html);
        var selectedCurrency = CurrencySelection.Selected(doc);
        var collectionRoot = doc.QuerySelector(".t4s-main-collection-page");
        var isCollection = collectionRoot != null || source.AbsolutePath.StartsWith("/collections/", StringComparison.OrdinalIgnoreCase)
            && !source.AbsolutePath.Contains("/products/", StringComparison.OrdinalIgnoreCase)
            && doc.QuerySelector(".collection-content[data-section-type='collection-grid'], #CollectionAjaxContent") != null;
        var countText = Clean(collectionRoot?.QuerySelector(".t4s-active-filters__count .cp")?.TextContent).Replace(",", "");
        int? expectedProducts = int.TryParse(countText, out var count) && count >= 0 ? count : null;
        var products = new List<Product>(); var links = new HashSet<string>();
        var s = custom ?? new Selectors();
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try { using var json = JsonDocument.Parse(script.TextContent); Walk(json.RootElement, source, products, links); } catch (JsonException) { }
        }
        var cardCss = s.Card.Length > 0 ? s.Card : "[data-component-type='s-search-result'][data-asin], li.s-item, .s-card, [data-listing-id], [itemtype*='schema.org/Product'], .product_pod, .product-card, .product-item, .product-block, .product, .t4s-main-collection-page .t4s-product, [data-product-card], [data-product-id]:not(script):not(tolstoy-carousel)";
        if (!detail)
        {
            var structuredProducts = products.ToArray();
            products.Clear();
            foreach (var card in doc.QuerySelectorAll(cardCss))
            {
                var linkNode = Pick(card, s.Link, "a[href*='/dp/'], a[href*='/itm/'], a[href*='/listing/'], a.s-card__link, a.s-item__link, a[href*='/products/'], a[href]");
                var url = Link(source, linkNode?.GetAttribute("href"));
                if (url == null) continue;
                links.Add(url);
                var titleNode = Pick(card, s.Title, ".product_pod h3 a, h2, h3, .s-item__title, .s-card__title, [itemprop='name'], .product-title, .title, .product-block__title, .grid-product__title");
                var title = Clean(titleNode?.GetAttribute("title") ?? titleNode?.QuerySelector("a[title]")?.GetAttribute("title") ?? titleNode?.GetAttribute("content") ?? titleNode?.TextContent);
                if (title.Length == 0) title = Clean(linkNode?.GetAttribute("title") ?? card.QuerySelector("img")?.GetAttribute("alt"));
                if (title.Length == 0) continue;
                products.Add(Money(s.Price.Length > 0 ? Text(card, s.Price, s.Price) : ProductFields.Price(card), ProductFields.OldPrice(card), Text(card, "", "[itemprop='priceCurrency']"), source, title, linkUrl: url, images: Images(card, source, s.Image), material: (s.Material.Length > 0 ? Text(card, s.Material, s.Material) : ProductFields.Material(card, title)), store: s.Store.Length > 0 ? Text(card, s.Store, s.Store) : ProductFields.Store(card, source.Host)));
            }
            products.AddRange(structuredProducts);
        }
        else
        {
            var title = Text(doc.DocumentElement!, s.Title, "#productTitle, h1, [itemprop='name']");
            var material = s.Material.Length > 0 ? Text(doc.DocumentElement!, s.Material, s.Material) : ProductFields.Material(doc.DocumentElement!, title);
            var store = s.Store.Length > 0 ? Text(doc.DocumentElement!, s.Store, s.Store) : ProductFields.Store(doc.DocumentElement!, source.Host);
            var price = s.Price.Length > 0 ? Text(doc.DocumentElement!, s.Price, s.Price) : ProductFields.Price(doc.DocumentElement!);
            var images = Images(doc.DocumentElement!, source, s.Image.Length > 0 ? s.Image : ".product_page #product_gallery img, #landingImage, #imgBlkFront, [itemprop='image'], .ux-image-carousel-item img, img[data-index='0']");
            if (images.Length == 0 && Link(source, doc.QuerySelector("meta[property='og:image']")?.GetAttribute("content")) is { } og) images = [og];
            var structured = products.FirstOrDefault(p => Key(p.Url) == Key(source.AbsoluteUri)) ?? products.FirstOrDefault(p => p.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            products.Clear();
            var fallback = Money(price, ProductFields.OldPrice(doc.DocumentElement!), Text(doc.DocumentElement!, "", "[itemprop='priceCurrency']"), source, title, source.AbsoluteUri, images, material, store);
            if (fallback.OldPrice.Length == 0 && Pricing.ToNumber(fallback.Price) is { } now)
            {
                foreach (Match m in Regex.Matches(html, "\"compare_at_price\"\\s*:\\s*\"?(\\d+(?:\\.\\d+)?)"))
                {
                    var raw = decimal.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var candidate = new[] { raw / 100, raw }.FirstOrDefault(v => v > now && v < now * 20);
                    if (candidate > 0) { fallback = fallback with { OldPrice = Pricing.Format(candidate) }; break; }
                }
            }
            var merged = structured == null ? fallback : Merge(structured, fallback);
            if (fallback.Price.Length > 0) merged = merged with {
                Price = fallback.Price,
                Currency = fallback.Currency.Length > 0 ? fallback.Currency : merged.Currency,
                OldPrice = fallback.OldPrice.Length > 0 ? fallback.OldPrice : merged.OldPrice
            };
            if (merged.Title.Length > 0) products.Add(merged with { Url = source.AbsoluteUri });
        }
        var nextNode = Pick(doc.DocumentElement!, s.Next, "a[rel~='next'], link[rel~='next'], a[data-load-more]:not([data-is-prev]), a.s-pagination-next, a.pagination__next, a[aria-label='Next page'], a[aria-label='Next'], a.wt-action-group__item[aria-label*='Next'], .pagination a.next, .pager li.next a, a.next.page-numbers");
        // Breadcrumb ListItems are navigation, not product detail links.
        if (collectionRoot != null)
        {
            // This theme's JSON-LD is a separately paginated, truncated list.
            // The rendered collection grid is the authoritative product set.
            links = products.Select(product => product.Url).ToHashSet();
        }
        var next = nextNode?.GetAttribute("aria-disabled") == "true" || nextNode?.HasAttribute("disabled") == true ? null : Link(source, nextNode?.GetAttribute("href"));
        if (nextNode == null && s.Next.Length == 0)
        {
            // Sites vary the casing and wording of Next; scope numbered links
            // to pagination so category and product links cannot become pages.
            var candidates = doc.QuerySelectorAll("a[href]").Where(a => a.GetAttribute("aria-disabled") != "true" && !a.HasAttribute("disabled"));
            var forward = candidates.FirstOrDefault(a => Regex.IsMatch(Clean(a.GetAttribute("aria-label") ?? a.TextContent), @"^(?:next(?:\s+page)?|(?:load|show|view)\s+more(?:\s+products)?|[›»→])$", RegexOptions.IgnoreCase));
            next = Link(source, forward?.GetAttribute("href"));
            if (next == source.AbsoluteUri) next = null;
            if (next == null)
            {
                var current = doc.QuerySelector(".pagination [aria-current='page'], .pagination .active, .pager [aria-current='page'], nav[aria-label*='agination'] [aria-current='page']");
                if (int.TryParse(Clean(current?.TextContent), out var number))
                    next = Link(source, current?.ParentElement?.ParentElement?.QuerySelectorAll("a[href]").FirstOrDefault(a => Clean(a.TextContent) == (number + 1).ToString())?.GetAttribute("href"));
            }
        }
        var paginationEnded = false;
        if (!detail && Pagination.IsEbaySearch(source))
        {
            var nativeNext = doc.QuerySelector(".pagination__next, .srp-pagination a[rel='next'], .srp-pagination button[aria-label='Next page']");
            paginationEnded = nativeNext != null && (nativeNext.HasAttribute("disabled") || nativeNext.GetAttribute("aria-disabled") == "true"
                || nativeNext.ClassList.Contains("is-disabled"));
            var numbered = doc.QuerySelectorAll(".pagination a[href], .srp-pagination a[href]")
                .Select(a => Link(source, a.GetAttribute("href"))).Where(url => url != null).Select(url => new Uri(url!))
                .Where(target => target.GetLeftPart(UriPartial.Authority) == source.GetLeftPart(UriPartial.Authority)
                    && Pagination.IsEbaySearch(target) && Pagination.EbayPage(target) > Pagination.EbayPage(source))
                .OrderBy(Pagination.EbayPage).FirstOrDefault();
            if (numbered != null) { next = numbered.AbsoluteUri; paginationEnded = false; }
            else if (paginationEnded) next = null;
        }
        var visible = selectedCurrency.Length > 0 ? selectedCurrency : doc.QuerySelectorAll("[class*='price'], [itemprop='price'], [data-price], .a-price, .s-item__price").Select(x => Pricing.Detect(x.TextContent)).Where(x => x.Length > 0).GroupBy(x => x).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "";
        var merged2 = products.GroupBy(p => Key(p.Url)).Select(g => g.Aggregate(Merge)).ToList();
        // Decrum's collection script inserts .dc-was after page load. Read
        // the rendered prices rather than reproducing the store's formula or
        // assuming that a readable current price means pricing is complete.
        var requiresPriceRendering = !detail && doc.QuerySelectorAll("script").Any(script =>
            script.TextContent.Contains("dc-was") && script.TextContent.Contains("insertAdjacentHTML"))
            && doc.QuerySelectorAll(".t4s-product .t4s-product-price").Any(price => price.QuerySelector(".dc-was") == null);
        return new ParsedPage(FixCurrency(merged2, source, visible), links.ToList(), next, isCollection || !detail && Pagination.IsEbaySearch(source), expectedProducts, paginationEnded, requiresPriceRendering);
    }
    private static Product Money(string price, string old, string hint, Uri source, string title, string linkUrl, string[] images, string material, string store)
    {
        var (p, o, c) = Pricing.Build(price, old, hint);
        return new Product(title, linkUrl, images, material, store, p, c, source.AbsoluteUri, o);
    }
    // One store page normally shows one currency: use what the page visibly shows, else .pk => PKR.
    private static List<Product> FixCurrency(List<Product> list, Uri source, string visible)
    {
        var forced = visible.Length > 0 ? visible : source.Host.EndsWith(".pk", StringComparison.OrdinalIgnoreCase) ? "PKR" : "";
        return forced.Length == 0 ? list : list.Select(p => p.Price.Length > 0 ? p with { Currency = forced } : p).ToList();
    }
    public static Product Merge(Product first, Product extra) => first with
    {
        Title = first.Title.Length > 0 ? first.Title : extra.Title,
        Images = first.Images.Concat(extra.Images).Distinct().ToArray(),
        Material = first.Material.Length > 0 ? first.Material : extra.Material,
        Store = first.Store.Length > 0 ? first.Store : extra.Store,
        Price = first.Price.Length > 0 ? first.Price : extra.Price,
        OldPrice = first.OldPrice.Length > 0 ? first.OldPrice : extra.OldPrice,
        Currency = first.Currency.Length > 0 ? first.Currency : extra.Currency
    };
    private static IElement? Pick(IElement parent, string custom, string fallback) => parent.QuerySelector(custom.Length > 0 ? custom : fallback);
    private static string Text(IElement parent, string custom, string fallback)
    {
        var el = Pick(parent, custom, fallback);
        return Clean(el?.GetAttribute("content") ?? el?.TextContent);
    }
    private static string[] Images(IElement parent, Uri source, string css) => parent.QuerySelectorAll(css.Length > 0 ? css : "img, [data-src], [data-lazy-src]").Select(x => Link(source, x.GetAttribute("data-src") ?? x.GetAttribute("data-lazy-src") ?? x.GetAttribute("src") ?? x.GetAttribute("content"))).OfType<string>().Distinct().ToArray();
    private static string Value(JsonElement node, string key)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(key, out var value)) return "";
        return Scalar(value);
    }
    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => Clean(value.GetString()), JsonValueKind.Number => value.ToString(),
        JsonValueKind.Array => string.Join("; ", value.EnumerateArray().Select(Scalar).Where(x => x.Length > 0)),
        JsonValueKind.Object => Value(value, "name"), _ => ""
    };
    private static void Walk(JsonElement node, Uri source, List<Product> products, HashSet<string> links)
    {
        if (node.ValueKind == JsonValueKind.Array) { foreach (var child in node.EnumerateArray()) Walk(child, source, products, links); return; }
        if (node.ValueKind != JsonValueKind.Object) return;
        var type = Value(node, "@type");
        if (type.Split(';', StringSplitOptions.TrimEntries).Any(t => t is "Product" or "IndividualProduct" or "ProductGroup"))
        {
            var url = Link(source, Value(node, "url")) ?? source.AbsoluteUri;
            var images = new List<string>();
            if (node.TryGetProperty("image", out var image))
                foreach (var img in image.ValueKind == JsonValueKind.Array ? image.EnumerateArray().ToArray() : new[] { image })
                    if (Link(source, img.ValueKind == JsonValueKind.String ? img.GetString() : Value(img, "url")) is { } imageUrl) images.Add(imageUrl);
            var price = ""; var currency = ""; var seller = Value(node, "seller");
            if (node.TryGetProperty("offers", out var offers))
            {
                var offer = offers.ValueKind == JsonValueKind.Array ? offers.EnumerateArray().FirstOrDefault() : offers;
                price = Value(offer, "price");
                if (price.Length == 0) { price = Value(offer, "lowPrice"); var high = Value(offer, "highPrice"); if (high.Length > 0 && high != price) price += " – " + high; }
                if (price.Length == 0 && offer.ValueKind == JsonValueKind.Object && offer.TryGetProperty("priceSpecification", out var spec)) price = Value(spec, "price");
                currency = Value(offer, "priceCurrency"); if (seller.Length == 0) seller = Value(offer, "seller");
            }
            var material = Value(node, "material");
            if (material.Length == 0 && node.TryGetProperty("additionalProperty", out var props))
                foreach (var prop in props.ValueKind == JsonValueKind.Array ? props.EnumerateArray().ToArray() : new[] { props })
                    if (Value(prop, "name").TrimEnd('s').Equals("material", StringComparison.OrdinalIgnoreCase)) material = Value(prop, "value");
            var title = Value(node, "name");
            if (material.Length == 0) material = ProductFields.MaterialWords(title + ". " + Value(node, "description"));
            if (title.Length > 0) products.Add(Money(price, "", currency, source, title, url, images.Distinct().ToArray(), material, seller));
            links.Add(url);
        }
        if (type == "ListItem")
        {
            var raw = Value(node, "url");
            if (node.TryGetProperty("item", out var item)) raw = item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : Value(item, "url");
            if (Link(source, raw) is { } link) links.Add(link);
        }
        foreach (var child in node.EnumerateObject()) if (child.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object) Walk(child.Value, source, products, links);
    }
}
