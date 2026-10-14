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
    private enum PanelView { Opportunities, Shortlist, Sources }

    private AppData? _data;
    private List<SourceEntry> _sources = new();
    private bool _isScanning;
    private DispatcherTimer? _dayTimer;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AcrylicHelper.EnableAcrylic(this, 0x26, 0x4D, 0x59, 190);

        _data = DataStore.Load();
        DataContext = _data;

        ((CollectionViewSource)Resources["OpportunitiesView"]).Source = _data.Calls;
        ((CollectionViewSource)Resources["ShortlistView"]).Source = _data.Calls;

        _sources = FundingScanner.AllSources
            .Select(s => new SourceEntry(s, !_data.DisabledSources.Contains(s.Name)))
            .ToList();
        SourcesListBox.ItemsSource = _sources;

        if (_data.WindowLeft.HasValue && _data.WindowTop.HasValue)
        {
            Left = _data.WindowLeft.Value;
            Top = _data.WindowTop.Value;
        }
        Width = _data.WindowWidth;
        Height = _data.WindowHeight;

        StatusText.Text = _data.LastScan is { } last
            ? $"Last scanned {last:d MMM 'at' HH:mm}."
            : "Not scanned yet — hit Scan to fetch calls.";

        SetView(PanelView.Opportunities);
        RefreshViews();

        // Deadline buckets depend on today's date, so re-sort periodically in case
        // the app sits open across midnight.
        _dayTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _dayTimer.Tick += (_, _) => RefreshViews();
        _dayTimer.Start();
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
    private void ShowSources_Click(object sender, RoutedEventArgs e) => SetView(PanelView.Sources);

    private void SetView(PanelView view)
    {
        OpportunitiesPanel.Visibility = view == PanelView.Opportunities ? Visibility.Visible : Visibility.Collapsed;
        ShortlistPanel.Visibility = view == PanelView.Shortlist ? Visibility.Visible : Visibility.Collapsed;
        SourcesPanel.Visibility = view == PanelView.Sources ? Visibility.Visible : Visibility.Collapsed;

        var active = (Brush)FindResource("ActivePillBrush");
        OpportunitiesButton.Background = view == PanelView.Opportunities ? active : Brushes.Transparent;
        ShortlistButton.Background = view == PanelView.Shortlist ? active : Brushes.Transparent;
        SourcesButton.Background = view == PanelView.Sources ? active : Brushes.Transparent;
    }

    private void OpportunitiesView_Filter(object sender, FilterEventArgs e)
        => e.Accepted = e.Item is FundingCall { IsDismissed: false } call && call.Bucket != DeadlineBucket.Closed;

    private void ShortlistView_Filter(object sender, FilterEventArgs e)
        => e.Accepted = e.Item is FundingCall { IsShortlisted: true };

    /// <summary>Re-filters, re-groups and re-sorts both call lists, then updates their empty-state hints.</summary>
    private void RefreshViews()
    {
        if (_data is null) return;
        foreach (var call in _data.Calls)
            call.RefreshDerived();

        var opportunities = ((CollectionViewSource)Resources["OpportunitiesView"]).View;
        var shortlist = ((CollectionViewSource)Resources["ShortlistView"]).View;
        opportunities?.Refresh();
        shortlist?.Refresh();

        OpportunitiesHint.Visibility = opportunities?.IsEmpty != false ? Visibility.Visible : Visibility.Collapsed;
        ShortlistHint.Visibility = shortlist?.IsEmpty != false ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Scanning -----------------------------------------------------------------

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null || _isScanning) return;

        var enabled = _sources.Where(s => s.IsEnabled).Select(s => s.Source).ToList();
        if (enabled.Count == 0)
        {
            StatusText.Text = "No sources are switched on — pick some under Sources.";
            return;
        }

        _isScanning = true;
        ScanButton.IsEnabled = false;
        StatusText.Text = $"Scanning {enabled.Count} source{(enabled.Count == 1 ? "" : "s")}...";

        try
        {
            var result = await FundingScanner.ScanAsync(enabled, _data.Calls);
            _data.LastScan = DateTime.Now;
            RefreshViews();
            SaveNow();

            var summary = result.Added > 0
                ? $"Found {result.Added} new call{(result.Added == 1 ? "" : "s")} ({result.Refreshed} already known)."
                : $"No new calls ({result.Refreshed} already known).";
            if (result.Errors.Count > 0)
                summary += "\nProblems with: " + string.Join("; ", result.Errors);
            StatusText.Text = summary;

            if (result.Added > 0 && !IsActive)
                (Application.Current as App)?.ShowNotification("New funding calls",
                    $"{result.Added} new call{(result.Added == 1 ? "" : "s")} found.");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            _isScanning = false;
            ScanButton.IsEnabled = true;
        }
    }

    // --- Call actions -------------------------------------------------------------

    private void Star_Click(object sender, RoutedEventArgs e)
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
            StatusText.Text = $"That call doesn't have a usable web link ({call.Url}).";
            return;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    // --- Sources ------------------------------------------------------------------

    private void SourceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        _data.DisabledSources = _sources.Where(s => !s.IsEnabled).Select(s => s.Name).ToList();
        SaveNow();
    }
}
