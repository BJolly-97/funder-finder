using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using FunderFinder.Models;
using FunderFinder.Native;
using FunderFinder.Services;
using FunderFinder.Windows;

namespace FunderFinder;

public partial class MainWindow : Window
{
    private enum PanelView { Opportunities, Shortlist, Seen, Filters }

    private AppData? _data;
    private List<SourceEntry> _sources = new();
    private bool _isScanning;
    private DispatcherTimer? _timer;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Tint comes from Theme.xaml. Kept nearly opaque: with a light base, too much
        // desktop showing through muddies the colour and the text contrast.
        var tint = (Color)FindResource("WindowBackgroundColor");
        AcrylicHelper.EnableAcrylic(this, tint.R, tint.G, tint.B, 235);

        _data = DataStore.Load();
        RemoveRetiredSampleData(_data);
        FundingCall.SeenAfterDays = _data.SeenAfterDays;
        DataContext = _data;

        ((CollectionViewSource)Resources["OpportunitiesView"]).Source = _data.Calls;
        ((CollectionViewSource)Resources["ShortlistView"]).Source = _data.Calls;
        ((CollectionViewSource)Resources["SeenView"]).Source = _data.Calls;

        _sources = FundingScanner.AllSources
            .Select(s => new SourceEntry(s, !_data.DisabledSources.Contains(s.Name),
                _data.SourceShowAll.TryGetValue(s.Name, out var showAll) ? showAll : s.ShowAllByDefault))
            .ToList();
        SourcesList.ItemsSource = _sources;
        KeywordGroupsList.ItemsSource = _data.KeywordProfile.Groups;
        ShowNonMatchingCheck.IsChecked = _data.ShowNonMatching;
        ShowOnlyNewCheck.IsChecked = _data.ShowOnlyNew;

        if (_data.WindowLeft.HasValue && _data.WindowTop.HasValue)
        {
            Left = _data.WindowLeft.Value;
            Top = _data.WindowTop.Value;
        }
        Width = _data.WindowWidth;
        Height = _data.WindowHeight;

        UpdateLastScanLabel();

        SetView(PanelView.Opportunities);
        ApplyFilterSectionState();
        ApplyFilter();
        RefreshViews();

        // Every few minutes: re-sort (buckets depend on today's date, in case the app
        // sits open across midnight) and run the daily scan once it's due.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _timer.Tick += async (_, _) =>
        {
            RefreshViews();
            UpdateLastScanLabel(); // "today" becomes "yesterday" at midnight
            if (IsDailyScanDue()) await RunScanAsync(automatic: true);
        };
        _timer.Start();

