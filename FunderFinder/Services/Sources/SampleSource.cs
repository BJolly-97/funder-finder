using System.Net.Http;
using FunderFinder.Models;

namespace FunderFinder.Services.Sources;

/// <summary>
/// Placeholder source returning made-up calls, so the UI has something to show
/// before any real source is wired up. Deadlines are relative to today so every
/// group (closing soon / open / rolling, plus one already closed) is exercised.
/// </summary>
public class SampleSource : IFundingSource
{
    public string Name => "Sample data";

    public string Description => "Made-up calls for trying out the app. Switch off once real sources are added.";

    public async Task<IReadOnlyList<FundingCall>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        await Task.Delay(400, ct); // Feel like a network fetch.

        var today = DateTime.Today;
        return new List<FundingCall>
        {
            new()
            {
                Id = "sample-1",
                Title = "Early Career Research Fellowship (sample)",
                Funder = "Example Research Council",
                Amount = "up to £400k",
                Deadline = today.AddDays(6),
                Summary = "Three-year fellowships for researchers within eight years of their PhD.",
                Url = "https://example.org/funding/early-career-fellowship",
            },
            new()
            {
                Id = "sample-2",
                Title = "Community Partnerships Small Grants (sample)",
                Funder = "Example Foundation",
                Amount = "£5k–£25k",
                Deadline = today.AddDays(12),
                Summary = "Seed funding for projects co-designed with community organisations.",
                Url = "https://example.org/funding/community-partnerships",
            },
            new()
            {
                Id = "sample-3",
                Title = "Interdisciplinary Network Awards (sample)",
                Funder = "Example Trust",
                Amount = "up to £60k",
                Deadline = today.AddDays(45),
                Summary = "Supports new networks bringing together two or more disciplines.",
                Url = "https://example.org/funding/network-awards",
            },
            new()
            {
                Id = "sample-4",
                Title = "Strategic Programme Grant (sample)",
                Funder = "Example Research Council",
                Amount = "£1m–£3m",
                Deadline = today.AddDays(110),
                Summary = "Large multi-site programmes addressing a strategic priority area.",
                Url = "https://example.org/funding/programme-grant",
            },
            new()
            {
                Id = "sample-5",
                Title = "Impact Acceleration Account (sample)",
                Funder = "Example University",
                Summary = "Rolling internal fund for knowledge exchange and impact activities.",
                Url = "https://example.org/funding/impact-acceleration",
            },
            new()
            {
                Id = "sample-6",
                Title = "Pilot Studies Scheme (sample, already closed)",
                Funder = "Example Charity",
                Amount = "up to £30k",
                Deadline = today.AddDays(-3),
                Summary = "Hidden from Opportunities because its deadline has passed.",
                Url = "https://example.org/funding/pilot-studies",
            },
        };
    }
}
