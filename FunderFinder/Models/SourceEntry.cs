using FunderFinder.Services.Sources;

namespace FunderFinder.Models;

/// <summary>A row on the Sources tab: one registered source plus whether it's switched on.</summary>
public class SourceEntry
{
    public SourceEntry(IFundingSource source, bool isEnabled)
    {
        Source = source;
        IsEnabled = isEnabled;
    }

    public IFundingSource Source { get; }
    public string Name => Source.Name;
    public string Description => Source.Description;
    public bool IsEnabled { get; set; }
}