        if (IsDailyScanDue()) _ = RunScanAsync(automatic: true);
    }

    /// <summary>The placeholder source from the first skeleton is gone; drop its made-up calls.</summary>
    private static void RemoveRetiredSampleData(AppData data)
    {
        foreach (var call in data.Calls.Where(c => c.Id.StartsWith("sample-")).ToList())
            data.Calls.Remove(call);
        data.DisabledSources.Remove("Sample data");
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Background app: closing just tucks it away on the taskbar (where the new-calls badge
        // can show). Real exit is in the tray menu.
        e.Cancel = true;
        TuckAway();
    }

    public void SaveNow()
    {
        if (_data is null) return;
        // While minimised, Left/Top report Windows' off-screen parking spot — save the real size and place.
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            _data.WindowLeft = bounds.Left;
            _data.WindowTop = bounds.Top;
            _data.WindowWidth = bounds.Width;
            _data.WindowHeight = bounds.Height;
        }
        DataStore.Save(_data);
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => TuckAway();

    /// <summary>
    /// Minimise to the taskbar rather than vanishing into the tray: the taskbar button (your
    /// pinned icon) is where the new-calls badge appears, and clicking it brings the panel back.
    /// </summary>
    public void TuckAway()
    {
        SaveNow();
        WindowState = WindowState.Minimized;
    }

    // --- Taskbar badge ---------------------------------------------------------------

    private int _badgeCount;

    /// <summary>
    /// Shows (or clears, for 0) a small green badge with the number of new calls on the
    /// taskbar button. Set after a scan you weren't watching; cleared when you open the window.
    /// </summary>
    private void SetNewCallsBadge(int count)
    {
        _badgeCount = count;
        if (count <= 0)
        {
            TaskbarInfo.Overlay = null;
            TaskbarInfo.Description = "Funder Finder";
            return;
        }

        var label = count > 9 ? "9+" : count.ToString();
        var text = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            label.Length > 1 ? 8.5 : 10.5, (Brush)FindResource("FrameTextBrush"), 1.0);

        var badge = new DrawingGroup();
        using (var dc = badge.Open())
        {
            // Leaf-green disc with a cream rim, number in the middle (drawn on a 16px square).
            dc.DrawEllipse((Brush)FindResource("ShortlistBrush"), new Pen((Brush)FindResource("FrameTextBrush"), 1.2),
                new Point(8, 8), 7.2, 7.2);
            dc.DrawText(text, new Point(8 - text.Width / 2, 8 - text.Height / 2));
        }
        TaskbarInfo.Overlay = new DrawingImage(badge);
        TaskbarInfo.Description = $"Funder Finder — {count} new call{Plural(count)}";
    }

    /// <summary>Shows the panel and raises it above other windows, without pinning it on top.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!IsVisible) Show();

        Topmost = true;
        Activate();
        Topmost = false;
    }

    // --- Views ------------------------------------------------------------------

    private void ShowOpportunities_Click(object sender, RoutedEventArgs e) => SetView(PanelView.Opportunities);
    private void ShowShortlist_Click(object sender, RoutedEventArgs e) => SetView(PanelView.Shortlist);
    private void ShowSeen_Click(object sender, RoutedEventArgs e) => SetView(PanelView.Seen);
    private void ShowFilters_Click(object sender, RoutedEventArgs e) => SetView(PanelView.Filters);

    private PanelView _currentView = PanelView.Opportunities;

    private void SetView(PanelView view)
    {
        var changed = view != _currentView;
        _currentView = view;
        OpportunitiesPanel.Visibility = view == PanelView.Opportunities ? Visibility.Visible : Visibility.Collapsed;
        ShortlistPanel.Visibility = view == PanelView.Shortlist ? Visibility.Visible : Visibility.Collapsed;
        SeenPanel.Visibility = view == PanelView.Seen ? Visibility.Visible : Visibility.Collapsed;
        FiltersPanel.Visibility = view == PanelView.Filters ? Visibility.Visible : Visibility.Collapsed;

        if (changed)
            FadeInPanel(view switch
            {
                PanelView.Shortlist => ShortlistPanel,
                PanelView.Seen => SeenPanel,
                PanelView.Filters => FiltersPanel,
                _ => OpportunitiesPanel,
            });

        // PillButtonStyle styles whichever tab is tagged "Active".
        OpportunitiesButton.Tag = view == PanelView.Opportunities ? "Active" : null;
        ShortlistButton.Tag = view == PanelView.Shortlist ? "Active" : null;
        SeenButton.Tag = view == PanelView.Seen ? "Active" : null;
        FiltersButton.Tag = view == PanelView.Filters ? "Active" : null;

        MarkOpportunitiesViewed();
    }

    // --- Search ------------------------------------------------------------------

    private string[] _searchWords = Array.Empty<string>();
    private DispatcherTimer? _searchDebounce;

    private bool IsSearching => _searchWords.Length > 0;

    /// <summary>Every word typed must appear somewhere in the call's visible text (any order, any case).</summary>
    private bool PassesSearch(FundingCall call)
    {
        if (!IsSearching) return true;
        var text = $"{call.Title} {call.Funder} {call.Summary} {call.Eligibility} {call.Amount} {call.SourceName} {call.MatchLabel}";
        return _searchWords.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // Typing on the Filters tab means you're looking for calls — show them.
        if (_currentView == PanelView.Filters && SearchBox.Text.Length > 0) SetView(PanelView.Opportunities);

        // Short pause so the lists re-filter once you stop typing, not on every key.
        _searchDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(180), DispatcherPriority.Input, (_, _) =>
        {
            _searchDebounce!.Stop();
            _searchWords = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            RefreshLists();
        }, Dispatcher);
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) ClearSearch();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e) => ClearSearch();

    private void ClearSearch()
    {
        SearchBox.Clear();
        _searchWords = Array.Empty<string>();
        RefreshLists();
    }

    /// <summary>Ctrl+F jumps to the search bar from anywhere in the window.</summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    // --- Filters tab sections ------------------------------------------------------

    private void ToggleSourcesSection_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        _data.SourcesSectionOpen = !_data.SourcesSectionOpen;
        AnimateSection(SourcesList, _data.SourcesSectionOpen);
        SetChevron(SourcesChevron, _data.SourcesSectionOpen, animate: true);
        SaveNow();
    }

    private void ToggleKeywordsSection_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        _data.KeywordsSectionOpen = !_data.KeywordsSectionOpen;
        AnimateSection(KeywordsSection, _data.KeywordsSectionOpen);
        SetChevron(KeywordsChevron, _data.KeywordsSectionOpen, animate: true);
        KeywordsActions.Visibility = _data.KeywordsSectionOpen ? Visibility.Visible : Visibility.Collapsed;
        SaveNow();
    }

    /// <summary>Sets both sections open/closed as saved, without animating — used at startup.</summary>
    private void ApplyFilterSectionState()
    {
        if (_data is null) return;
        SourcesList.Visibility = _data.SourcesSectionOpen ? Visibility.Visible : Visibility.Collapsed;
        KeywordsSection.Visibility = _data.KeywordsSectionOpen ? Visibility.Visible : Visibility.Collapsed;
        KeywordsActions.Visibility = _data.KeywordsSectionOpen ? Visibility.Visible : Visibility.Collapsed;
        SetChevron(SourcesChevron, _data.SourcesSectionOpen, animate: false);
        SetChevron(KeywordsChevron, _data.KeywordsSectionOpen, animate: false);
        UpdateFilterSections();
    }

    /// <summary>Keeps the section headers' summaries ("8 of 9 on", "4 groups, 147 terms") current.</summary>
    private void UpdateFilterSections()
    {
        if (_data is null) return;
        SourcesSummary.Text = $"   ·   {_sources.Count(s => s.IsEnabled)} of {_sources.Count} on";
        var groups = _data.KeywordProfile.Groups;
        KeywordsSummary.Text = $"   ·   {groups.Count} group{Plural(groups.Count)}, {groups.Sum(g => g.Terms.Count)} terms";
    }

    /// <summary>
    /// Open, not dismissed, and from a source that's ticked on the Sources tab. Calls from
    /// unticked sources stay saved (flags and all), just hidden until the source is back on.
    /// The Shortlist ignores this — what you've shortlisted always stays visible.
    /// </summary>
    private bool IsOpenAndVisible(FundingCall call) =>
        !call.IsDismissed && call.Bucket != DeadlineBucket.Closed && _data?.DisabledSources.Contains(call.SourceName) != true;

    private bool PassesFilter(FundingCall call) => call.IsMatch || _data?.ShowNonMatching == true;

    private void OpportunitiesView_Filter(object sender, FilterEventArgs e)
        => e.Accepted = e.Item is FundingCall call && IsOpenAndVisible(call) && !call.IsSeen && PassesFilter(call) && PassesSearch(call)
                        && (call.IsNew || _data?.ShowOnlyNew != true);

    private void ShortlistView_Filter(object sender, FilterEventArgs e)
        => e.Accepted = e.Item is FundingCall { IsShortlisted: true } call && PassesSearch(call);

    private void SeenView_Filter(object sender, FilterEventArgs e)
        => e.Accepted = e.Item is FundingCall call && IsOpenAndVisible(call) && call.IsSeen && PassesFilter(call) && PassesSearch(call);

    private ICollectionView? ViewOf(string key) => ((CollectionViewSource)Resources[key]).View;

    /// <summary>Re-evaluates dates and badges on every call, then refreshes the lists.</summary>
    private void RefreshViews()
    {
        if (_data is null) return;
        foreach (var call in _data.Calls)
            call.RefreshDerived();
        RefreshLists();
    }

    /// <summary>Re-filters, re-sections and re-ranks all three call lists, then updates their hints and counts.</summary>
    private void RefreshLists()
    {
        if (_data is null) return;

        var opportunities = ViewOf("OpportunitiesView");
        var shortlist = ViewOf("ShortlistView");
        var seen = ViewOf("SeenView");
        opportunities?.Refresh();
        shortlist?.Refresh();
        seen?.Refresh();

        OpportunitiesHint.Visibility = opportunities?.IsEmpty != false ? Visibility.Visible : Visibility.Collapsed;
        ShortlistHint.Visibility = shortlist?.IsEmpty != false ? Visibility.Visible : Visibility.Collapsed;
        SeenHint.Visibility = seen?.IsEmpty != false ? Visibility.Visible : Visibility.Collapsed;

        const string NoSearchHits = "Nothing here matches your search.";
        OpportunitiesHint.Text = IsSearching ? NoSearchHits
            : _data.ShowOnlyNew ? "Nothing new right now — untick \"Only new\" to see every call."
            : "No matching open calls yet — hit Scan to fetch some.";
        ShortlistHint.Text = IsSearching ? NoSearchHits : "Nothing shortlisted — tap a leaf to keep a call here.";
        SeenHint.Text = IsSearching ? NoSearchHits
            : "Nothing here yet — calls move here a week after you first see them in Opportunities.";

        MatchSummaryText.Text = IsSearching ? SearchSummary(opportunities) : OpportunitiesSummary();
        SeenSummaryText.Text =
            $"Matching calls move here {FundingCall.SeenAfterDays} days after you first see them in Opportunities. " +
            "↺ brings one back for another week.";

        UpdateExportSummary();
        if (_sources.Count > 0) UpdateFilterSections();
        MarkOpportunitiesViewed();
    }

    private string OpportunitiesSummary()
    {
        var current = _data!.Calls.Where(c => IsOpenAndVisible(c) && c.IsMatch && !c.IsSeen).ToList();
        var unseen = current.Count(c => !c.FirstViewed.HasValue);
        var strong = current.Count(c => c.IsStrongMatch);
        var seenCount = _data.Calls.Count(c => IsOpenAndVisible(c) && c.IsMatch && c.IsSeen);

        if (_data.ShowOnlyNew)
        {
            var fresh = current.Count(c => c.IsNew);
            return $"Showing {fresh} new call{Plural(fresh)} of {current.Count} matching";
        }

        return current.Count == 0 && seenCount == 0
            ? string.Empty
            : $"{current.Count} matching call{Plural(current.Count)}" + (strong > 0 ? $" ({strong} strong)" : "")
              + (unseen > 0 ? $" · {unseen} new to you" : "")
              + (seenCount > 0 ? $" · {seenCount} moved to Seen" : "");
    }

    /// <summary>
    /// "4 results for "graphene" · 3 more among non-matching calls · 2 in Seen" — so a
    /// search never silently misses calls the other filters are hiding.
    /// </summary>
    private string SearchSummary(ICollectionView? opportunities)
    {
        var shown = opportunities?.Cast<object>().Count() ?? 0;
        var hidden = _data!.ShowNonMatching ? 0
            : _data.Calls.Count(c => IsOpenAndVisible(c) && !c.IsSeen && !c.IsMatch && PassesSearch(c));
        var inSeen = _data.Calls.Count(c => IsOpenAndVisible(c) && c.IsSeen && PassesFilter(c) && PassesSearch(c));
        return $"{shown} result{Plural(shown)} for “{string.Join(' ', _searchWords)}”"
               + (hidden > 0 ? $" · {hidden} more among non-matching calls" : "")
               + (inSeen > 0 ? $" · {inSeen} in Seen" : "");
    }

    /// <summary>
    /// Starts the Seen clock for everything currently listed in Opportunities — but only
    /// while you can actually see it: window open and active, Opportunities tab showing.
    /// </summary>
    private void MarkOpportunitiesViewed()
    {
        if (_data is null || !IsVisible || !IsActive || OpportunitiesPanel.Visibility != Visibility.Visible) return;
        if (ViewOf("OpportunitiesView") is not { } view) return;

        var now = DateTime.Now;
        var changed = false;
        foreach (FundingCall call in view)
        {
            if (call.FirstViewed.HasValue) continue;
            call.FirstViewed = now;
            changed = true;
        }
        if (changed) SaveNow();
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        MaybePlayMorningReplay();
        MarkOpportunitiesViewed();
        if (_badgeCount > 0) SetNewCallsBadge(0); // you've opened it — the badge has done its job
    }

    /// <summary>First time you open the window each day: a quick tree play-through summarising the scans.</summary>
    private void MaybePlayMorningReplay()
    {
        if (_data is null || _isScanning || WindowState == WindowState.Minimized) return;
        if (_data.LastReplayDate?.Date == DateTime.Today || ScanOverlay.Visibility == Visibility.Visible) return;
        _data.LastReplayDate = DateTime.Today;
        SaveNow();
        PlayMorningReplay(_sources.Count(s => s.IsEnabled));
    }

    /// <summary>Called by the app's safety net when something unexpected goes wrong.</summary>
    public void ReportError(Exception ex)
        => SetStatus($"Something went wrong ({ex.GetType().Name}: {ex.Message}). Your data is safe — if it keeps happening, restart the app.");

    // --- Status ------------------------------------------------------------------

    /// <summary>Shows a message in the status strip; the strip stays hidden while there's nothing to say.</summary>
    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusBorder.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The banner's "· Last scan today 09:00" suffix.</summary>
    private void UpdateLastScanLabel()
    {
        if (_data?.LastScan is not { } last)
        {
            LastScanRun.Text = "  ·  Not scanned yet";
            return;
        }

        var day = last.Date == DateTime.Today ? "today"
            : last.Date == DateTime.Today.AddDays(-1) ? "yesterday"
            : last.ToString("d MMM");
        LastScanRun.Text = $"  ·  Last scan {day} {last:HH:mm}";
    }

    // --- Scanning -----------------------------------------------------------------

    /// <summary>
    /// True when the most recent scheduled time today (9:00, 14:00, ...) has passed without a
    /// scan since — so logging in at 10:30 catches up on the 9:00 scan straight away.
    /// </summary>
    private bool IsDailyScanDue()
    {
        if (_data is null) return false;
        var latestSlot = _data.ScanHours
            .Select(h => DateTime.Today.AddHours(h))
            .Where(t => t <= DateTime.Now)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        return latestSlot != DateTime.MinValue && (_data.LastScan is null || _data.LastScan < latestSlot);
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await RunScanAsync(automatic: false);

    private async Task RunScanAsync(bool automatic)
    {
        if (_data is null || _isScanning) return;

        var enabled = _sources.Where(s => s.IsEnabled).Select(s => s.Source).ToList();
        if (enabled.Count == 0)
        {
            SetStatus("No sources are switched on — pick some under Sources.");
            return;
        }

        _isScanning = true;
        ScanButton.IsEnabled = false;
        SetStatus($"Scanning {enabled.Count} source{(enabled.Count == 1 ? "" : "s")}... (takes around 10 seconds)");

        // The growing-tree overlay, only if you can see the window (no point animating in the tray).
        var showOverlay = IsVisible && WindowState != WindowState.Minimized;
        var overlayRun = showOverlay ? BeginScanOverlay(enabled.Count) : 0;
        var overlayHeadline = "Scan finished";

        try
        {
            var progress = new Progress<ScanProgress>(p => { if (showOverlay) OnScanProgress(p); });
            var result = await FundingScanner.ScanAsync(enabled, _data.Calls, progress);
            _data.LastScan = DateTime.Now;
            ApplyFilter();
            RefreshViews();
            SaveNow();

            var newMatches = result.Added.Where(c => c.IsMatch && IsOpenAndVisible(c)).ToList();
            var newStrong = newMatches.Count(c => c.IsStrongMatch);

            var summary = newMatches.Count > 0
                ? $"{newMatches.Count} new matching call{Plural(newMatches.Count)}" + (newStrong > 0 ? $" ({newStrong} strong)" : "")
                : "No new matching calls";
            summary += $" — {result.Added.Count} new in total, {result.Refreshed} already known";
            if (result.Duplicates > 0) summary += $", {result.Duplicates} duplicate{Plural(result.Duplicates)} merged";
            summary += ".";
            if (result.Errors.Count > 0)
                summary += "\nProblems with: " + string.Join("; ", result.Errors);
            SetStatus(summary);
            UpdateLastScanLabel();
            overlayHeadline = newMatches.Count > 0
                ? $"{newMatches.Count} new matching call{Plural(newMatches.Count)}"
                : "No new matching calls";

            // Not looking at the window? Badge the taskbar button (adds up across scans until you open it).
            if (newMatches.Count > 0 && !IsActive) SetNewCallsBadge(_badgeCount + newMatches.Count);

            // The morning scan always reports in; a manual one only if you've looked away.
            if (newMatches.Count > 0 && (automatic || !IsActive))
            {
                var headline = newMatches.OrderByDescending(c => c.IsStrongMatch).ThenByDescending(c => c.MatchScore).First();
                (Application.Current as App)?.ShowNotification(
                    $"{newMatches.Count} new funding call{Plural(newMatches.Count)}" + (newStrong > 0 ? $", {newStrong} strong" : ""),
                    newMatches.Count == 1 ? headline.Title : $"Including: {headline.Title}");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Scan failed: {ex.Message}");
            overlayHeadline = "Scan failed — see the status line";
        }
        finally
        {
            _isScanning = false;
            ScanButton.IsEnabled = true;
        }

        if (showOverlay)
        {
            _data.LastReplayDate = DateTime.Today; // you watched the real tree — no replay needed today
            await EndScanOverlayAsync(overlayHeadline, overlayRun);
        }
    }

    private static string Plural(int n) => n == 1 ? "" : "s";

    // --- Call actions -------------------------------------------------------------

    private void Shortlist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FundingCall call }) return;
        call.IsShortlisted = !call.IsShortlisted;

        if (call.IsShortlisted && sender is System.Windows.Controls.Button leaf) PopLeaf(leaf);

        // On Opportunities/Seen the row stays put, so only the Shortlist list needs redoing —
        // refreshing everything would rebuild this row and cut the leaf's pop short.
        if (_currentView == PanelView.Shortlist) RefreshLists();
        else
        {
            ViewOf("ShortlistView")?.Refresh();
            ShortlistHint.Visibility = ViewOf("ShortlistView")?.IsEmpty != false ? Visibility.Visible : Visibility.Collapsed;
            UpdateExportSummary();
        }
        SaveNow();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FundingCall call }) return;
        call.IsDismissed = true;
        call.IsShortlisted = false;
        RefreshViews();
        SaveNow();
    }

    private void OpenCall_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FundingCall call }) return;

        // URLs come from scraped pages, so only ever hand real web links to the shell.
        if (!Uri.TryCreate(call.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            SetStatus($"That call doesn't have a usable web link ({call.Url}).");
            return;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    /// <summary>Seen tab's ↺: back into Opportunities, with a fresh week before it moves again.</summary>
    private void RestoreToOpportunities_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FundingCall call }) return;
        call.FirstViewed = DateTime.Now;
        RefreshViews();
        SaveNow();
    }

    // --- Export ------------------------------------------------------------------

    /// <summary>
    /// All shortlisted calls in on-screen order (sections, then relevance) — deliberately
    /// ignoring any search, so an export never quietly drops ticked calls.
    /// </summary>
    private List<FundingCall> ShortlistedCalls() => _data?.Calls
        .Where(c => c.IsShortlisted)
        .OrderBy(c => c.BucketOrder).ThenByDescending(c => c.IsStrongMatch)
        .ThenByDescending(c => c.MatchScore).ThenBy(c => c.DeadlineSortKey)
        .ToList() ?? new List<FundingCall>();

    private void UpdateExportSummary()
    {
        var shortlisted = ShortlistedCalls();
        var ticked = shortlisted.Count(c => c.IncludeInExport);
        ExportSummaryText.Text = shortlisted.Count == 0 ? string.Empty : $"{ticked} of {shortlisted.Count} ticked for export";
        ExportPdfButton.IsEnabled = ticked > 0;
        ToggleAllExportButton.Content = shortlisted.Count > 0 && ticked == shortlisted.Count ? "Untick all" : "Tick all";
        ToggleAllExportButton.Visibility = shortlisted.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearShortlistButton.Visibility = shortlisted.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Empties the shortlist (after confirming). The calls themselves stay in Opportunities / Seen.</summary>
    private void ClearShortlist_Click(object sender, RoutedEventArgs e)
    {
        var shortlisted = ShortlistedCalls();
        if (shortlisted.Count == 0) return;

        if (!ConfirmDialog.Ask(this, "Clear the shortlist?",
                $"Remove all {shortlisted.Count} call{Plural(shortlisted.Count)} from your shortlist?\n\n" +
                "They'll stay in Opportunities or Seen, so you can shortlist them again later.",
                "Clear all", destructive: true))
            return;

        foreach (var call in shortlisted)
            call.IsShortlisted = false;
        RefreshLists();
        SaveNow();
        SetStatus($"Shortlist cleared — {shortlisted.Count} call{Plural(shortlisted.Count)} removed.");
    }

    private void ExportTick_Click(object sender, RoutedEventArgs e)
    {
        UpdateExportSummary();
        SaveNow();
    }

    private void ToggleAllExport_Click(object sender, RoutedEventArgs e)
    {
        var shortlisted = ShortlistedCalls();
        var tickAll = shortlisted.Any(c => !c.IncludeInExport);
        foreach (var call in shortlisted)
            call.IncludeInExport = tickAll;
        UpdateExportSummary();
        SaveNow();
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        // Same order as on screen: sections, then relevance.
        var calls = ShortlistedCalls().Where(c => c.IncludeInExport).ToList();
        if (calls.Count == 0) return;

        var now = DateTime.Now;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export shortlist to PDF",
            Filter = "PDF document (*.pdf)|*.pdf",
            FileName = $"Funding shortlist - {now:yyyy-MM-dd}.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            ShortlistPdfExporter.Export(dlg.FileName, calls, now);
            SetStatus($"Exported {calls.Count} call{Plural(calls.Count)} to {System.IO.Path.GetFileName(dlg.FileName)}.");
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't export the PDF: {ex.Message}");
        }
    }

    private void ShowOnlyNew_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        _data.ShowOnlyNew = ShowOnlyNewCheck.IsChecked == true;
        RefreshViews();
        SaveNow();
    }

    private void ShowNonMatching_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        _data.ShowNonMatching = ShowNonMatchingCheck.IsChecked == true;
        RefreshViews();
        SaveNow();
    }

    // --- Filter -------------------------------------------------------------------

    private void ApplyFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        // Text boxes push their edits on lost focus; clicking the button already moved
        // focus, but force it for keyboard users (Enter on a focused button).
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox box)
            box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();

        ApplyFilter();
        RefreshViews();
        SaveNow();
        KeywordGroupsList.Items.Refresh();
        SetStatus("Filter applied and saved. " + MatchSummaryText.Text + ".");
    }

    private void ResetFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        if (!ConfirmDialog.Ask(this, "Reset keywords?",
                "Replace all your keyword groups with the starting set (tuned for materials science)?\n\n" +
                "Any groups or terms you've added will be lost.",
                "Reset", destructive: true))
            return;

        _data.KeywordProfile = KeywordProfile.CreateDefault();
        KeywordGroupsList.ItemsSource = _data.KeywordProfile.Groups;
        ApplyFilter_Click(sender, e);
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        var group = new KeywordGroup { Name = "New group", CountsOnItsOwn = true };
        _data.KeywordProfile.Groups.Add(group);
        SaveNow();

        // Scroll to the new group and put the cursor in its name, ready to type over.
        FiltersScroller.ScrollToEnd(); // Keywords is the last section
        Dispatcher.BeginInvoke(() =>
        {
            if (KeywordGroupsList.ItemContainerGenerator.ContainerFromItem(group) is DependencyObject container
                && FindDescendant<System.Windows.Controls.TextBox>(container) is { } nameBox)
            {
                nameBox.Focus();
                nameBox.SelectAll();
            }
        }, DispatcherPriority.Loaded);
        SetStatus("Group added — name it, add terms, then Apply & save.");
    }

    private void RemoveGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null || sender is not FrameworkElement { DataContext: KeywordGroup group }) return;

        var label = string.IsNullOrWhiteSpace(group.Name) ? "this group" : $"\"{group.Name}\"";
        var detail = group.Terms.Count == 0 ? "" : $" and its {group.Terms.Count} term{Plural(group.Terms.Count)}";
        if (!ConfirmDialog.Ask(this, "Remove keyword group?", $"Remove {label}{detail}?", "Remove", destructive: true))
            return;

        _data.KeywordProfile.Groups.Remove(group);
        ApplyFilter_Click(sender, e); // re-match without it, and save
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    // --- Sources ------------------------------------------------------------------

    private void SourceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        _data.DisabledSources = _sources.Where(s => !s.IsEnabled).Select(s => s.Name).ToList();
        RefreshViews(); // hide or bring back that source's calls straight away
        SaveNow();
    }

    /// <summary>"Show everything from this source": re-filter straight away, no new scan needed.</summary>
    private void SourceShowAll_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null || sender is not FrameworkElement { DataContext: SourceEntry entry }) return;
        _data.SourceShowAll[entry.Name] = entry.ShowAll;
        ApplyFilter();
        RefreshViews();
        SaveNow();
    }

    /// <summary>Re-scores every call against the keyword profile and the per-source "show everything" settings.</summary>
    private void ApplyFilter()
    {
        if (_data is null) return;
        var showAll = _sources.Where(s => s.ShowAll).Select(s => s.Name).ToHashSet();
        KeywordMatcher.Apply(_data.KeywordProfile, _data.Calls, showAll);
    }
}
