using FunderFinder.Services.Sources;

namespace FunderFinder.Models;

/// <summary>A row on the Sources tab: one registered source, whether it's scanned, and whether it skips the filter.</summary>
public class SourceEntry
{
    public SourceEntry(IFundingSource source, bool isEnabled, bool showAll)
    {
        Source = source;
        IsEnabled = isEnabled;
        ShowAll = showAll;
    }

    public IFundingSource Source { get; }
    public string Name => Source.Name;
    public string Description => Source.Description;
    public bool IsEnabled { get; set; }

    /// <summary>Show every call from this source, matching or not.</summary>
    public bool ShowAll { get; set; }
}
