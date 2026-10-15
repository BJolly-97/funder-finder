using System.Net.Http;
using FunderFinder.Models;

namespace FunderFinder.Services.Sources;

/// <summary>
/// One place funding calls can be fetched from — an RSS feed, an API, or a scraped
/// web page. To add a source, implement this and register it in
/// <see cref="FundingScanner.AllSources"/>.
/// </summary>
public interface IFundingSource
{
    /// <summary>Shown on the Sources tab and stamped on each call. Also the key for saved settings, so keep it stable.</summary>
    string Name { get; }

    string Description { get; }

    /// <summary>
    /// Whether this source's calls skip the keyword filter until you say otherwise on
    /// the Sources tab. For small or specialist sources whose schemes are open to
    /// materials work without saying so ("Research Fellowships", "any discipline") —
    /// the filter would otherwise hide nearly all of them.
    /// </summary>
    bool ShowAllByDefault => false;

    /// <summary>
    /// Fetches the source's current calls. Throwing is fine: the scanner catches it,
    /// reports it in the status line, and carries on with the other sources.
    /// Each call needs a stable <see cref="FundingCall.Id"/>; if left empty, one is
    /// derived from the source name and URL.
    /// </summary>
    Task<IReadOnlyList<FundingCall>> FetchAsync(HttpClient http, CancellationToken ct);
}
