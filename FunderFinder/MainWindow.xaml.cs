using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using FunderFinder.Models;
using FunderFinder.Native;
using FunderFinder.Services;

namespace FunderFinder;

public partial class MainWindow : Window
{
    private enum PanelView { Opportunities, Shortlist, Seen, Filter, Sources }

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
        SourcesListBox.ItemsSource = _sources;
        KeywordGroupsList.ItemsSource = _data.KeywordProfile.Groups;
        ShowNonMatchingCheck.IsChecked = _data.ShowNonMatching;

        if (_data.WindowLeft.HasValue && _data.WindowTop.HasValue)
        {
            Left = _data.WindowLeft.Value;
            Top = _data.WindowTop.Value;
        }
        Width = _data.WindowWidth;
        Height = _data.WindowHeight;

        UpdateLastScanLabel();

        SetView(PanelView.Opportunities);
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
        // Background app: closing hides to the tray. Real exit is in the tray menu.
        e.Cancel = true;
        SaveNow();
        Hide();
    }

    public void SaveNow()
    {
        if (_data is null) return;
        _data.WindowLeft = Left;
        _data.WindowTop = Top;
        _data.WindowWidth = Width;
        _data.WindowHeight = Height;
        DataStore.Save(_data);
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        SaveNow();
        Hide();
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
    private void ShowFilter_Click(object sender, RoutedEventArgs e) => SetView(PanelView.Filter);
    private void ShowSources_Click(object sender, RoutedEventArgs e) => SetView(PanelView.Sources);

    private void SetView(PanelView view)
    {
        OpportunitiesPanel.Visibility = view == PanelView.Opportunities ? Visibility.Visible : Visibility.Collapsed;
        ShortlistPanel.Visibility = view == PanelView.Shortlist ? Visibility.Visible : Visibility.Collapsed;
        SeenPanel.Visibility = view == PanelView.Seen ? Visibility.Visible : Visibility.Collapsed;
        FilterPanel.Visibility = view == PanelView.Filter ? Visibility.Visible : Visibility.Collapsed;
        SourcesPanel.Visibility = view == PanelView.Sources ? Visibility.Visible : Visibility.Collapsed;

        // PillButtonStyle styles whichever tab is tagged "Active".
        OpportunitiesButton.Tag = view == PanelView.Opportunities ? "Active" : null;
        ShortlistButton.Tag = view == PanelView.Shortlist ? "Active" : null;
        SeenButton.Tag = view == PanelView.Seen ? "Active" : null;
        FilterButton.Tag = view == PanelView.Filter ? "Active" : null;
        SourcesButton.Tag = view == PanelView.Sources ? "Active" : null;

        MarkOpportunitiesViewed();
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
        => e.Accepted = e.Item is FundingCall call && IsOpenAndVisible(call) && !call.IsSeen && PassesFilter(call);

    private void ShortlistView_Filter(object sender, FilterEventArgs e)
        => e.Accepted = e.Item is FundingCall { IsShortlisted: true };

    private void SeenView_Filter(object sender, FilterEventArgs e)
        => e.Accepted = e.Item is FundingCall call && IsOpenAndVisible(call) && call.IsSeen && PassesFilter(call);

    private ICollectionView? ViewOf(string key) => ((CollectionViewSource)Resources[key]).View;

    /// <summary>Re-filters, re-sections and re-ranks all three call lists, then updates their hints and counts.</summary>
    private void RefreshViews()
    {
        if (_data is null) return;
        foreach (var call in _data.Calls)
            call.RefreshDerived();

        var opportunities = ViewOf("OpportunitiesView");
        var shortlist = ViewOf("ShortlistView");
        var seen = ViewOf("SeenView");
        opportunities?.Refresh();
        shortlist?.Refresh();
        seen?.Refresh();

        OpportunitiesHint.Visibility = opportunities?.IsEmpty != false ? Visibility.Visible : Visibility.Collapsed;
        ShortlistHint.Visibility = shortlist?.IsEmpty != false ? Visibility.Visible : Visibility.Collapsed;
        SeenHint.Visibility = seen?.IsEmpty != false ? Visibility.Visible : Visibility.Collapsed;

        var current = _data.Calls.Where(c => IsOpenAndVisible(c) && c.IsMatch && !c.IsSeen).ToList();
        var unseen = current.Count(c => !c.FirstViewed.HasValue);
        var strong = current.Count(c => c.IsStrongMatch);
        var seenCount = _data.Calls.Count(c => IsOpenAndVisible(c) && c.IsMatch && c.IsSeen);
        MatchSummaryText.Text = current.Count == 0 && seenCount == 0
            ? string.Empty
            : $"{current.Count} matching call{Plural(current.Count)}" + (strong > 0 ? $" ({strong} strong)" : "")
              + (unseen > 0 ? $" · {unseen} new to you" : "")
              + (seenCount > 0 ? $" · {seenCount} moved to Seen" : "");
        SeenSummaryText.Text =
            $"Matching calls move here {FundingCall.SeenAfterDays} days after you first see them in Opportunities. " +
            "↺ brings one back for another week.";

        UpdateExportSummary();
        MarkOpportunitiesViewed();
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

    private void Window_Activated(object? sender, EventArgs e) => MarkOpportunitiesViewed();

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

    private bool IsDailyScanDue()
    {
        if (_data is null) return false;
        var todaysRun = DateTime.Today.AddHours(_data.DailyScanHour);
        return DateTime.Now >= todaysRun && (_data.LastScan is null || _data.LastScan < todaysRun);
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

        try
        {
            var result = await FundingScanner.ScanAsync(enabled, _data.Calls);
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
        }
        finally
        {
            _isScanning = false;
            ScanButton.IsEnabled = true;
        }
    }

    private static string Plural(int n) => n == 1 ? "" : "s";

    // --- Call actions -------------------------------------------------------------

    private void Shortlist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FundingCall call }) return;
        call.IsShortlisted = !call.IsShortlisted;
        RefreshViews();
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

    private List<FundingCall> ShortlistedCalls() =>
        ViewOf("ShortlistView")?.Cast<FundingCall>().ToList() ?? new List<FundingCall>();

    private void UpdateExportSummary()
    {
        var shortlisted = ShortlistedCalls();
        var ticked = shortlisted.Count(c => c.IncludeInExport);
        ExportSummaryText.Text = shortlisted.Count == 0 ? string.Empty : $"{ticked} of {shortlisted.Count} ticked for export";
        ExportPdfButton.IsEnabled = ticked > 0;
        ToggleAllExportButton.Content = shortlisted.Count > 0 && ticked == shortlisted.Count ? "Untick all" : "Tick all";
        ToggleAllExportButton.Visibility = shortlisted.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        var answer = MessageBox.Show(this, "Replace your keyword groups with the defaults?", "Funder Finder",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

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
        KeywordGroupsScroller.ScrollToEnd();
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
        if (MessageBox.Show(this, $"Remove {label}{detail}?", "Funder Finder",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
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
