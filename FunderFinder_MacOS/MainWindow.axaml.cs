using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FunderFinder.Mac.Platform;
using FunderFinder.Models;
using FunderFinder.Services;

namespace FunderFinder.Mac;

/// <summary>A section heading in a call list ("Due within two weeks — ranked by relevance").</summary>
public record SectionHeader(string Text, bool IsUrgent);

/// <summary>A call as shown in one list, plus that list's extras (export tick on Shortlist, ↺ on Seen).</summary>
public record CallRow(FundingCall Call, bool ShowExportTick, bool ShowRestore);

/// <summary>
/// The Mac (Avalonia) window. Behaviour mirrors the Windows app; the data, sources, matching
/// and PDF export are the very same code, shared from ../FunderFinder_Windows.
/// </summary>
public partial class MainWindow : Window
{
    private enum Tab { Opportunities, Shortlist, Seen, Filters }

    private readonly AppData _data;
    private readonly List<SourceEntry> _sources;
    private Tab _tab = Tab.Opportunities;
    private bool _isScanning;
    private bool _quitting;
    private int _badgeCount;
    private string[] _searchWords = Array.Empty<string>();
    private readonly DispatcherTimer _searchDebounce;
    private readonly DispatcherTimer _timer;
    private List<FundingCall> _listed = new();

