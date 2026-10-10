using AngleSharp.Dom;
using System.Text.RegularExpressions;

namespace ADProductScraper.Api;

public static class ProductFields
{
    // Exclude prices and material names that belong to navigation, ads or other products.
    private const string Excluded = "script, style, nav, footer, header, del, s, [hidden], [aria-hidden='true'], .related, .related-products, .recommendations, .recommended-products, .cross-sells, .upsells, [id*='recommend'], [class*='recommend'], [class*='related'], [class*='shipping'], [class*='installment']";
    private static IElement Content(IElement root)
    {
        var copy = (IElement)root.Clone(true);
        foreach (var node in copy.QuerySelectorAll(Excluded).ToArray()) node.Remove();
        return copy;
    }
    private static string Read(IElement? node) => Extractor.Clean(node?.GetAttribute("content") ?? node?.GetAttribute("data-price") ?? node?.GetAttribute("data-material") ?? node?.TextContent);
    public static string Material(IElement root, string title)
    {
        var content = Content(root);
        var explicitValue = content.QuerySelectorAll("[itemprop='material'], [data-material], .material, .po-material .po-break-word, .po-fabric_type .po-break-word, .po-outer_material .po-break-word").Select(Read).FirstOrDefault(x => x.Length > 0);
        if (explicitValue != null) return explicitValue;
        foreach (var label in content.QuerySelectorAll("th, dt, label, strong, b, span, div"))
        {
            if (!Regex.IsMatch(Read(label), @"^(?:materials?(?: type| composition)?|fabric(?: type| composition)?|composition|outer material|made of)\s*:?$", RegexOptions.IgnoreCase)) continue;
            var value = Read(label.NextElementSibling);
            if (value.Length == 0 && label.ParentElement is { } parent && Read(parent) == Read(label))
                value = Read(parent.NextElementSibling);
            if (value.Length is > 0 and < 200) return value;
        }
        foreach (var node in content.QuerySelectorAll("p, li, dd, td, div, span"))
        {
            var text = Regex.Replace(Read(node), @"[\u200e\u200f\u200b\u00a0]", " ").Trim();
            if (text.Length > 500) continue;
            var match = Regex.Match(text, @"^(?:materials?|fabric(?: composition)?|composition)\s*:\s*(.{1,180}?)(?:[.;]|$)", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.Trim();
        }
        var descriptions = content.QuerySelectorAll("[itemprop='description'], [class*='description'], [id*='description'], [class*='specification'], [id*='feature'], details, p, li").Select(Read);
        return MaterialWords(string.Join(". ", new[] { title }.Concat(descriptions)));
    }
    private static readonly string[] Marketplaces = ["amazon.", "ebay.", "etsy.", "aliexpress.", "daraz.", "temu.", "walmart.", "alibaba."];
    public static string Store(IElement root, string host)
    {
        var content = Content(root);
        foreach (var css in new[] { "#sellerProfileTriggerId", "#merchant-info a", "[tabular-attribute-name='Sold by'] .tabular-buybox-text", "[data-shop-name]", "[data-seller-name]", ".shop-name", ".store-name", ".s-item__seller-info-text", ".s-card__seller", "[itemprop='seller'] [itemprop='name']", "[itemprop='seller']", ".x-sellercard-atf__info__about-seller a", ".x-sellercard-atf__info__about-seller", ".x-sellercard-atf__info__name", "a[href*='/shop/']", "a[href*='/str/']" })
        {
            var node = content.QuerySelector(css);
            var v = Extractor.Clean(node?.GetAttribute("data-shop-name") ?? node?.GetAttribute("data-seller-name") ?? Read(node));
            if (v.Length is > 0 and < 100) return v;
        }
        var merchant = Regex.Replace(Read(content.QuerySelector("#merchant-info")), @"[\u200e\u200f]", "");
        var sold = Regex.Match(merchant, @"sold by\s+(.{2,80}?)(?:\s+and\s+|\.|$)", RegexOptions.IgnoreCase);
        if (sold.Success) return sold.Groups[1].Value.Trim();
        foreach (var css in new[] { "#bylineInfo", "[itemprop='brand']", ".product__vendor", ".product-brand", "a.brand", "[class*='vendor']" })
        {
            var v = Read(content.QuerySelector(css));
            v = Regex.Replace(v, @"^(?:Visit the\s+|Brand:\s*)|\s+Store$", "", RegexOptions.IgnoreCase).Trim();
            if (v.Length is > 0 and < 80) return v;
        }
        if (!Marketplaces.Any(m => host.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            var site = root.Owner?.QuerySelector("meta[property='og:site_name']")?.GetAttribute("content")?.Trim() ?? "";
            if (site.Length is > 0 and < 80) return site;
        }
        return "";
    }
    public static string MaterialWords(string text)
    {
        // Report only material words actually present; do not infer a fabric from product type.
        const string words = @"faux leather|synthetic leather|genuine leather|vegan leather|stainless steel|sterling silver|solid gold|solid wood|organic cotton|recycled polyester|cotton|linen|polyester|nylon|viscose|rayon|elastane|spandex|cashmere|wool|silk|denim|leather|suede|velvet|acrylic|bamboo|hemp|jute|canvas|ceramic|porcelain|silicone|rubber|aluminium|aluminum|brass|copper|titanium|oak|walnut|teak|rattan";
        var found = new List<string>();
        foreach (Match m in Regex.Matches(text, @"\b(?:\d{1,3}(?:[.,]\d+)?\s*%\s*)?(?:" + words + @")\b", RegexOptions.IgnoreCase))
        {
            var before = text.Substring(Math.Max(0, m.Index - 24), Math.Min(24, m.Index));
            var after = text.Substring(m.Index + m.Length, Math.Min(16, text.Length - m.Index - m.Length));
            if (Regex.IsMatch(before, @"\b(?:no|not|without|free of)\s+$", RegexOptions.IgnoreCase) || Regex.IsMatch(after, @"^[- ](?:free|look|effect)\b", RegexOptions.IgnoreCase)) continue;
            found.Add(m.Value);
        }
        return string.Join("; ", found.Distinct(StringComparer.OrdinalIgnoreCase));
    }
    public static string OldPrice(IElement root)
    {
        var copy = (IElement)root.Clone(true);
        foreach (var node in copy.QuerySelectorAll("script, style, nav, footer, header, .related, .related-products, .recommendations, [class*='recommend'], [class*='related']").ToArray()) node.Remove();
        var found = new List<string>();
        foreach (var node in copy.QuerySelectorAll("del, s, strike, [class*='price' i], [class*='compare' i], [class*='original' i], [class*='was' i], [style*='line-through' i], .a-text-price .a-offscreen"))
        {
            var cls = node.GetAttribute("class") ?? "";
            var struck = node.TagName is "DEL" or "S" or "STRIKE" || (node.GetAttribute("style") ?? "").Contains("line-through", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(cls, @"compare|original|(^|[-_\s])old([-_\s]|$)|(^|[-_\s])was([-_\s]|$)|regular|strike|before|retail|msrp|line-through|a-text-price", RegexOptions.IgnoreCase);
            if (!struck) continue;
            var value = Read(node);
            if (value.Length > 0 && value.Length < 60 && Regex.IsMatch(value, @"\d")) found.Add(value);
        }
        if (found.Count > 0) return string.Join(" ", found.Distinct());
        return "";
    }
    public static string Price(IElement root)
    {
        var content = Content(root);
        foreach (var price in content.QuerySelectorAll(".grid-product__price, .product__price, .product-single__price"))
        {
            if (price.Matches("del, s, strike, [class*='original'], [class*='compare']")) continue;
            var current = (IElement)price.Clone(true);
            foreach (var node in current.QuerySelectorAll("del, s, strike, [class*='original'], [class*='compare'], [class*='savings'], .visually-hidden").ToArray()) node.Remove();
            var value = Read(current);
            if (value.Length is > 0 and < 100 && Regex.IsMatch(value, @"\d")) return value;
        }
        foreach (var selector in new[] {
            ".price ins, .sale-price, .price__sale .price-item--sale, [data-sale-price]",
            ".a-price .a-offscreen, .s-item__price, .s-card__price, .currency-value, #corePrice_feature_div .a-offscreen, .x-price-primary, .price_color, [data-buy-box-region='price']",
            "[itemprop='price'], meta[property='product:price:amount'], meta[property='og:price:amount'], meta[itemprop='price']",
            "[data-price], .price .amount, .price-item--regular, .product-price, .t4s-product-price, .current-price, .price, [class*='productPrice']" })
        {
            foreach (var node in content.QuerySelectorAll(selector))
            {
                var value = Extractor.Clean(node.GetAttribute("data-sale-price") ?? Read(node));
                if (value.Length > 0 && value.Length < 100 && Regex.IsMatch(value, @"\d") && !Regex.IsMatch(value, @"\b(?:shipping|delivery|installment|monthly|/month|per month)\b", RegexOptions.IgnoreCase)) return value;
            }
        }
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in content.QuerySelectorAll("p, span, div, td, dd, strong"))
        {
            if (node.Children.Length > 0) continue;
            var value = Read(node);
            if (value.Length > 120 || Regex.IsMatch(value, @"\b(?:shipping|delivery|save|off|installment|monthly|month)\b", RegexOptions.IgnoreCase)) continue;
            var match = Regex.Match(value, @"(?:[$£€₹]|\b(?:USD|GBP|EUR|PKR|INR|AED|Rs\.?)\s*)\s*\d[\d.,]*(?:\s*[-–]\s*(?:[$£€₹]|USD|GBP|EUR|PKR|INR|AED|Rs\.?)?\s*\d[\d.,]*)?", RegexOptions.IgnoreCase);
            if (match.Success) candidates.Add(match.Value.Trim());
        }
        return candidates.Count == 1 ? candidates.Single() : "";
    }
}
