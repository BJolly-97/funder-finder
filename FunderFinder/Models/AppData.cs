using System.Collections.ObjectModel;

namespace FunderFinder.Models;

public class AppData
{
    /// <summary>Every call seen so far, including dismissed ones (so they stay hidden after a re-scan).</summary>
    public ObservableCollection<FundingCall> Calls { get; set; } = new();

    /// <summary>Names of sources switched off on the Sources tab. Anything not listed is on.</summary>
    public List<string> DisabledSources { get; set; } = new();

    /// <summary>
    /// Your choices for "show everything from this source" (true = skip the keyword
    /// filter). Sources not listed use their own default.
    /// </summary>
    public Dictionary<string, bool> SourceShowAll { get; set; } = new();

    public KeywordProfile KeywordProfile { get; set; } = KeywordProfile.CreateDefault();

    /// <summary>Whether the Filters tab's Sources / Keywords sections are open — remembered between runs.</summary>
    public bool SourcesSectionOpen { get; set; } = true;
    public bool KeywordsSectionOpen { get; set; } = true;

    /// <summary>Show calls that didn't match the keyword filter too (dimmed), to check nothing's being missed.</summary>
    public bool ShowNonMatching { get; set; }

    public DateTime? LastScan { get; set; }

    /// <summary>Hour of day (local time) for the automatic daily scan.</summary>
    public int DailyScanHour { get; set; } = 9;

    /// <summary>Days a call stays in Opportunities after you first see it, before moving to Seen.</summary>
    public int SeenAfterDays { get; set; } = 7;

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 380;
    public double WindowHeight { get; set; } = 600;
}