    public MainWindow()
    {
        InitializeComponent();

        _data = DataStore.Load();
        FundingCall.SeenAfterDays = _data.SeenAfterDays;
        _sources = FundingScanner.AllSources
            .Select(s => new SourceEntry(s, !_data.DisabledSources.Contains(s.Name),
                _data.SourceShowAll.TryGetValue(s.Name, out var showAll) ? showAll : s.ShowAllByDefault))
            .ToList();
        SourcesList.ItemsSource = _sources;
        KeywordGroupsList.ItemsSource = _data.KeywordProfile.Groups;
        OnlyNewCheck.IsChecked = _data.ShowOnlyNew;
        NonMatchingCheck.IsChecked = _data.ShowNonMatching;
        if (_data.WindowWidth >= MinWidth) Width = _data.WindowWidth;
        if (_data.WindowHeight >= MinHeight) Height = _data.WindowHeight;

        // macOS draws its traffic-light buttons at the banner's left; elsewhere the caption
        // buttons sit at the right — leave room for whichever this machine has.
        TrafficLightSpace.Width = OperatingSystem.IsMacOS() ? 78 : 12;
        CaptionButtonSpace.Width = OperatingSystem.IsMacOS() ? 0 : 140;

        PrepareFades();
        ApplySectionState();
        ApplyFilter();
        UpdateLastScanLabel();
        SetTab(Tab.Opportunities, animate: false);

        _searchDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(180), DispatcherPriority.Input, (_, _) =>
        {
            _searchDebounce!.Stop();
            _searchWords = (SearchBox.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            RefreshLists();
        });
        SearchBox.TextChanged += SearchBox_TextChanged;

        // Every few minutes: re-sort (sections depend on today's date) and run the daily scan once due.
        _timer = new DispatcherTimer(TimeSpan.FromMinutes(5), DispatcherPriority.Background, async (_, _) =>
        {
            RefreshAll();
            UpdateLastScanLabel();
            if (IsDailyScanDue()) await RunScanAsync(automatic: true);
        });
        _timer.Start();

        Opened += (_, _) => { if (IsDailyScanDue()) _ = RunScanAsync(automatic: true); };
        Activated += (_, _) => OnActivated();
        Closing += OnClosing;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    // --- Window lifecycle ---------------------------------------------------------

    /// <summary>Closing just tucks the window away in the Dock; Quit is in the menu-bar icon (or ⌘Q).</summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        SaveNow();
        if (_quitting || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;
        e.Cancel = true;
        WindowState = WindowState.Minimized;
    }

    public void SaveNow()
    {
        if (WindowState == WindowState.Normal && !double.IsNaN(Width) && !double.IsNaN(Height))
        {
            _data.WindowWidth = Width;
            _data.WindowHeight = Height;
        }
        DataStore.Save(_data);
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    public void ScanFromMenu() => _ = RunScanAsync(automatic: false);

    /// <summary>Really quit (menu-bar "Quit"): save, let the window close, end the app.</summary>
    public void Quit()
    {
        _quitting = true;
        SaveNow();
        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private void Banner_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void OnActivated()
    {
        MaybePlayMorningReplay();
        MarkOpportunitiesViewed();
        if (_badgeCount > 0) SetNewCallsBadge(0); // you've opened it — the badge has done its job
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (command && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsFocused)
        {
            SearchBox.Text = "";
            e.Handled = true;
        }
    }

    // --- Tabs -----------------------------------------------------------------------

    private void Tab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && Enum.TryParse<Tab>(name, out var tab)) SetTab(tab, animate: true);
    }

    private void SetTab(Tab tab, bool animate)
    {
        var changed = tab != _tab;
        _tab = tab;
        CallsPanel.IsVisible = tab != Tab.Filters;
        FiltersPanel.IsVisible = tab == Tab.Filters;
        OpportunitiesBar.IsVisible = tab == Tab.Opportunities;
        ShortlistBar.IsVisible = tab == Tab.Shortlist;
        SeenBar.IsVisible = tab == Tab.Seen;

        foreach (var (button, t) in new[] { (OpportunitiesTab, Tab.Opportunities), (ShortlistTab, Tab.Shortlist), (SeenTab, Tab.Seen), (FiltersTab, Tab.Filters) })
            button.Classes.Set("active", t == tab);

        if (tab != Tab.Filters) RefreshLists(resetScroll: true);
        if (animate && changed) FadeIn(tab == Tab.Filters ? FiltersPanel : CallsPanel);
    }

    // --- Listing ----------------------------------------------------------------------

    private bool IsOpenAndVisible(FundingCall c) =>
        !c.IsDismissed && c.Bucket != DeadlineBucket.Closed && !_data.DisabledSources.Contains(c.SourceName);

    private bool PassesFilter(FundingCall c) => c.IsMatch || _data.ShowNonMatching;

    private bool PassesSearch(FundingCall c)
    {
        if (_searchWords.Length == 0) return true;
        var text = $"{c.Title} {c.Funder} {c.Summary} {c.Eligibility} {c.Amount} {c.SourceName} {c.MatchLabel}";
        return _searchWords.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<FundingCall> CallsFor(Tab tab) => tab switch
    {
        Tab.Opportunities => _data.Calls.Where(c => IsOpenAndVisible(c) && !c.IsSeen && PassesFilter(c) && PassesSearch(c)
                                                    && (c.IsNew || !_data.ShowOnlyNew)),
        Tab.Shortlist => _data.Calls.Where(c => c.IsShortlisted && PassesSearch(c)),
        Tab.Seen => _data.Calls.Where(c => IsOpenAndVisible(c) && c.IsSeen && PassesFilter(c) && PassesSearch(c)),
        _ => Enumerable.Empty<FundingCall>(),
    };

    /// <summary>Strong matches first, then keyword score, then closing date — within each section.</summary>
    private static IEnumerable<FundingCall> Ranked(IEnumerable<FundingCall> calls) => calls
        .OrderBy(c => c.BucketOrder).ThenByDescending(c => c.IsStrongMatch)
        .ThenByDescending(c => c.MatchScore).ThenBy(c => c.DeadlineSortKey);

    private static string SectionTitle(DeadlineBucket bucket) => bucket switch
    {
        DeadlineBucket.ClosingSoon => "⚠ Due within two weeks — ranked by relevance",
        DeadlineBucket.Closed => "Closed",
        _ => "General — ranked by relevance",
    };

    /// <summary>Re-evaluates dates/badges on every call (they change as days pass), then re-lists.</summary>
    private void RefreshAll()
    {
        foreach (var call in _data.Calls) call.RefreshDerived();
        RefreshLists();
    }

    /// <summary>Rebuilds the current tab's list (sections + ranked calls), hints and counts.</summary>
    private void RefreshLists(bool resetScroll = false)
    {
        var offset = CallsScroller.Offset;
        _listed = Ranked(CallsFor(_tab)).ToList();

        var items = new List<object>();
        foreach (var section in _listed.GroupBy(c => c.Bucket))
        {
            items.Add(new SectionHeader(SectionTitle(section.Key), section.Key == DeadlineBucket.ClosingSoon));
            items.AddRange(section.Select(c => new CallRow(c, _tab == Tab.Shortlist, _tab == Tab.Seen)));
        }
        CallsList.ItemsSource = items;
        if (!resetScroll) Dispatcher.UIThread.Post(() => CallsScroller.Offset = offset, DispatcherPriority.Loaded);
        else CallsScroller.Offset = default;

        var searching = _searchWords.Length > 0;
        EmptyHint.IsVisible = _listed.Count == 0 && _tab != Tab.Filters;
        EmptyHint.Text = searching ? "Nothing here matches your search." : _tab switch
        {
            Tab.Shortlist => "Nothing shortlisted — tap a leaf to keep a call here.",
            Tab.Seen => "Nothing here yet — calls move here a week after you first see them in Opportunities.",
            _ when _data.ShowOnlyNew => "Nothing new right now — untick \"Only new\" to see every call.",
            _ => "No matching open calls yet — hit ↻ to scan.",
        };

        MatchSummaryText.Text = searching ? SearchSummary() : OpportunitiesSummary();
        SeenBar.Text = $"Matching calls move here {FundingCall.SeenAfterDays} days after you first see them in Opportunities. ↺ brings one back for another week.";
        UpdateExportSummary();
        UpdateFilterSummaries();
        MarkOpportunitiesViewed();
    }

    private string OpportunitiesSummary()
    {
        var current = _data.Calls.Where(c => IsOpenAndVisible(c) && c.IsMatch && !c.IsSeen).ToList();
        if (_data.ShowOnlyNew)
        {
            var fresh = current.Count(c => c.IsNew);
            return $"Showing {fresh} new call{Plural(fresh)} of {current.Count} matching";
        }
        var unseen = current.Count(c => !c.FirstViewed.HasValue);
        var strong = current.Count(c => c.IsStrongMatch);
        var seen = _data.Calls.Count(c => IsOpenAndVisible(c) && c.IsMatch && c.IsSeen);
        return current.Count == 0 && seen == 0 ? ""
            : $"{current.Count} matching call{Plural(current.Count)}" + (strong > 0 ? $" ({strong} strong)" : "")
              + (unseen > 0 ? $" · {unseen} new to you" : "") + (seen > 0 ? $" · {seen} moved to Seen" : "");
    }

    private string SearchSummary()
    {
        var shown = _tab == Tab.Opportunities ? _listed.Count : CallsFor(Tab.Opportunities).Count();
        var hidden = _data.ShowNonMatching ? 0 : _data.Calls.Count(c => IsOpenAndVisible(c) && !c.IsSeen && !c.IsMatch && PassesSearch(c));
        var inSeen = _data.Calls.Count(c => IsOpenAndVisible(c) && c.IsSeen && PassesFilter(c) && PassesSearch(c));
        return $"{shown} result{Plural(shown)} for “{string.Join(' ', _searchWords)}”"
               + (hidden > 0 ? $" · {hidden} more among non-matching calls" : "") + (inSeen > 0 ? $" · {inSeen} in Seen" : "");
    }

    /// <summary>Starts the Seen clock for what's listed in Opportunities — only while you can see it.</summary>
    private void MarkOpportunitiesViewed()
    {
        if (!IsActive || WindowState == WindowState.Minimized || _tab != Tab.Opportunities) return;
        var now = DateTime.Now;
        var changed = false;
        foreach (var call in _listed.Where(c => !c.FirstViewed.HasValue))
        {
            call.FirstViewed = now;
            changed = true;
        }
        if (changed) SaveNow();
    }

    // --- Search -------------------------------------------------------------------------

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_tab == Tab.Filters && !string.IsNullOrEmpty(SearchBox.Text)) SetTab(Tab.Opportunities, animate: true);
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    // --- Call actions -------------------------------------------------------------------

    private void Row_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || sender is not Control { DataContext: CallRow row }) return;
        if (!Uri.TryCreate(row.Call.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            SetStatus($"That call doesn't have a usable web link ({row.Call.Url}).");
            return;
        }
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private void Shortlist_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CallRow row } button) return;
        row.Call.IsShortlisted = !row.Call.IsShortlisted;
        if (row.Call.IsShortlisted && button.Content is Visual icon) PopLeaf(icon);
        if (_tab == Tab.Shortlist) RefreshLists(); else UpdateExportSummary();
        SaveNow();
    }

    private void Dismiss_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: CallRow row }) return;
        row.Call.IsDismissed = true;
        row.Call.IsShortlisted = false;
        RefreshLists();
        SaveNow();
    }

    private void Restore_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: CallRow row }) return;
        row.Call.FirstViewed = DateTime.Now;
        RefreshLists();
        SaveNow();
    }

    private void OnlyNew_Click(object? sender, RoutedEventArgs e)
    {
        _data.ShowOnlyNew = OnlyNewCheck.IsChecked == true;
        RefreshLists();
        SaveNow();
    }

    private void NonMatching_Click(object? sender, RoutedEventArgs e)
    {
        _data.ShowNonMatching = NonMatchingCheck.IsChecked == true;
        RefreshLists();
        SaveNow();
    }

    // --- Shortlist & export -----------------------------------------------------------

    private List<FundingCall> ShortlistedCalls() => Ranked(_data.Calls.Where(c => c.IsShortlisted)).ToList();

    private void UpdateExportSummary()
    {
        var shortlisted = ShortlistedCalls();
        var ticked = shortlisted.Count(c => c.IncludeInExport);
        ExportSummaryText.Text = shortlisted.Count == 0 ? "" : $"{ticked} of {shortlisted.Count} ticked for export";
        ExportPdfButton.IsEnabled = ticked > 0;
        ToggleAllExportButton.Content = shortlisted.Count > 0 && ticked == shortlisted.Count ? "Untick all" : "Tick all";
        ToggleAllExportButton.IsVisible = ClearShortlistButton.IsVisible = shortlisted.Count > 0;
    }

    private void ExportTick_Click(object? sender, RoutedEventArgs e)
    {
        UpdateExportSummary();
        SaveNow();
    }

    private void ToggleAllExport_Click(object? sender, RoutedEventArgs e)
    {
        var shortlisted = ShortlistedCalls();
        var tickAll = shortlisted.Any(c => !c.IncludeInExport);
        foreach (var call in shortlisted) call.IncludeInExport = tickAll;
        RefreshLists();
        SaveNow();
    }

    private async void ClearShortlist_Click(object? sender, RoutedEventArgs e)
    {
        var shortlisted = ShortlistedCalls();
        if (shortlisted.Count == 0) return;
        if (!await ConfirmDialog.AskAsync(this, "Clear the shortlist?",
                $"Remove all {shortlisted.Count} call{Plural(shortlisted.Count)} from your shortlist?\n\n" +
                "They'll stay in Opportunities or Seen, so you can shortlist them again later.", "Clear all", destructive: true))
            return;
        foreach (var call in shortlisted) call.IsShortlisted = false;
        RefreshLists();
        SaveNow();
        SetStatus($"Shortlist cleared — {shortlisted.Count} call{Plural(shortlisted.Count)} removed.");
    }

    private async void ExportPdf_Click(object? sender, RoutedEventArgs e)
    {
        var calls = ShortlistedCalls().Where(c => c.IncludeInExport).ToList();
        if (calls.Count == 0) return;

        var now = DateTime.Now;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export shortlist to PDF",
            SuggestedFileName = $"Funding shortlist - {now:yyyy-MM-dd}.pdf",
            DefaultExtension = "pdf",
            FileTypeChoices = new[] { new FilePickerFileType("PDF document") { Patterns = new[] { "*.pdf" } } },
        });
        var path = file?.TryGetLocalPath();
        if (path is null) return;

        try
        {
            ShortlistPdfExporter.Export(path, calls, now);
            SetStatus($"Exported {calls.Count} call{Plural(calls.Count)} to {Path.GetFileName(path)}.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't export the PDF: {ex.Message}");
        }
    }

    // --- Filters: sources --------------------------------------------------------------

    private void SourceToggle_Click(object? sender, RoutedEventArgs e)
    {
        _data.DisabledSources = _sources.Where(s => !s.IsEnabled).Select(s => s.Name).ToList();
        RefreshLists();
        SaveNow();
    }

    private void SourceShowAll_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SourceEntry entry }) return;
        _data.SourceShowAll[entry.Name] = entry.ShowAll;
        ApplyFilter();
        RefreshAll();
        SaveNow();
    }

    private void ApplyFilter()
    {
        var showAll = _sources.Where(s => s.ShowAll).Select(s => s.Name).ToHashSet();
        KeywordMatcher.Apply(_data.KeywordProfile, _data.Calls, showAll);
    }

    // --- Filters: sections -------------------------------------------------------------

    private void ToggleSources_Click(object? sender, RoutedEventArgs e)
    {
        _data.SourcesSectionOpen = !_data.SourcesSectionOpen;
        ApplySectionState();
        SaveNow();
    }

    private void ToggleKeywords_Click(object? sender, RoutedEventArgs e)
    {
        _data.KeywordsSectionOpen = !_data.KeywordsSectionOpen;
        ApplySectionState();
        SaveNow();
    }

    private void ApplySectionState()
    {
        SourcesList.IsVisible = _data.SourcesSectionOpen;
        SourcesChevron.Text = _data.SourcesSectionOpen ? "▾" : "▸";
        KeywordsSection.IsVisible = KeywordsActions.IsVisible = _data.KeywordsSectionOpen;
        KeywordsChevron.Text = _data.KeywordsSectionOpen ? "▾" : "▸";
        UpdateFilterSummaries();
    }

    private void UpdateFilterSummaries()
    {
        SourcesSummary.Text = $"·  {_sources.Count(s => s.IsEnabled)} of {_sources.Count} on";
        var groups = _data.KeywordProfile.Groups;
        KeywordsSummary.Text = $"·  {groups.Count} group{Plural(groups.Count)}, {groups.Sum(g => g.Terms.Count)} terms";
    }

    // --- Filters: keywords -------------------------------------------------------------

    private void ApplyKeywords_Click(object? sender, RoutedEventArgs e)
    {
        ApplyFilter();
        RefreshAll();
        SaveNow();
        SetStatus("Filter applied and saved. " + OpportunitiesSummary() + ".");
    }

    private void AddGroup_Click(object? sender, RoutedEventArgs e)
    {
        var group = new KeywordGroup { Name = "New group", CountsOnItsOwn = true };
        _data.KeywordProfile.Groups.Add(group);
        SaveNow();
        UpdateFilterSummaries();
        Dispatcher.UIThread.Post(() =>
        {
            FiltersScroller.ScrollToEnd();
            if (KeywordGroupsList.ContainerFromItem(group) is Control container
                && container.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } nameBox)
            {
                nameBox.Focus();
                nameBox.SelectAll();
            }
        }, DispatcherPriority.Loaded);
        SetStatus("Group added — name it, add terms, then Apply & save.");
    }

    private async void RemoveGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: KeywordGroup group }) return;
        var label = string.IsNullOrWhiteSpace(group.Name) ? "this group" : $"\"{group.Name}\"";
        var detail = group.Terms.Count == 0 ? "" : $" and its {group.Terms.Count} term{Plural(group.Terms.Count)}";
        if (!await ConfirmDialog.AskAsync(this, "Remove keyword group?", $"Remove {label}{detail}?", "Remove", destructive: true)) return;
        _data.KeywordProfile.Groups.Remove(group);
        ApplyKeywords_Click(sender, e);
    }

    private async void ResetKeywords_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDialog.AskAsync(this, "Reset keywords?",
                "Replace all your keyword groups with the starting set (tuned for materials science)?\n\nAny groups or terms you've added will be lost.",
                "Reset", destructive: true))
            return;
        _data.KeywordProfile = KeywordProfile.CreateDefault();
        KeywordGroupsList.ItemsSource = _data.KeywordProfile.Groups;
        ApplyKeywords_Click(sender, e);
    }

    // --- Scanning ------------------------------------------------------------------------

    /// <summary>True when the most recent scheduled time today (9:00 by default) has passed without a scan since.</summary>
    private bool IsDailyScanDue()
    {
        var latestSlot = _data.ScanHours.Select(h => DateTime.Today.AddHours(h)).Where(t => t <= DateTime.Now)
            .DefaultIfEmpty(DateTime.MinValue).Max();
        return latestSlot != DateTime.MinValue && (_data.LastScan is null || _data.LastScan < latestSlot);
    }

    private async void Scan_Click(object? sender, RoutedEventArgs e) => await RunScanAsync(automatic: false);

    private async Task RunScanAsync(bool automatic)
    {
        if (_isScanning) return;
        var enabled = _sources.Where(s => s.IsEnabled).Select(s => s.Source).ToList();
        if (enabled.Count == 0)
        {
            SetStatus("No sources are switched on — pick some under Filters → Sources.");
            return;
        }

        _isScanning = true;
        ScanButton.IsEnabled = false;
        SetStatus($"Scanning {enabled.Count} source{Plural(enabled.Count)}... (takes around 10 seconds)");
        var showOverlay = IsVisible && WindowState != WindowState.Minimized;
        var run = showOverlay ? BeginScanOverlay(enabled.Count) : 0;
        var headline = "Scan finished";

        try
        {
            var progress = new Progress<ScanProgress>(p => { if (showOverlay) OnScanProgress(p); });
            var result = await FundingScanner.ScanAsync(enabled, _data.Calls, progress);
            _data.LastScan = DateTime.Now;
            ApplyFilter();
            RefreshAll();
            SaveNow();

            var newMatches = result.Added.Where(c => c.IsMatch && IsOpenAndVisible(c)).ToList();
            var newStrong = newMatches.Count(c => c.IsStrongMatch);
            var summary = (newMatches.Count > 0
                    ? $"{newMatches.Count} new matching call{Plural(newMatches.Count)}" + (newStrong > 0 ? $" ({newStrong} strong)" : "")
                    : "No new matching calls")
                + $" — {result.Added.Count} new in total, {result.Refreshed} already known"
                + (result.Duplicates > 0 ? $", {result.Duplicates} duplicate{Plural(result.Duplicates)} merged" : "") + ".";
            if (result.Errors.Count > 0) summary += "\nProblems with: " + string.Join("; ", result.Errors);
            SetStatus(summary);
            UpdateLastScanLabel();
            headline = newMatches.Count > 0 ? $"{newMatches.Count} new matching call{Plural(newMatches.Count)}" : "No new matching calls";

            // Not looking at the window? Badge the Dock icon (adds up across scans until you open it).
            if (newMatches.Count > 0 && !IsActive) SetNewCallsBadge(_badgeCount + newMatches.Count);
        }
        catch (Exception ex)
        {
            SetStatus($"Scan failed: {ex.Message}");
            headline = "Scan failed — see the status line";
        }
        finally
        {
            _isScanning = false;
            ScanButton.IsEnabled = true;
        }

        if (showOverlay)
        {
            _data.LastReplayDate = DateTime.Today; // you watched the real tree — no replay needed today
            await EndScanOverlayAsync(headline, run);
        }
    }

    private void SetNewCallsBadge(int count)
    {
        _badgeCount = Math.Max(0, count);
        DockBadge.Set(_badgeCount);
    }

    /// <summary>First time you open the window each day: a quick, text-free tree play-through.</summary>
    private void MaybePlayMorningReplay()
    {
        if (_isScanning || WindowState == WindowState.Minimized || ScanOverlay.IsVisible) return;
        if (_data.LastReplayDate?.Date == DateTime.Today) return;
        _data.LastReplayDate = DateTime.Today;
        SaveNow();
        var run = BeginScanOverlay(_sources.Count(s => s.IsEnabled));
        ScanOverlayTitle.IsVisible = ScanOverlayDetail.IsVisible = false;
        _ = EndScanOverlayAsync(string.Empty, run);
    }

    // --- Status & banner ---------------------------------------------------------------

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusBorder.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    private void UpdateLastScanLabel()
    {
        if (_data.LastScan is not { } last)
        {
            BannerText.Text = "Funder Finder  ·  Not scanned yet";
            return;
        }
        var day = last.Date == DateTime.Today ? "today" : last.Date == DateTime.Today.AddDays(-1) ? "yesterday" : last.ToString("d MMM");
        BannerText.Text = $"Funder Finder  ·  Last scan {day} {last:HH:mm}";
    }

    private static string Plural(int n) => n == 1 ? "" : "s";

    // --- Small animations -------------------------------------------------------------

    private static readonly TimeSpan TabFade = TimeSpan.FromMilliseconds(320);

    private void PrepareFades()
    {
        foreach (var panel in new Control[] { CallsPanel, FiltersPanel })
            panel.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TabFade, Easing = new CubicEaseOut() } };
    }

    /// <summary>The newly shown tab fades in.</summary>
    private static void FadeIn(Control panel)
    {
        var transitions = panel.Transitions;
        panel.Transitions = null;
        panel.Opacity = 0;
        panel.Transitions = transitions;
        Dispatcher.UIThread.Post(() => panel.Opacity = 1, DispatcherPriority.Render);
    }

    /// <summary>A small springy pop on the leaf you've just shortlisted.</summary>
    private static void PopLeaf(Visual icon)
    {
        icon.Transitions = new Transitions
        {
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new CubicEaseOut() },
        };
        icon.RenderTransform = TransformOperations.Parse("scale(1.45)");
        DispatcherTimer.RunOnce(() => icon.RenderTransform = TransformOperations.Parse("scale(1)"), TimeSpan.FromMilliseconds(150));
    }
}
