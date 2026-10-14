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
    /// <summary>Shown on the Sources tab and stamped on each call. Also the key for on/off state, so keep it stable.</summary>
    string Name { get; }

    string Description { get; }

    /// <summary>
    /// Fetches the source's current calls. Throwing is fine: the scanner catches it,
    /// reports it in the status line, and carries on with the other sources.
    /// Each call needs a stable <see cref="FundingCall.Id"/>; if left empty, one is
    /// derived from the source name and URL.
    /// </summary>
    Task<IReadOnlyList<FundingCall>> FetchAsync(HttpClient http, CancellationToken ct);
}
