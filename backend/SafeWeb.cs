using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace ADProductScraper.Api;

// DNS is checked inside ConnectCallback, so the checked address is the address actually connected to.
public enum PageKind { Ok, Captcha, Blocked }

public sealed class SafeWeb : IDisposable
{
    public static bool IsTemporaryError(string html) => Regex.IsMatch(html,
        @"<title[^>]*>\s*Error Page\s*\|\s*eBay\s*</title>", RegexOptions.IgnoreCase)
        && html.Contains("Something went wrong", StringComparison.OrdinalIgnoreCase);
    public static bool IsChallenge(int status, string html)
    {
        var lower = html.ToLowerInvariant();
        var title = Regex.Match(lower, @"<title[^>]*>(.*?)</title>", RegexOptions.Singleline).Groups[1].Value;
        if (Regex.IsMatch(title, @"robot check|just a moment|captcha|verify you are human|attention required|are you a robot|security check") || lower.Contains("validatecaptcha")) return true;
        return status is 403 or 429 or 503 && Regex.IsMatch(lower, @"captcha|challenge-platform|cf-chl|hcaptcha|g-recaptcha");
    }
    public static PageKind Classify(int status, string html)
    {
        if (IsTemporaryError(html)) return PageKind.Blocked;
        var lower = html.ToLowerInvariant();
        if (lower.Contains("captcha-delivery.com") || lower.Contains("datadome")) return PageKind.Blocked; // hard bot protection: a manual solve will not carry over
        if (IsChallenge(status, html)) return PageKind.Captcha;
        return status == 200 ? PageKind.Ok : PageKind.Blocked;
    }
    public static string BlockedMessage(Uri uri, int status, PageKind kind, bool afterSolve) => kind == PageKind.Captcha && !afterSolve
        ? $"{uri.Host} shows a CAPTCHA / bot check, so products could not be collected. Solve it in the browser window when it opens, or try another website."
        : $"{uri.Host} blocks automated collection ({(status == 200 ? "bot protection" : "HTTP " + status)}). It is protected against scraping, so products can't be collected from this website. Try another store.";
    private readonly HttpClient client;
    public SafeWeb()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = async (context, ct) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
                if (addresses.Length == 0 || addresses.Any(a => !IsPublic(a))) throw new HttpRequestException("Private or local network addresses are not allowed.");
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            }
        };
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(35) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AD Product Scraper/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,text/plain;q=0.9");
    }
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var b = address.GetAddressBytes();
        if (b.Length == 16) return (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] == 1 && (b[2] == 0x0d && b[3] == 0xb8 || b[2] == 0 && b[3] == 0));
        return !(b[0] is 0 or 10 or 127 || b[0] >= 224 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] >= 16 && b[1] <= 31 || b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2) || b[0] == 100 && b[1] >= 64 && b[1] <= 127 || b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100) || b[0] == 203 && b[1] == 0 && b[2] == 113);
    }
    public static Uri Validate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Host.Length == 0)
            throw new ArgumentException("Enter a public HTTP/HTTPS URL on a standard port.");
        if (uri.IsLoopback || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(uri.Host, out var ip) && !IsPublic(ip))
            throw new ArgumentException("Local and private network URLs are not allowed.");
        return uri;
    }
    public async Task<(int Status, string Html)> Fetch(Uri uri, CancellationToken ct, string? cookie = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(35));
        ct = deadline.Token;
        var origin = uri.GetLeftPart(UriPartial.Authority);
        for (var redirects = 0; redirects < 6; redirects++)
        {
            Validate(uri.AbsoluteUri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!string.IsNullOrEmpty(cookie)) request.Headers.TryAddWithoutValidation("Cookie", cookie);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                var next = new Uri(uri, location);
                if (next.GetLeftPart(UriPartial.Authority) != origin) throw new HttpRequestException("Redirect changes website. Paste the final website URL instead.");
                uri = next; continue;
            }
            if (response.Content.Headers.ContentLength > 8_000_000) throw new HttpRequestException("Page exceeds 8 MB limit.");
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var bytes = new byte[16384];
            int length;
            while ((length = await stream.ReadAsync(bytes, ct)) > 0)
            {
                if (buffer.Length + length > 8_000_000) throw new HttpRequestException("Page exceeds 8 MB limit.");
                await buffer.WriteAsync(bytes.AsMemory(0, length), ct);
            }
            var encoding = System.Text.Encoding.UTF8;
            try { if (response.Content.Headers.ContentType?.CharSet is { } charset) encoding = System.Text.Encoding.GetEncoding(charset.Trim('"')); } catch (ArgumentException) { }
            return ((int)response.StatusCode, encoding.GetString(buffer.ToArray()));
        }
        throw new HttpRequestException("Too many redirects.");
    }
    public void Dispose() => client.Dispose();
}

public sealed class RobotsRules
{
    private readonly List<(string Pattern, bool Allow)> rules = [];
    public static RobotsRules Parse(string text)
    {
        var result = new RobotsRules();
        var groups = new List<(List<string> Agents, List<(string, bool)> Rules)>();
        List<string> agents = []; List<(string, bool)> directives = [];
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim(); var colon = line.IndexOf(':'); if (colon < 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant(); var value = line[(colon + 1)..].Trim();
            if (key == "user-agent")
            {
                if (directives.Count > 0) { groups.Add((agents, directives)); agents = []; directives = []; }
                agents.Add(value.ToLowerInvariant());
            }
            else if (agents.Count > 0 && key is "allow" or "disallow") directives.Add((value, key == "allow"));
        }
        groups.Add((agents, directives));
        var specific = groups.Any(g => g.Agents.Any(a => a != "*" && "adproductscraper".StartsWith(a) && a.Length > 0));
        foreach (var group in groups.Where(g => g.Agents.Any(a => specific ? a != "*" && a.Length > 0 && "adproductscraper".StartsWith(a) : a == "*")))
            result.rules.AddRange(group.Rules.Where(r => r.Item1.Length > 0));
        return result;
    }
    public bool Allows(Uri uri) => BlockingRule(uri) == null;
    public string? BlockingRule(Uri uri)
    {
        var matches = rules.Where(r => Regex.IsMatch(uri.PathAndQuery, "^" + Regex.Escape(r.Pattern.TrimEnd('$')).Replace("\\*", ".*") + (r.Pattern.EndsWith('$') ? "$" : ""), RegexOptions.None, TimeSpan.FromMilliseconds(100))).ToList();
        if (matches.Count == 0) return null;
        var rule = matches.OrderByDescending(r => r.Pattern.Replace("*", "").Length).ThenByDescending(r => r.Allow).First();
        return rule.Allow ? null : rule.Pattern;
    }
}
