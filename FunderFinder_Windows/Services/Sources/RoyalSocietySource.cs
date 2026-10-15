using System.Net.Http;
using FunderFinder.Models;

namespace FunderFinder.Services.Sources;

/// <summary>
/// Royal Society grant schemes from its grant listings page — one card per scheme,
/// tagged Open or Closed, usually with a "Closing ..." date. Only open ones are kept.
/// </summary>
public class RoyalSocietySource : IFundingSource
{
    private const string PageUrl = "https://royalsociety.org/grants/search/grant-listings/";

    public string Name => "Royal Society";

    public string Description => "Royal Society fellowships, industry fellowships and international schemes (royalsociety.org).";

    public bool ShowAllByDefault => true;

    public async Task<IReadOnlyList<FundingCall>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var doc = await HtmlText.LoadAsync(http, PageUrl, ct)
                  ?? throw new InvalidOperationException("grant listings page not found");

        var cards = doc.DocumentNode.SelectNodes("//article[contains(@class,'card--grant')]");
        if (cards is null || cards.Count == 0)
            throw new InvalidOperationException("no grant cards found — the page layout may have changed");

        var calls = new List<FundingCall>();
        foreach (var card in cards)
        {
            var status = HtmlText.Clean(card.SelectSingleNode(".//span[contains(@class,'card__tag')]"));
            if (!status.StartsWith("Open", StringComparison.OrdinalIgnoreCase)) continue;

            var href = card.SelectSingleNode(".//a[contains(@class,'card__link')]")?.GetAttributeValue("href", "") ?? "";
            var url = new Uri(new Uri(PageUrl), href);
            calls.Add(new FundingCall
            {
                Id = $"rs-{url.AbsolutePath.Trim('/').Replace('/', '-')}",
                Title = HtmlText.Clean(card.SelectSingleNode(".//*[contains(@class,'card__title')]")),
                Funder = "Royal Society",
                // Descriptions are cut short on the listing ("…"); the full text is on the scheme page.
                Summary = HtmlText.Clean(card.SelectSingleNode(".//div[contains(@class,'card__desc')]")),
                Url = url.AbsoluteUri,
                Status = "Open",
                Deadline = HtmlText.ParseUkDate(HtmlText.Clean(card.SelectSingleNode(".//div[contains(@class,'card__meta')]"))),
            });
        }
        return calls;
    }
}
