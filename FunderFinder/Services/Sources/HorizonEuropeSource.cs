using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using FunderFinder.Models;

namespace FunderFinder.Services.Sources;

/// <summary>
/// Horizon Europe topics from the EU Funding &amp; Tenders portal's public search API
/// (the same one the portal's own search page uses). Fetches every open and
/// forthcoming topic, then keeps those with a deadline still ahead — the API's
/// status flag lags, so a few long-closed topics still claim to be open.
/// Search results carry titles and call codes but not descriptions, so the keyword
/// filter works on those.
/// </summary>
public class HorizonEuropeSource : IFundingSource
{
    private const string SearchUrl = "https://api.tech.ec.europa.eu/search-api/prod/rest/search?apiKey=SEDIA&text=***";
    private const string TopicUrl = "https://ec.europa.eu/info/funding-tenders/opportunities/portal/screen/opportunities/topic-details/";
    private const int PageSize = 100;
    private const int MaxPages = 15;

    // Portal codes: 31094501 forthcoming, 31094502 open; 43108390 Horizon Europe; type 1/2 = call topics.
    private const string Query =
        """{"bool":{"must":[{"terms":{"type":["1","2"]}},{"terms":{"status":["31094501","31094502"]}},{"terms":{"frameworkProgramme":["43108390"]}}]}}""";

    private const string UkEligibility =
        "UK organisations can apply as Horizon Europe is open to the UK as an associated country — a few EIC and space topics are restricted, so check the topic's conditions.";

    // Identifier part → readable programme area (HORIZON-CL4-2027-... → "Cluster 4: Digital, Industry & Space").
    private static readonly (string Code, string Name)[] Areas =
    {
        ("CL1", "Cluster 1: Health"), ("HLTH", "Cluster 1: Health"),
        ("CL2", "Cluster 2: Culture, Creativity & Inclusive Society"),
        ("CL3", "Cluster 3: Civil Security"),
        ("CL4", "Cluster 4: Digital, Industry & Space"),
        ("CL5", "Cluster 5: Climate, Energy & Mobility"),
        ("CL6", "Cluster 6: Food, Bioeconomy & Environment"),
        ("EIC", "European Innovation Council"), ("ERC", "European Research Council"),
        ("MSCA", "Marie Skłodowska-Curie Actions"), ("INFRA", "Research Infrastructures"),
        ("MISS", "EU Missions"), ("WIDERA", "Widening Participation"), ("EIE", "European Innovation Ecosystems"),
        ("JU", "Joint Undertaking partnership"), ("JTI", "Joint Undertaking partnership"),
    };

    public string Name => "Horizon Europe";

    public string Description => "EU research and innovation topics, open to UK organisations as an associated country (ec.europa.eu Funding & Tenders portal).";

    public async Task<IReadOnlyList<FundingCall>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var calls = new Dictionary<string, FundingCall>();
        int? total = null;

        for (var page = 1; page <= MaxPages && (total is null || (page - 1) * PageSize < total); page++)
        {
            if (page > 1) await Task.Delay(300, ct); // be polite
            using var doc = await SearchAsync(http, page, ct);
            total ??= doc.RootElement.GetProperty("totalResults").GetInt32();

            foreach (var result in doc.RootElement.GetProperty("results").EnumerateArray())
                if (Parse(result) is { } call)
                    calls.TryAdd(call.Id, call); // the same topic can appear once per language variant
        }

        if (total is null or 0)
            throw new InvalidOperationException("the EU search returned nothing — the API may have changed");
        return calls.Values.ToList();
    }

    private static async Task<JsonDocument> SearchAsync(HttpClient http, int page, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        void AddJson(string name, string json)
        {
            var part = new StringContent(json);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            form.Add(part, name, name + ".json");
        }
        AddJson("query", Query);
        AddJson("languages", """["en"]""");
        AddJson("sort", """{"order":"ASC","field":"deadlineDate"}""");

        using var response = await http.PostAsync($"{SearchUrl}&pageSize={PageSize}&pageNumber={page}", form, ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
    }

    private static FundingCall? Parse(JsonElement result)
    {
        if (!result.TryGetProperty("metadata", out var meta)) return null;

        List<string> Values(string key) => meta.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.ToString()).Where(s => s.Length > 0).ToList()
            : new List<string>();
        string First(string key) => Values(key).FirstOrDefault() ?? string.Empty;

        var identifier = First("identifier");
        var title = First("title");
        if (identifier.Length == 0 || title.Length == 0) return null;

        // Multi-stage and cut-off topics list several deadlines; the next one ahead is what matters.
        var deadlines = Values("deadlineDate").Select(s => DateTime.TryParse(s, out var d) ? d.Date : (DateTime?)null).OfType<DateTime>().ToList();
        var next = deadlines.Where(d => d >= DateTime.Today).OrderBy(d => d).Cast<DateTime?>().FirstOrDefault();
        if (deadlines.Count > 0 && next is null) return null; // every deadline has passed

        var area = Areas.FirstOrDefault(a => Regex.IsMatch(identifier, $@"(^|-){a.Code}(-|$)")).Name;
        var actionType = First("typesOfAction");
        var later = deadlines.Count(d => d > next);
        var summary = string.Join(" · ", new[]
        {
            identifier,
            actionType,
            later > 0 ? $"{later} later cut-off{(later == 1 ? "" : "s")}" : null,
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new FundingCall
        {
            Id = $"eu-{identifier}",
            Title = HtmlText.Clean(title),
            Funder = string.IsNullOrEmpty(area) ? "Horizon Europe" : $"Horizon Europe · {area}",
            Summary = summary,
            Eligibility = UkEligibility,
            Url = TopicUrl + Uri.EscapeDataString(identifier),
            Status = First("status") == "31094501" ? "Forthcoming" : "Open",
            Opens = DateTime.TryParse(First("startDate"), out var start) ? start.Date : null,
            Deadline = next,
        };
    }
}
