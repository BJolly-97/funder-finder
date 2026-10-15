using System.Net.Http;
using System.Text.RegularExpressions;
using FunderFinder.Models;

namespace FunderFinder.Services.Sources;

/// <summary>
/// UKRI Funding Finder: every research council (EPSRC, STFC, NERC, ...) plus
/// cross-council and Innovate UK calls. Reads the open + upcoming listing pages,
/// which carry funders, amounts and dates. (The site's RSS feed only holds the
/// 20 newest posts, so it can't give the full list.)
/// </summary>
public class UkriFundingFinderSource : IFundingSource
{
    private const string ListUrl = "https://www.ukri.org/opportunity/";
    private const string StatusQuery = "?filter_status%5B0%5D=open&filter_status%5B1%5D=upcoming";
    private const int MaxPages = 40;

    public string Name => "UKRI Funding Finder";

    public string Description => "Research council calls (EPSRC, STFC, NERC, ...) and cross-council programmes, mostly for UK research organisations (ukri.org).";

    public async Task<IReadOnlyList<FundingCall>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var calls = new List<FundingCall>();

        for (var page = 1; page <= MaxPages; page++)
        {
            if (page > 1) await Task.Delay(300, ct); // be polite
            var url = page == 1 ? ListUrl + StatusQuery : $"{ListUrl}page/{page}/{StatusQuery}";
            var doc = await HtmlText.LoadAsync(http, url, ct);
            if (doc is null) break;

            var items = doc.DocumentNode.SelectNodes("//div[contains(concat(' ', normalize-space(@class), ' '), ' type-opportunity ')]");
            if (items is null || items.Count == 0) break;

            foreach (var item in items)
                if (Parse(item) is { } call) calls.Add(call);
        }

        if (calls.Count == 0)
            throw new InvalidOperationException("no opportunities found — the page layout may have changed");

        return calls;
    }

    private static FundingCall? Parse(HtmlAgilityPack.HtmlNode item)
    {
        var link = item.SelectSingleNode(".//a[contains(@class,'ukri-funding-opp__link')]");
        var href = link?.GetAttributeValue("href", "") ?? "";
        var slug = Regex.Match(href, @"/opportunity/([^/]+)/?$");
        if (link is null || !slug.Success) return null;

        // Summary paragraphs: the first says what it funds, any after it are usually eligibility.
        var paragraphs = item.SelectNodes(".//div[contains(@class,'entry-content')]/p")?
            .Select(HtmlText.Clean).Where(p => p.Length > 0).ToList() ?? new List<string>();

        string Row(string label)
        {
            var dd = item.SelectSingleNode($".//dt[starts-with(normalize-space(), '{label}')]/following-sibling::dd[1]");
            return HtmlText.Clean(dd);
        }

        DateTime? RowDate(string label)
        {
            var time = item.SelectSingleNode($".//dt[starts-with(normalize-space(), '{label}')]/following-sibling::dd[1]//time");
            var iso = time?.GetAttributeValue("datetime", "");
            return DateTime.TryParse(iso, out var d) ? d : HtmlText.ParseUkDate(Row(label));
        }

        var maxAward = Row("Maximum award");
        var totalFund = Row("Total fund");
        var amount = !string.IsNullOrEmpty(maxAward) && maxAward.Contains('£') ? $"max {HtmlText.FormatMoney(maxAward)}"
            : !string.IsNullOrEmpty(totalFund) && totalFund.Contains('£') ? $"total {HtmlText.FormatMoney(totalFund)}"
            : null;

        return new FundingCall
        {
            Id = $"ukri-{slug.Groups[1].Value}",
            Title = HtmlText.Clean(link),
            Funder = ShortFunders(item),
            Summary = string.Join(" ", paragraphs),
            Eligibility = string.Join(" ", paragraphs.Skip(1)),
            Url = href,
            Amount = amount,
            Status = Row("Opportunity status"),
            Opens = RowDate("Opening date"),
            Deadline = RowDate("Closing date"),
        };
    }

    /// <summary>"Engineering and Physical Sciences Research Council (EPSRC)" → "EPSRC".</summary>
    private static string ShortFunders(HtmlAgilityPack.HtmlNode item)
    {
        var names = item.SelectNodes(".//a[contains(@class,'ukri-funder__link')]")?
            .Select(a => HtmlText.Clean(a))
            .Select(n => Regex.Match(n, @"\(([^)]+)\)\s*$") is { Success: true } m ? m.Groups[1].Value : n)
            .Distinct()
            .ToList() ?? new List<string>();
        return names.Count == 0 ? "UKRI" : string.Join(", ", names);
    }
}
