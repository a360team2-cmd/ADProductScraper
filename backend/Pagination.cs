using System.Text.RegularExpressions;

namespace ADProductScraper.Api;

public static class Pagination
{
    public static bool IsEbaySearch(Uri uri) => Regex.IsMatch(uri.Host, @"(^|\.)ebay\.", RegexOptions.IgnoreCase)
        && uri.AbsolutePath.StartsWith("/sch/", StringComparison.OrdinalIgnoreCase);
    public static int EbayPage(Uri uri)
    {
        var match = Regex.Match(uri.Query, @"[?&]_pgn=(\d+)(?=&|$)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : 1;
    }
    // Browser scrolling can stop before every batch loads. Probe the next page
    // regardless of transport, but stop if a store ignores page and repeats results.
    public static string? Next(Uri uri, ParsedPage page, bool hasNewProducts)
    {
        if (IsEbaySearch(uri))
        {
            if (page.PaginationEnded) return null;
            var current = EbayPage(uri);
            if (page.Next != null && Uri.TryCreate(page.Next, UriKind.Absolute, out var target)
                && target.GetLeftPart(UriPartial.Authority) == uri.GetLeftPart(UriPartial.Authority)
                && IsEbaySearch(target) && EbayPage(target) > current) return target.AbsoluteUri;
            if (!hasNewProducts || page.Products.Count == 0 && page.DetailLinks.Count == 0 || current == int.MaxValue) return null;
            var query = Regex.Replace(uri.Query, @"([?&])_pgn=\d+(?=&|$)", m => m.Groups[1].Value + "_pgn=" + (current + 1), RegexOptions.IgnoreCase);
            if (query == uri.Query) query += (query.Length > 1 ? "&" : "?") + "_pgn=" + (current + 1);
            return uri.GetLeftPart(UriPartial.Path) + query;
        }
        if (page.Next != null) return page.Next;
        if ((page.Products.Count == 0 && page.DetailLinks.Count == 0) || !hasNewProducts) return null;
        // A product detail page must not be treated as a one-item results page.
        if (page.Products.Count == 1 && Extractor.Key(page.Products[0].Url) == Extractor.Key(uri.AbsoluteUri)) return null;
        var match = Regex.Match(uri.Query, @"([?&])(_pgn|page|p|pg|pageNumber|pageIndex)=(\d+)(?=&|$)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            if (!int.TryParse(match.Groups[3].Value, out var number) || number == int.MaxValue) return null;
            return uri.GetLeftPart(UriPartial.Path) + uri.Query.Remove(match.Index, match.Length)
                .Insert(match.Index, $"{match.Groups[1].Value}{match.Groups[2].Value}={number + 1}");
        }
        var parameter = Regex.IsMatch(uri.Host, @"(^|\.)ebay\.", RegexOptions.IgnoreCase) ? "_pgn" : "page";
        return uri.GetLeftPart(UriPartial.Path) + (uri.Query.Length > 1 ? uri.Query + "&" : "?") + parameter + "=2";
    }
}
