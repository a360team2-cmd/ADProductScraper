using AngleSharp.Dom;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace ADProductScraper.Api;

public static class CurrencySelection
{
    public static string Resolve(string? value, string url)
    {
        var code = (value ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0 && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var match = Regex.Match(uri.Query, @"[?&](?:currency|currency_code)=([a-z]{3})(?=&|$)", RegexOptions.IgnoreCase);
            if (match.Success) code = match.Groups[1].Value.ToUpperInvariant();
        }
        if (code.Length > 0 && !Regex.IsMatch(code, "^[A-Z]{3}$")) throw new ArgumentException("Currency must be a three-letter code such as PKR, USD or GBP.");
        return code;
    }

    public static string Selected(IParentNode document)
    {
        foreach (var form in document.QuerySelectorAll("form[action*='/localization']"))
        {
            var direct = form.QuerySelector("select[name='currency_code'] option:checked, input[name='currency_code']")?.GetAttribute("value");
            if (direct != null && Regex.IsMatch(direct, "^[A-Z]{3}$")) return direct;
            var country = form.QuerySelector("input[name='country_code'], select[name='country_code'] option:checked")?.GetAttribute("value");
            var option = form.QuerySelectorAll("[data-value], option").FirstOrDefault(node => (node.GetAttribute("data-value") ?? node.GetAttribute("value")) == country);
            var match = Regex.Match(option?.TextContent ?? "", @"\b[A-Z]{3}\b");
            if (match.Success) return match.Value;
        }
        return "";
    }

    public static async Task Apply(IPage page, string currency, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(currency)) return;
        ct.ThrowIfCancellationRequested();
        var result = await page.EvaluateAsync<string>("""
        code => {
          const matches = text => new RegExp('\\b' + code + '\\b').test(text || '');
          for (const form of document.querySelectorAll('form[action*="/localization"]')) {
            if (new URL(form.action, location.href).origin !== location.origin) continue;
            const direct = form.querySelector('[name="currency_code"]');
            const country = form.querySelector('[name="country_code"]');
            const candidates = Array.from(form.querySelectorAll('[data-value], option'));
            const current = candidates.find(n => (n.dataset.value || n.value) === country?.value);
            if (direct?.value === code || matches(current?.textContent)) return 'selected';
            const option = candidates.find(n => n.value === code || matches(n.textContent));
            if (!option) continue;
            const input = option.value === code && direct ? direct : country;
            if (!input) continue;
            input.value = option.dataset.value || option.value;
            document.documentElement.dataset.catalogCurrencyPending = code;
            HTMLFormElement.prototype.submit.call(form);
            return 'submitted';
          }
          return 'unsupported';
        }
        """, currency);
        if (result == "unsupported") throw new HttpRequestException($"The website's currency selector could not select {currency}. Choose a supported currency or use Website default; prices were not relabelled.");
        if (result == "selected") return;
        try
        {
            await page.WaitForFunctionAsync("""
            code => !document.documentElement.dataset.catalogCurrencyPending && Array.from(document.querySelectorAll('form[action*="/localization"]')).some(form => {
              if (form.querySelector('[name="currency_code"]')?.value === code) return true;
              const country = form.querySelector('[name="country_code"]')?.value;
              return Array.from(form.querySelectorAll('[data-value], option')).some(n =>
                (n.dataset.value || n.value) === country && new RegExp('\\b' + code + '\\b').test(n.textContent));
            })
            """, currency, new() { Timeout = 30000 });
        }
        catch (TimeoutException error) { throw new HttpRequestException($"The website did not confirm {currency} after changing currency. Resume to retry; prices were not relabelled.", error); }
        ct.ThrowIfCancellationRequested();
    }
}
