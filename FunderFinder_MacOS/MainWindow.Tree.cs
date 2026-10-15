using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using FunderFinder.Services;

namespace FunderFinder.Mac;

/// <summary>
/// The scan overlay: a bare tree that grows leaves as each source finishes (and, once a
/// day, a quick text-free replay). Same leaf spots and greens as the Windows app.
/// </summary>
public partial class MainWindow
{
    /// <summary>Leaf base point on a twig, and degrees to turn it from its drawn up-right direction.</summary>
    private static readonly (double X, double Y, double Angle)[] LeafSpots =
    {
        (82, 46, -60), (122, 50, -50), (38, 54, -95), (100, 50, -20), (34, 86, -150),
        (128, 88, 10), (62, 50, -110), (76, 28, -80), (136, 60, 0), (26, 64, -140),
        (94, 30, -30), (58, 92, -120), (116, 72, -40), (46, 76, -80), (104, 86, -10),
        (80, 70, -100), (81, 66, -30), (40, 60, -130), (120, 56, -10), (66, 56, -140),
        (96, 54, 10), (78, 34, -120), (90, 36, 0), (38, 88, -110), (132, 64, 30),
        (30, 68, -170), (124, 86, -20),
    };

    private static readonly IBrush[] LeafBrushes =
    {
        Brush.Parse("#4B6043"), Brush.Parse("#5E7553"), Brush.Parse("#718A62"), Brush.Parse("#55693F"),
    };

    private static readonly Geometry LeafGeometry = Geometry.Parse("M 0,0 C 0,-6 4,-10 10,-10 C 10,-4 6,0 0,0 Z");

    private readonly List<(Path Leaf, ScaleTransform Scale, double Size)> _treeLeaves = new();
    private DispatcherTimer? _leafTimer;
    private int _leavesShown;
    private int _leavesAllowed;
    private bool _overlayDismissed;
    private int _overlayRun; // bumps each time the overlay starts, so an old run can't hide a newer one

    /// <summary>Washes the window out and shows the bare tree, ready to grow. Returns this run's id.</summary>
    private int BeginScanOverlay(int sourceCount)
    {
        _overlayRun++;
        foreach (var (leaf, _, _) in _treeLeaves) TreeCanvas.Children.Remove(leaf);
        _treeLeaves.Clear();
        _leavesShown = 0;
        _leavesAllowed = 1;
        _overlayDismissed = false;

        for (var i = 0; i < LeafSpots.Length; i++)
        {
            var (x, y, angle) = LeafSpots[i];
            var scale = new ScaleTransform(0, 0)
            {
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(380), Easing = new BackEaseOut() },
                    new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(380), Easing = new BackEaseOut() },
                },
            };
            var leaf = new Path
            {
                Data = LeafGeometry,
                Fill = LeafBrushes[i % LeafBrushes.Length],
                RenderTransformOrigin = Avalonia.RelativePoint.TopLeft, // grow out from the twig
                RenderTransform = new TransformGroup { Children = { scale, new RotateTransform(angle) } },
            };
            Canvas.SetLeft(leaf, x);
            Canvas.SetTop(leaf, y);
            TreeCanvas.Children.Add(leaf);
            _treeLeaves.Add((leaf, scale, 0.95 + (i * 37 % 10) / 25.0));
        }

        ScanOverlayTitle.Text = "Scanning for funding…";
        ScanOverlayDetail.Text = $"0 of {sourceCount} sources";
        ScanOverlayTitle.IsVisible = ScanOverlayDetail.IsVisible = true; // the morning replay hides these

        ScanOverlay.Opacity = 0;
        ScanOverlay.IsVisible = true;
        Dispatcher.UIThread.Post(() => ScanOverlay.Opacity = 1, DispatcherPriority.Render);

        // Leaves sprout steadily up to what progress allows, so the tree never looks stuck.
        _leafTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(260), DispatcherPriority.Normal, (_, _) => GrowNextLeaf());
        _leafTimer.Interval = TimeSpan.FromMilliseconds(260);
        _leafTimer.Start();
        return _overlayRun;
    }

    /// <summary>A source finished: let the tree grow its share of leaves, and say which one.</summary>
    private void OnScanProgress(ScanProgress p)
    {
        _leavesAllowed = Math.Min(_treeLeaves.Count, (int)Math.Ceiling(_treeLeaves.Count * (p.Done + 0.6) / p.Total));
        ScanOverlayDetail.Text = $"{p.Done} of {p.Total} sources · {p.SourceName} {(p.Succeeded ? "done" : "had a problem")}";
    }

    private void GrowNextLeaf()
    {
        if (_leavesShown >= Math.Min(_leavesAllowed, _treeLeaves.Count)) return;
        var (_, scale, size) = _treeLeaves[_leavesShown++];
        scale.ScaleX = size;
        scale.ScaleY = size;
    }

    /// <summary>Scan finished: fill the rest of the crown quickly, hold a moment, then fade away.</summary>
    private async Task EndScanOverlayAsync(string headline, int run)
    {
        if (run != _overlayRun) return;
        _leavesAllowed = _treeLeaves.Count;
        if (_leafTimer is not null) _leafTimer.Interval = TimeSpan.FromMilliseconds(45);
        ScanOverlayTitle.Text = headline;

        while (_leavesShown < _treeLeaves.Count && !_overlayDismissed && run == _overlayRun)
            await Task.Delay(50);
        if (run != _overlayRun) return;
        _leafTimer?.Stop();
        if (!_overlayDismissed) await Task.Delay(900);
        if (run == _overlayRun) HideScanOverlay();
    }

    private void HideScanOverlay()
    {
        if (!ScanOverlay.IsVisible) return;
        ScanOverlay.Opacity = 0;
        DispatcherTimer.RunOnce(() => { if (ScanOverlay.Opacity == 0) ScanOverlay.IsVisible = false; }, TimeSpan.FromMilliseconds(270));
    }

    /// <summary>Clicking the overlay hides it early; the scan carries on in the background.</summary>
    private void ScanOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _overlayDismissed = true;
        HideScanOverlay();
    }
}
