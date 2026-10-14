using System.Net.Http;
using FunderFinder.Models;

namespace FunderFinder.Services.Sources;

/// <summary>
/// Leverhulme Trust schemes from "Schemes at a glance". Schemes marked "Currently
/// closed" are skipped; open ones carry a closing date, or none if they're rolling
/// (e.g. Research Project Grants).
/// </summary>
public class LeverhulmeSource : IFundingSource
{
    private const string BaseUrl = "https://www.leverhulme.ac.uk";
    private const string PageUrl = BaseUrl + "/schemes-at-a-glance";

    public string Name => "Leverhulme Trust";

    public string Description => "Leverhulme research project grants, fellowships and prizes, open to any discipline (leverhulme.ac.uk).";

    public bool ShowAllByDefault => true;

    public async Task<IReadOnlyList<FundingCall>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var doc = await HtmlText.LoadAsync(http, PageUrl, ct)
                  ?? throw new InvalidOperationException("schemes page not found");

        var rows = doc.DocumentNode.SelectNodes("//div[contains(@class,'views-row')][.//div[contains(@class,'views-field-title')]]");
        if (rows is null || rows.Count == 0)
            throw new InvalidOperationException("no schemes found — the page layout may have changed");

        var calls = new List<FundingCall>();
        foreach (var row in rows)
        {
            if (HtmlText.Clean(row.SelectSingleNode(".//div[contains(@class,'views-field-field-scheme-closed')]"))
                    .Contains("closed", StringComparison.OrdinalIgnoreCase))
                continue;

            var link = row.SelectSingleNode(".//div[contains(@class,'views-field-title')]//a");
            var href = link?.GetAttributeValue("href", "") ?? "";
            if (link is null || href.Length == 0) continue;

            string Field(string name) => HtmlText.Clean(row.SelectSingleNode($".//div[contains(@class,'views-field-field-{name}')]//div[contains(@class,'field-content')]"));

            var value = Field("listing-value");
            calls.Add(new FundingCall
            {
                Id = $"leverhulme-{href.Trim('/')}",
                Title = HtmlText.Clean(link),
                Funder = "Leverhulme Trust",
                Summary = Field("listing-text"),
                Url = new Uri(new Uri(BaseUrl), href).AbsoluteUri,
                Amount = value.Contains('£') ? value : null,
                Status = "Open",
                Deadline = HtmlText.ParseUkDate(Field("closing-date")),
            });
        }
        return calls;
    }
}
