using System.Collections.ObjectModel;

namespace FunderFinder.Models;

public class AppData
{
    /// <summary>Every call seen so far, including dismissed ones (so they stay hidden after a re-scan).</summary>
    public ObservableCollection<FundingCall> Calls { get; set; } = new();

    /// <summary>Names of sources switched off on the Sources tab. Anything not listed is on.</summary>
    public List<string> DisabledSources { get; set; } = new();

    public DateTime? LastScan { get; set; }

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 360;
    public double WindowHeight { get; set; } = 540;
}
