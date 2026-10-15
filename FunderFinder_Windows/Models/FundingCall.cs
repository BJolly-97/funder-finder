using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FunderFinder.Models;

/// <summary>
/// The section a call lists under. Declared in display order, so the enum value
/// doubles as the sort key; within a section, calls rank by relevance.
/// </summary>
public enum DeadlineBucket
{
    ClosingSoon,
    General, // later deadlines, rolling, and not-yet-dated calls
    Closed,
}

/// <summary>A single funding opportunity, as returned by an <see cref="Services.Sources.IFundingSource"/>.</summary>
public class FundingCall : INotifyPropertyChanged
{
    /// <summary>Days before the deadline at which a call counts as "closing soon".</summary>
    public const int ClosingSoonDays = 14;

    /// <summary>
    /// Days a call stays in Opportunities after you first had it on screen, before
    /// moving to Seen. Set from <see cref="AppData.SeenAfterDays"/> at startup.
    /// </summary>
    public static int SeenAfterDays { get; set; } = 7;

    private bool _isShortlisted;
    private bool _isDismissed;
    private bool _includeInExport = true;
    private DateTime? _firstViewed;

    /// <summary>Stable identity across scans — used to merge re-fetched calls with saved ones.</summary>
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Funder { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Eligibility { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    /// <summary>Free text, since funders phrase this every possible way ("up to £50k", "£1.2m total", ...).</summary>
    public string? Amount { get; set; }

    /// <summary>As the source words it, e.g. "Open now", "Opening soon", "Upcoming".</summary>
    public string? Status { get; set; }

    public DateTime? Opens { get; set; }

    /// <summary>Closing date, or null for rolling/open-ended calls.</summary>
    public DateTime? Deadline { get; set; }

    public string SourceName { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }

    public bool IsShortlisted
    {
        get => _isShortlisted;
        set
        {
            // Newly shortlisted calls start out ticked for the next PDF export.
            if (value && !_isShortlisted) IncludeInExport = true;
            _isShortlisted = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Ticked on the Shortlist tab: goes into the next PDF export.</summary>
    public bool IncludeInExport
    {
        get => _includeInExport;
        set { _includeInExport = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// When this call was first on screen in Opportunities (window open, tab showing) —
    /// not when it was scanned, so calls don't age out while you're away.
    /// </summary>
    public DateTime? FirstViewed
    {
        get => _firstViewed;
        set { _firstViewed = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNew)); OnPropertyChanged(nameof(IsSeen)); }
    }

    /// <summary>Viewed long enough ago to have moved from Opportunities to Seen.</summary>
    [JsonIgnore]
    public bool IsSeen => FirstViewed.HasValue && FirstViewed.Value.Date.AddDays(SeenAfterDays) <= DateTime.Today;

    /// <summary>Hidden by hand as not relevant. Kept (not deleted) so a re-scan doesn't bring it back.</summary>
    public bool IsDismissed
    {
        get => _isDismissed;
        set { _isDismissed = value; OnPropertyChanged(); }
    }

    // --- Keyword match (recomputed from the current profile, never saved) ---------

    [JsonIgnore] public int MatchScore { get; private set; }
    [JsonIgnore] public bool IsMatch { get; private set; }
    [JsonIgnore] public bool IsStrongMatch { get; private set; }
    [JsonIgnore] public IReadOnlyList<string> MatchedTerms { get; private set; } = Array.Empty<string>();

    /// <summary>Listed only because its source is set to "show everything", not because of keywords.</summary>
    [JsonIgnore] public bool IsShownBySource { get; private set; }

    [JsonIgnore]
    public string MatchLabel
    {
        get
        {
            if (!IsShownBySource)
                return MatchedTerms.Count == 0 ? "No keyword matches" : string.Join(" · ", MatchedTerms);
            // Listed because the source is set to show everything; any (boost-only) hits still shown.
            return MatchedTerms.Count == 0
                ? $"All {SourceName} calls shown"
                : $"{string.Join(" · ", MatchedTerms)} · all {SourceName} calls shown";
        }
    }

    public void SetMatch(int score, bool isMatch, bool isStrong, IReadOnlyList<string> terms, bool shownBySource = false)
    {
        MatchScore = score;
        IsMatch = isMatch;
        IsStrongMatch = isStrong;
        MatchedTerms = terms;
        IsShownBySource = shownBySource;
        RefreshDerived();
    }

    /// <summary>Status for sources whose listings carry no dates — the label then points to the call's page.</summary>
    public const string StatusSeePage = "Open — dates on the call page";

    // --- Derived display values -------------------------------------------------

    [JsonIgnore]
    public DeadlineBucket Bucket =>
        !Deadline.HasValue ? DeadlineBucket.General
        : Deadline.Value.Date < DateTime.Today ? DeadlineBucket.Closed
        : Deadline.Value.Date <= DateTime.Today.AddDays(ClosingSoonDays) ? DeadlineBucket.ClosingSoon
        : DeadlineBucket.General;

    /// <summary>String form of <see cref="Bucket"/> for grouping — WPF group header triggers can't reliably match an enum.</summary>
    [JsonIgnore]
    public string BucketName => Bucket.ToString();

    [JsonIgnore]
    public int BucketOrder => (int)Bucket;

    /// <summary>Closing within two weeks — for styling the date pill (the Mac app binds to this).</summary>
    [JsonIgnore]
    public bool IsClosingSoon => Bucket == DeadlineBucket.ClosingSoon;

    /// <summary>Non-nullable deadline for sorting within a group.</summary>
    [JsonIgnore]
    public DateTime DeadlineSortKey => Deadline ?? DateTime.MaxValue;

    [JsonIgnore]
    public string DeadlineLabel =>
        // No date on an announced-but-not-open call means "not published yet", not open-ended.
        !Deadline.HasValue ? (Status == StatusSeePage ? "See page" : IsNotYetOpen || IsUpcomingStatus ? "TBC" : "Rolling")
        : Bucket == DeadlineBucket.Closed ? "Closed"
        : $"{Deadline:d MMM yy}";

    /// <summary>Not open yet — the caption then says when it opens.</summary>
    [JsonIgnore]
    public bool IsNotYetOpen => Opens.HasValue && Opens.Value > DateTime.Now;

    private bool IsUpcomingStatus => Status is { } s &&
        (s.Contains("upcoming", StringComparison.OrdinalIgnoreCase) || s.Contains("soon", StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public string Caption
    {
        get
        {
            var parts = new List<string> { Funder };
            if (!string.IsNullOrWhiteSpace(Amount)) parts.Add(Amount!);
            if (IsNotYetOpen) parts.Add($"opens {Opens:d MMM}");
            return string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }

    /// <summary>"ukri.org ↗" — the site the call's page is on; the full address shows on hover.</summary>
    [JsonIgnore]
    public string LinkLabel => Uri.TryCreate(Url, UriKind.Absolute, out var uri)
        ? $"{(uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host)} ↗"
        : string.Empty;

    /// <summary>Not looked at yet, or first looked at today — shown as a "New" badge.</summary>
    [JsonIgnore]
    public bool IsNew => !FirstViewed.HasValue || FirstViewed.Value.Date == DateTime.Today;

    [JsonIgnore]
    public string TitleToolTip => string.IsNullOrWhiteSpace(Eligibility)
        ? "Open the call's web page"
        : $"Eligibility: {Eligibility}\n\nClick to open the call's web page";

    /// <summary>Takes the latest scraped details from a fresh fetch, keeping this call's own flags.</summary>
    public void UpdateFrom(FundingCall fresh)
    {
        Title = fresh.Title;
        Funder = fresh.Funder;
        Summary = fresh.Summary;
        Eligibility = fresh.Eligibility;
        Url = fresh.Url;
        Amount = fresh.Amount;
        Status = fresh.Status;
        Opens = fresh.Opens;
        Deadline = fresh.Deadline;
        RefreshDerived();
    }

    /// <summary>Re-raises every binding, e.g. so buckets move on as days pass.</summary>
    public void RefreshDerived() => OnPropertyChanged(string.Empty);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
