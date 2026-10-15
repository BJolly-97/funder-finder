using System.Net.Http;
using FunderFinder.Models;

namespace FunderFinder.Services.Sources;

/// <summary>
/// Henry Royce Institute's own calls: the "Current Funding Opportunities" cards on
/// royce.ac.uk/funding (closed ones sit in a separate section and are skipped).
/// The cards carry no dates, so calls are marked "see page".
/// </summary>
public class RoyceSource : IFundingSource
{
    private const string PageUrl = "https://www.royce.ac.uk/funding/";

    public string Name => "Henry Royce Institute";

    public string Description => "Royce's own calls: access schemes, MATcelerate and other materials funding (royce.ac.uk).";

    public bool ShowAllByDefault => true;

    public async Task<IReadOnlyList<FundingCall>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var doc = await HtmlText.LoadAsync(http, PageUrl, ct)
                  ?? throw new InvalidOperationException("funding page not found");

        // The section whose heading is "Current Funding Opportunities".
        var section = doc.DocumentNode.SelectSingleNode(
            "//section[.//h3[contains(normalize-space(), 'Current Funding Opportunities')]]")
            ?? throw new InvalidOperationException("no 'Current Funding Opportunities' section — the page layout may have changed");

        var calls = new List<FundingCall>();
        foreach (var card in section.SelectNodes(".//div[contains(@class,'cta-block__item-content')][.//h4]") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
        {
            var title = HtmlText.Clean(card.SelectSingleNode(".//h4"));
            var href = card.SelectSingleNode(".//a[@href]")?.GetAttributeValue("href", "") ?? "";
            if (title.Length == 0 || href.Length == 0) continue;

            calls.Add(new FundingCall
            {
                Id = $"royce-{new Uri(new Uri(PageUrl), href).AbsolutePath.Trim('/').Replace('/', '-')}",
                Title = title,
                Funder = "Henry Royce Institute",
                Summary = HtmlText.Clean(card.SelectSingleNode(".//div[contains(@class,'cta-block__content')]")),
                Url = new Uri(new Uri(PageUrl), href).AbsoluteUri,
                Status = FundingCall.StatusSeePage,
            });
        }
        return calls;
    }
}
