using System.Net.Http;
using System.Text.RegularExpressions;
using FunderFinder.Models;

namespace FunderFinder.Services.Sources;

/// <summary>
/// Faraday Institution battery funding: the fellowship and sprint signposts on its
/// opportunities page (PhD studentships and jobs on the same page are skipped).
/// No dates on the listing, so calls are marked "see page".
/// </summary>
public class FaradayInstitutionSource : IFundingSource
{
    private const string PageUrl = "https://www.faraday.ac.uk/opportunities/";

    public string Name => "Faraday Institution";

    public string Description => "Battery research funding: industry fellowships, entrepreneurial fellowships and industry sprints (faraday.ac.uk).";

    public bool ShowAllByDefault => true;

    public async Task<IReadOnlyList<FundingCall>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var doc = await HtmlText.LoadAsync(http, PageUrl, ct)
                  ?? throw new InvalidOperationException("opportunities page not found");

        var calls = new List<FundingCall>();
        foreach (var link in doc.DocumentNode.SelectNodes("//a[contains(@class,'signpost-link')]") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
        {
            var title = HtmlText.Clean(link.SelectSingleNode(".//h3"));
            if (!Regex.IsMatch(title, "fellowship|sprint", RegexOptions.IgnoreCase)) continue;

            var url = new Uri(new Uri(PageUrl), link.GetAttributeValue("href", "").Trim());
            calls.Add(new FundingCall
            {
                Id = $"faraday-{url.AbsolutePath.Trim('/').Replace('/', '-')}",
                Title = title,
                Funder = "Faraday Institution",
                Summary = HtmlText.Clean(link.SelectSingleNode(".//div[contains(@class,'signpost-text')]")),
                Url = url.AbsoluteUri,
                Status = FundingCall.StatusSeePage,
            });
        }

        if (calls.Count == 0)
            throw new InvalidOperationException("no fellowships or sprints found — the page layout may have changed");
        return calls;
    }
}
