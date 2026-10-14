using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FunderFinder.Models;

/// <summary>
/// How a call groups by its closing date. Declared in display order, so the enum
/// value doubles as the sort key.
/// </summary>
public enum DeadlineBucket
{
    ClosingSoon,
    Open,
    Rolling,
    Closed,
}

/// <summary>A single funding opportunity, as returned by an <see cref="Services.Sources.IFundingSource"/>.</summary>
public class FundingCall : INotifyPropertyChanged
{
    /// <summary>Days before the deadline at which a call counts as "closing soon".</summary>
    public const int ClosingSoonDays = 14;

    private bool _isShortlisted;
    private bool _isDismissed;

    /// <summary>Stable identity across scans — used to merge re-fetched calls with saved ones.</summary>
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Funder { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    /// <summary>Free text, since funders phrase this every possible way ("up to £50k", "£1.2m total", ...).</summary>
    public string? Amount { get; set; }

    /// <summary>Closing date, or null for rolling/open-ended calls.</summary>
    public DateTime? Deadline { get; set; }

    public string SourceName { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }

    public bool IsShortlisted
    {
        get => _isShortlisted;
        set { _isShortlisted = value; OnPropertyChanged(); OnPropertyChanged(nameof(StarGlyph)); }
    }

    /// <summary>Hidden by hand as not relevant. Kept (not deleted) so a re-scan doesn't bring it back.</summary>
    public bool IsDismissed
    {
        get => _isDismissed;
        set { _isDismissed = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public DeadlineBucket Bucket =>
        !Deadline.HasValue ? DeadlineBucket.Rolling
        : Deadline.Value.Date < DateTime.Today ? DeadlineBucket.Closed
        : Deadline.Value.Date <= DateTime.Today.AddDays(ClosingSoonDays) ? DeadlineBucket.ClosingSoon
        : DeadlineBucket.Open;

    /// <summary>String form of <see cref="Bucket"/> for grouping — WPF group header triggers can't reliably match an enum.</summary>
    [JsonIgnore]
    public string BucketName => Bucket.ToString();

    [JsonIgnore]
    public int BucketOrder => (int)Bucket;

    /// <summary>Non-nullable deadline for sorting within a group.</summary>
    [JsonIgnore]
    public DateTime DeadlineSortKey => Deadline ?? DateTime.MaxValue;

    [JsonIgnore]
    public string DeadlineLabel => Bucket switch
    {
        DeadlineBucket.Rolling => "Rolling",
        DeadlineBucket.Closed => "Closed",
        _ => $"{Deadline:d MMM yy}",
    };

    [JsonIgnore]
    public string Caption => string.IsNullOrWhiteSpace(Amount) ? Funder : $"{Funder} · {Amount}";

    [JsonIgnore]
    public string StarGlyph => IsShortlisted ? "★" : "☆"; // ★ / ☆

    /// <summary>Takes the latest scraped details from a fresh fetch, keeping this call's own flags.</summary>
    public void UpdateFrom(FundingCall fresh)
    {
        Title = fresh.Title;
        Funder = fresh.Funder;
        Summary = fresh.Summary;
        Url = fresh.Url;
        Amount = fresh.Amount;
        Deadline = fresh.Deadline;
        RefreshDerived();
    }

    /// <summary>Re-raises every binding, e.g. so buckets move on as days pass.</summary>
    public void RefreshDerived() => OnPropertyChanged(string.Empty);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
