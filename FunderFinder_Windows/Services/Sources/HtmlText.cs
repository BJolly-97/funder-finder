using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace FunderFinder.Services.Sources;

/// <summary>Small helpers shared by the HTML-scraping sources.</summary>
internal static class HtmlText
{
    private static readonly CultureInfo UkCulture = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>Visible text of a node: entities decoded, whitespace collapsed. Empty for null.</summary>
    public static string Clean(HtmlNode? node)
        => node is null ? string.Empty : Clean(node.InnerText);

    public static string Clean(string? text)
        => text is null ? string.Empty : Regex.Replace(HtmlEntity.DeEntitize(text), @"\s+", " ").Trim();

    /// <summary>Parses UK-style dates like "2 December 2026", ignoring any trailing time text.</summary>
    public static DateTime? ParseUkDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text, @"\d{1,2}\s+[A-Za-z]+\s+\d{4}");
        return m.Success && DateTime.TryParse(m.Value, UkCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>"£1,500,000" → "£1.5m", "£305,000" → "£305k". Anything unparseable is returned cleaned but as-is.</summary>
    public static string FormatMoney(string? text)
    {
        var raw = Clean(text);
        var m = Regex.Match(raw, @"£\s?([\d,]+(?:\.\d+)?)");
        if (!m.Success || !decimal.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var amount))
            return raw;

        return amount switch
        {
            >= 1_000_000 => $"£{amount / 1_000_000m:0.#}m",
            >= 1_000 => $"£{amount / 1_000m:0.#}k",
            _ => $"£{amount:0}",
        };
    }

    public static async Task<HtmlDocument?> LoadAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // past the last page
        response.EnsureSuccessStatusCode();

        var doc = new HtmlDocument();
        doc.LoadHtml(await response.Content.ReadAsStringAsync(ct));
        return doc;
    }
}
