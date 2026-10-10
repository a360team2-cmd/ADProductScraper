using System.Globalization;
using System.Text.RegularExpressions;

namespace ADProductScraper.Api;

public static class Pricing
{
    private static readonly Regex Money = new(
        @"(?<![\d.,])(?<c1>PKR|USD|GBP|EUR|INR|AED|Rs\.?|\$|£|€|₹)?\s*(?<n>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:[.,]\d+)?)(?!\s*%)\s*(?<c2>PKR|USD|GBP|EUR|INR|AED)?\b?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string Code(string symbol, string hint) => symbol.ToUpperInvariant().TrimEnd('.') switch
    {
        "RS" => hint == "INR" ? "INR" : "PKR",
        "$" => hint is "CAD" or "AUD" ? hint : "USD",
        "£" => "GBP", "€" => "EUR", "₹" => "INR",
        var x => x
    };

    // Currency actually shown in the visible text (symbol or code), or "".
    public static string Detect(string? text, string hint = "")
    {
        foreach (Match m in Money.Matches(text ?? ""))
        {
            var c = m.Groups["c1"].Success ? m.Groups["c1"].Value : m.Groups["c2"].Value;
            if (c.Length > 0) return Code(c, hint);
        }
        return "";
    }

    private static decimal? Number(string n)
    {
        n = n.Trim();
        if (n.Contains(',') && n.Contains('.')) n = n.LastIndexOf(',') > n.LastIndexOf('.') ? n.Replace(".", "").Replace(',', '.') : n.Replace(",", "");
        else if (n.Contains(',')) n = Regex.IsMatch(n, @",\d{3}(?!\d)") ? n.Replace(",", "") : n.Replace(',', '.');
        return decimal.TryParse(n, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public static string Format(decimal d) => d % 1 == 0 ? d.ToString("N0", CultureInfo.InvariantCulture) : d.ToString("N2", CultureInfo.InvariantCulture);

    public static decimal? ToNumber(string? text) => string.IsNullOrWhiteSpace(text) ? null : Number(text.Replace(" ", ""));

    // raw: current (sale) price text, possibly also containing the struck-through price.
    // old: optional separate text of the struck-through price.
    public static (string Price, string OldPrice, string Currency) Build(string? raw, string? old, string hint = "")
    {
        raw = Extractor.Clean(raw); old = Extractor.Clean(old); hint = (hint ?? "").Trim().ToUpperInvariant();
        var all = Money.Matches(raw).Cast<Match>().Select(m => (m, v: Number(m.Groups["n"].Value))).Where(x => x.v != null).ToList();
        var withCur = all.Where(x => x.m.Groups["c1"].Success || x.m.Groups["c2"].Success).ToList();
        var tokens = withCur.Count > 0 ? withCur : all;
        if (tokens.Count == 0) return ("", "", hint);
        var currency = Detect(raw, hint);
        if (currency.Length == 0) currency = hint;
        var values = tokens.Select(t => t.v!.Value).Distinct().OrderBy(v => v).ToList();
        var between = tokens.Count > 1 ? raw.Substring(tokens[0].m.Index + tokens[0].m.Length, Math.Max(0, tokens[1].m.Index - tokens[0].m.Index - tokens[0].m.Length)) : "";
        var isRange = tokens.Count > 1 && Regex.IsMatch(between, @"[-–—]|\bto\b", RegexOptions.IgnoreCase);
        string price, oldPrice = "";
        if (isRange) price = Format(values[0]) + " – " + Format(values[^1]);
        else if (values.Count > 1) { price = Format(values[0]); oldPrice = Format(values[^1]); }
        else price = Format(values[0]);
        if (oldPrice.Length == 0 && old.Length > 0 && !isRange)
        {
            var o = Money.Matches(old).Cast<Match>().Select(m => Number(m.Groups["n"].Value)).Where(v => v != null).Select(v => v!.Value).DefaultIfEmpty(0).Max();
            if (o > values[0]) oldPrice = Format(o);
        }
        return (price, oldPrice, currency);
    }
}
