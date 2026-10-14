using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.RegularExpressions;
using FunderFinder.Models;
using FunderFinder.Services.Sources;

namespace FunderFinder.Services;

public record ScanResult(IReadOnlyList<FundingCall> Added, int Refreshed, int Duplicates, IReadOnlyList<string> Errors);

/// <summary>Runs the enabled sources and merges what they return into the saved calls.</summary>
public static class FundingScanner
{
    /// <summary>
    /// Every source the app knows about. Register new sources here. Order matters
    /// for de-duplication: when two sources list the same call, the earlier one wins.
    /// </summary>
    public static readonly IReadOnlyList<IFundingSource> AllSources = new IFundingSource[]
    {
        new InnovationFundingServiceSource(),
        new UkriFundingFinderSource(),
        new RoyceSource(),
        new RoyalAcademyOfEngineeringSource(),
        new RoyalSocietySource(),
        new LeverhulmeSource(),
        new FaradayInstitutionSource(),
        new AriaSource(),
        new HorizonEuropeSource(),
    };

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FunderFinder/0.1 (personal desktop app)");
        return client;
    }

    private record SourceFetch(IFundingSource Source, IReadOnlyList<FundingCall> Calls, string? Error);

    /// <summary>
    /// Fetches all <paramref name="sources"/> in parallel, then merges into
    /// <paramref name="calls"/>: known calls (matched by Id) take the fresh details
    /// but keep their shortlisted/dismissed flags; unknown ones are added. A call
    /// whose title another source already supplied (e.g. an Innovate UK competition
    /// listed on both IFS and UKRI) is skipped.
    /// Must be awaited from the UI thread, since it modifies a bound collection.
    /// </summary>
    public static async Task<ScanResult> ScanAsync(
        IReadOnlyList<IFundingSource> sources, ObservableCollection<FundingCall> calls, CancellationToken ct = default)
    {
        var ordered = AllSources.Where(sources.Contains).ToList();
        var fetches = await Task.WhenAll(ordered.Select(source => FetchOneAsync(source, ct)));

        var byId = new Dictionary<string, FundingCall>();
        foreach (var call in calls)
            byId.TryAdd(call.Id, call);

        // Titles claimed so far, mapped to the source that claimed them. Seeded from saved
        // calls so a duplicate stays suppressed even when only one source is re-scanned.
        // Only cross-source repeats count — one source can list two calls with the same title.
        var titleOwners = new Dictionary<string, string>();
        foreach (var call in calls)
            titleOwners.TryAdd(NormaliseTitle(call.Title), call.SourceName);

        var added = new List<FundingCall>();
        int refreshed = 0, duplicates = 0;
        foreach (var fetch in fetches)
        {
            foreach (var fresh in fetch.Calls)
            {
                fresh.SourceName = fetch.Source.Name;
                if (string.IsNullOrWhiteSpace(fresh.Id))
                    fresh.Id = $"{fetch.Source.Name}|{fresh.Url}";

                var titleKey = NormaliseTitle(fresh.Title);
                if (titleOwners.TryGetValue(titleKey, out var ownerSource) && ownerSource != fresh.SourceName)
                {
                    duplicates++;
                    continue;
                }
                titleOwners[titleKey] = fresh.SourceName;

                if (byId.TryGetValue(fresh.Id, out var existing))
                {
                    existing.UpdateFrom(fresh);
                    refreshed++;
                }
                else
                {
                    fresh.FirstSeen = DateTime.Now;
                    calls.Add(fresh);
                    byId[fresh.Id] = fresh;
                    added.Add(fresh);
                }
            }
        }

        var errors = fetches.Where(f => f.Error is not null).Select(f => $"{f.Source.Name}: {f.Error}").ToList();
        return new ScanResult(added, refreshed, duplicates, errors);
    }

    private static readonly Dictionary<string, string> NumberWords = new()
    {
        ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4", ["five"] = "5", ["six"] = "6",
        ["seven"] = "7", ["eight"] = "8", ["nine"] = "9", ["ten"] = "10", ["eleven"] = "11", ["twelve"] = "12",
        ["first"] = "1", ["second"] = "2", ["third"] = "3", ["fourth"] = "4", ["fifth"] = "5",
    };

    private static readonly HashSet<string> IgnoredTitleWords = new()
    {
        "the", "a", "an", "and", "of", "for", "to", "in", "on", "round", "rd", "ukri",
    };

    /// <summary>
    /// The set of words in a title, order ignored, so the same call worded differently
    /// by two sites compares equal: "Battery Innovation: Feasibility Studies Rd 3" and
    /// "Battery innovation: feasibility studies round three", or "Investor Partnerships –
    /// Battery Innovation Programme" and "Battery Innovation Programme: investor partnerships".
    /// </summary>
    private static string NormaliseTitle(string title)
        => string.Join(" ", Regex.Matches(title.ToLowerInvariant(), "[a-z]+|[0-9]+")
            .Select(m => NumberWords.GetValueOrDefault(m.Value, m.Value))
            .Where(w => !IgnoredTitleWords.Contains(w))
            .Distinct()
            .Order());

    private static async Task<SourceFetch> FetchOneAsync(IFundingSource source, CancellationToken ct)
    {
        try
        {
            return new SourceFetch(source, await source.FetchAsync(Http, ct), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // One broken source (site redesign, timeout, ...) shouldn't sink the whole scan.
            return new SourceFetch(source, Array.Empty<FundingCall>(), ex.Message);
        }
    }
}
