using System.Net.Http;
using System.Text.RegularExpressions;
using FunderFinder.Models;

namespace FunderFinder.Services.Sources;

/// <summary>
/// Royal Academy of Engineering research and international programmes. The
/// programmes index only names them, so this reads each programme page for its
/// "Application deadline". Programmes saying they're closed, or whose deadline has
/// passed, are skipped; ones with no date are kept as "see page".
/// </summary>
public class RoyalAcademyOfEngineeringSource : IFundingSource
{
    private const string BaseUrl = "https://raeng.org.uk";
    private const string IndexUrl = BaseUrl + "/programmes-and-prizes/programmes/";

    // Listed under the programmes menu but not funding calls.
    private static readonly string[] NotFunding = { "global-talent-visa", "meet-the-researchers" };

    public string Name => "Royal Academy of Engineering";

    public string Description => "RAEng research and industrial fellowships, chairs and international programmes (raeng.org.uk).";

    public bool ShowAllByDefault => true;

    public async Task<IReadOnlyList<FundingCall>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var index = await HtmlText.LoadAsync(http, IndexUrl, ct)
                    ?? throw new InvalidOperationException("programmes page not found");

        var paths = (index.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
            .Select(a => a.GetAttributeValue("href", ""))
            .Where(h => Regex.IsMatch(h, @"^/programmes-and-prizes/programmes/(uk-grants-and-prizes/support-for-research|international-programmes)/[^/]+/$"))
            .Where(h => !NotFunding.Any(h.Contains))
            .Distinct()
            .ToList();
        if (paths.Count == 0)
            throw new InvalidOperationException("no programme links found — the page layout may have changed");

        var calls = new List<FundingCall>();
        foreach (var path in paths)
        {
            await Task.Delay(300, ct); // be polite
            var page = await HtmlText.LoadAsync(http, BaseUrl + path, ct);
            if (page is null) continue;

            var text = HtmlText.Clean(page.DocumentNode.SelectSingleNode("//main") ?? page.DocumentNode);
            if (Regex.IsMatch(text, @"currently closed|not currently open|now closed", RegexOptions.IgnoreCase)) continue;

            var deadlineText = Regex.Match(text, @"(?:Application deadline|Deadline for applications|Applications close)[^0-9]{0,30}(\d{1,2}\s+[A-Za-z]+\s+\d{4})", RegexOptions.IgnoreCase);
            var deadline = deadlineText.Success ? HtmlText.ParseUkDate(deadlineText.Groups[1].Value) : null;
            if (deadline is { } d && d.Date < DateTime.Today) continue;

            var title = HtmlText.Clean(page.DocumentNode.SelectSingleNode("//h1"));
            var summary = page.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", "") ?? "";

            calls.Add(new FundingCall
            {
                Id = $"raeng-{path.TrimEnd('/').Split('/').Last()}",
                Title = title.Length > 0 ? title : path,
                Funder = "Royal Academy of Engineering",
                Summary = HtmlText.Clean(summary),
                Url = BaseUrl + path,
                Status = deadline.HasValue ? "Open" : FundingCall.StatusSeePage,
                Deadline = deadline,
            });
        }
        return calls;
    }
}
