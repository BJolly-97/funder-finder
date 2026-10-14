using System.Net.Http;
using System.Text.RegularExpressions;
using FunderFinder.Models;

namespace FunderFinder.Services.Sources;

/// <summary>
/// Innovate UK competitions from the gov.uk Innovation Funding Service search page,
/// which lists only open and opening-soon competitions, 10 per page.
/// No feed or API exists, so this reads the page HTML.
/// </summary>
public class InnovationFundingServiceSource : IFundingSource
{
    private const string BaseUrl = "https://apply-for-innovation-funding.service.gov.uk";
    private const int MaxPages = 15;

    public string Name => "Innovation Funding Service";

    public string Description => "Innovate UK competitions, mostly for UK businesses (apply-for-innovation-funding.service.gov.uk).";

    public async Task<IReadOnlyList<FundingCall>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var calls = new List<FundingCall>();

        for (var page = 0; page < MaxPages; page++)
        {
            if (page > 0) await Task.Delay(300, ct); // be polite
            var doc = await HtmlText.LoadAsync(http, $"{BaseUrl}/competition/search?page={page}", ct);
            if (doc is null) break;

            var items = doc.DocumentNode.SelectNodes("//li[h2/a[contains(@href,'/competition/')]]");
            if (items is null || items.Count == 0) break;

            foreach (var li in items)
                if (Parse(li) is { } call) calls.Add(call);

            if (doc.DocumentNode.SelectSingleNode("//a[@rel='next']") is null) break;
        }

        if (calls.Count == 0)
            throw new InvalidOperationException("no competitions found — the page layout may have changed");

        return calls;
    }

    private static FundingCall? Parse(HtmlAgilityPack.HtmlNode li)
    {
        var link = li.SelectSingleNode("h2/a");
        var href = link?.GetAttributeValue("href", "") ?? "";
        var idMatch = Regex.Match(href, @"/competition/(\d+)/");
        if (link is null || !idMatch.Success) return null;

        var summary = HtmlText.Clean(li.SelectSingleNode("h2/following-sibling::div[1]"));
        var eligibility = HtmlText.Clean(li.SelectSingleNode("h3[normalize-space()='Eligibility']/following-sibling::div[1]"));
        // The status heading is whichever h3 isn't "Eligibility" ("Open now", "Opening soon", ...).
        var status = HtmlText.Clean(li.SelectSingleNode("h3[normalize-space()!='Eligibility']"));

        DateTime? opens = null, closes = null;
        foreach (var dt in li.SelectNodes(".//dl/dt") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
        {
            var value = HtmlText.ParseUkDate(HtmlText.Clean(dt.SelectSingleNode("following-sibling::dd[1]")));
            var label = HtmlText.Clean(dt);
            if (label.StartsWith("Opens", StringComparison.OrdinalIgnoreCase)) opens = value;
            else if (label.StartsWith("Closes", StringComparison.OrdinalIgnoreCase)) closes = value;
        }

        return new FundingCall
        {
            Id = $"ifs-{idMatch.Groups[1].Value}",
            Title = HtmlText.Clean(link),
            Funder = "Innovate UK",
            Summary = summary,
            Eligibility = eligibility,
            Url = BaseUrl + href,
            Amount = ExtractAmount(summary),
            Status = status,
            Opens = opens,
            Deadline = closes,
        };
    }

    /// <summary>Summaries usually say "a share of up to £5 million" — pull that out if present.</summary>
    private static string? ExtractAmount(string summary)
    {
        var m = Regex.Match(summary, @"up to £\s?[\d,.]+\s*(?:million|billion|m|k)?", RegexOptions.IgnoreCase);
        return m.Success ? m.Value.Replace(" million", "m").Replace(" billion", "bn") : null;
    }
}
