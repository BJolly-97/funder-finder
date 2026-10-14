using System.Collections.ObjectModel;
using System.Net.Http;
using FunderFinder.Models;
using FunderFinder.Services.Sources;

namespace FunderFinder.Services;

public record ScanResult(int Added, int Refreshed, IReadOnlyList<string> Errors);

/// <summary>Runs the enabled sources and merges what they return into the saved calls.</summary>
public static class FundingScanner
{
    /// <summary>Every source the app knows about. Register new sources here.</summary>
    public static readonly IReadOnlyList<IFundingSource> AllSources = new IFundingSource[]
    {
        new SampleSource(),
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
    /// but keep their shortlisted/dismissed flags; unknown ones are added.
    /// Must be awaited from the UI thread, since it modifies a bound collection.
    /// </summary>
    public static async Task<ScanResult> ScanAsync(
        IReadOnlyList<IFundingSource> sources, ObservableCollection<FundingCall> calls, CancellationToken ct = default)
    {
        var fetches = await Task.WhenAll(sources.Select(source => FetchOneAsync(source, ct)));

        var byId = new Dictionary<string, FundingCall>();
        foreach (var call in calls)
            byId.TryAdd(call.Id, call);

        int added = 0, refreshed = 0;
        foreach (var fetch in fetches)
        {
            foreach (var fresh in fetch.Calls)
            {
                fresh.SourceName = fetch.Source.Name;
                if (string.IsNullOrWhiteSpace(fresh.Id))
                    fresh.Id = $"{fetch.Source.Name}|{fresh.Url}";

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
                    added++;
                }
            }
        }

        var errors = fetches.Where(f => f.Error is not null).Select(f => $"{f.Source.Name}: {f.Error}").ToList();
        return new ScanResult(added, refreshed, errors);
    }

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
