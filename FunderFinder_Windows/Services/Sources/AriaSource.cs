using System.Net.Http;
using FunderFinder.Models;

namespace FunderFinder.Services.Sources;

/// <summary>
/// ARIA (Advanced Research and Invention Agency) open calls — programme requests
/// for proposals and opportunity seeds, each with its opportunity space and deadline.
/// </summary>
public class AriaSource : IFundingSource
{
    private const string PageUrl = "https://www.aria.org.uk/funding-opportunities/";

    public string Name => "ARIA";

    public string Description => "Advanced Research and Invention Agency: high-risk, high-reward programme calls (aria.org.uk).";

    public async Task<IReadOnlyList<FundingCall>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var doc = await HtmlText.LoadAsync(http, PageUrl, ct)
                  ?? throw new InvalidOperationException("funding page not found");

        var calls = new List<FundingCall>();
        foreach (var block in doc.DocumentNode.SelectNodes("//div[contains(concat(' ', normalize-space(@class), ' '), ' open-call ')][.//*[contains(@class,'open-call__title')]]")
                              ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
        {
            var title = HtmlText.Clean(block.SelectSingleNode(".//*[contains(@class,'open-call__title')]"));
            var href = block.SelectSingleNode(".//div[contains(@class,'open-call__links')]//a[@href]")?.GetAttributeValue("href", "") ?? "";
            if (title.Length == 0) continue;

            // Info items are label/value pairs: "Opportunity Space", "Programme Director", "Deadline...".
            string Info(string labelStart) => HtmlText.Clean(block.SelectSingleNode(
                $".//p[contains(@class,'open-call__info-title')][starts-with(normalize-space(), '{labelStart}')]/following-sibling::p[1]"));

            var space = Info("Opportunity Space");
            var type = HtmlText.Clean(block.SelectSingleNode(".//p[contains(@class,'open-call__type')]"));
            var url = href.Length > 0 ? new Uri(new Uri(PageUrl), href).AbsoluteUri : PageUrl;
            calls.Add(new FundingCall
            {
                Id = $"aria-{(href.Length > 0 ? href.Trim('/').Replace('/', '-') : title)}",
                Title = title,
                Funder = string.IsNullOrEmpty(space) ? "ARIA" : $"ARIA · {space}",
                Summary = HtmlText.Clean(block.SelectSingleNode(".//p[contains(@class,'open-call__text')]")),
                Url = url,
                Status = string.IsNullOrEmpty(type) ? "Open" : $"Open · {type}",
                Deadline = HtmlText.ParseUkDate(Info("Deadline")),
            });
        }

        if (calls.Count == 0)
            throw new InvalidOperationException("no open calls found — the page layout may have changed");
        return calls;
    }
}
