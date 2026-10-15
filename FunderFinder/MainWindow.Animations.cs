using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using FunderFinder.Services;

namespace FunderFinder;

/// <summary>
/// The window's animations: tab fade-in, fold-away Filters sections, the shortlist
/// leaf "pop", and the scan overlay's tree growing leaves. Durations are short on
/// purpose — they should feel smooth, never slow you down.
/// </summary>
public partial class MainWindow
{
    private static readonly Duration Quick = new(TimeSpan.FromMilliseconds(180));
    private static readonly Duration Fold = new(TimeSpan.FromMilliseconds(220));
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    // --- Tab switching ------------------------------------------------------------

    /// <summary>How long a tab takes to fade in when you switch to it.</summary>
    private static readonly Duration TabFade = new(TimeSpan.FromMilliseconds(320));

    /// <summary>The newly shown tab fades in while drifting up a few pixels.</summary>
    private static void FadeInPanel(FrameworkElement panel)
    {
        var shift = new TranslateTransform(0, 10);
        panel.RenderTransform = shift;
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TabFade) { EasingFunction = EaseOut });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TabFade) { EasingFunction = EaseOut });
    }

    // --- Fold-away sections -------------------------------------------------------

    /// <summary>Slides a section open (from zero height to its natural height) or shut.</summary>
    private static void AnimateSection(FrameworkElement section, bool open)
    {
        // Measure the natural height at the current width before animating towards it.
        section.BeginAnimation(HeightProperty, null);
        var from = section.Visibility == Visibility.Visible ? section.ActualHeight : 0;
        section.Visibility = Visibility.Visible;
        double to = 0;
        if (open)
        {
            var width = (section.Parent as FrameworkElement)?.ActualWidth ?? section.ActualWidth;
            section.Height = double.NaN;
            section.Measure(new Size(Math.Max(0, width - section.Margin.Left - section.Margin.Right), double.PositiveInfinity));
            to = section.DesiredSize.Height;
        }

        var slide = new DoubleAnimation(from, to, Fold) { EasingFunction = EaseOut };
        slide.Completed += (_, _) =>
        {
            // Hand the height back to layout so the section can grow/shrink with its content again.
            section.BeginAnimation(HeightProperty, null);
            section.Height = double.NaN;
            if (!open) section.Visibility = Visibility.Collapsed;
        };
        section.BeginAnimation(HeightProperty, slide);
        section.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 0 : 1, open ? 1 : 0, Fold));
    }

    /// <summary>▾ when open, turned to point right (▸) when folded.</summary>
    private static void SetChevron(TextBlock chevron, bool open, bool animate)
    {
        if (chevron.RenderTransform is not RotateTransform rotate) return;
        var angle = open ? 0 : -90;
        if (animate) rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, Fold) { EasingFunction = EaseOut });
        else
        {
            rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            rotate.Angle = angle;
        }
    }

    // --- Shortlist leaf -----------------------------------------------------------

    /// <summary>A small springy pop on the leaf you've just shortlisted.</summary>
    private static void PopLeaf(Button leafButton)
    {
        if (leafButton.Content is not FrameworkElement icon) return;
        // A transform made by the list's template is frozen and can't be animated, so give the
        // icon its own fresh one each time.
        var scale = new ScaleTransform();
        icon.RenderTransformOrigin = new Point(0.5, 0.5);
        icon.RenderTransform = scale;
        var pop = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromMilliseconds(320)) };
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.45, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)), EaseOut));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)),
            new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    // --- Scan overlay: a bare tree that grows leaves as sources finish -------------

    /// <summary>
    /// Where leaves grow on the tree: the base point (on a twig) and how far to turn the
    /// leaf from its drawn direction (which points up-right), in degrees clockwise.
    /// </summary>
    private static readonly (double X, double Y, double Angle)[] LeafSpots =
    {
        // Listed roughly in the order they appear, spread around the crown.
        (82, 46, -60), (122, 50, -50), (38, 54, -95), (100, 50, -20), (34, 86, -150),
        (128, 88, 10), (62, 50, -110), (76, 28, -80), (136, 60, 0), (26, 64, -140),
        (94, 30, -30), (58, 92, -120), (116, 72, -40), (46, 76, -80), (104, 86, -10),
        (80, 70, -100), (81, 66, -30), (40, 60, -130), (120, 56, -10), (66, 56, -140),
        (96, 54, 10), (78, 34, -120), (90, 36, 0), (38, 88, -110), (132, 64, 30),
        (30, 68, -170), (124, 86, -20),
    };

    // A few greens around the shortlist leaf colour, so the crown looks natural.
    private static readonly Brush[] LeafBrushes =
    {
        Frozen("#4B6043"), Frozen("#5E7553"), Frozen("#718A62"), Frozen("#55693F"),
    };

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static readonly Geometry LeafGeometry = Geometry.Parse("M 0,0 C 0,-6 4,-10 10,-10 C 10,-4 6,0 0,0 Z");

    private readonly List<Path> _treeLeaves = new();
    private DispatcherTimer? _leafTimer;
    private int _leavesShown;
    private int _leavesAllowed;
    private bool _overlayDismissed;

    /// <summary>Washes the window out and shows the bare tree, ready to grow.</summary>
    private void BeginScanOverlay(int sourceCount)
    {
        foreach (var leaf in _treeLeaves) TreeCanvas.Children.Remove(leaf);
        _treeLeaves.Clear();
        _leavesShown = 0;
        _leavesAllowed = 1;
        _overlayDismissed = false;

        for (var i = 0; i < LeafSpots.Length; i++)
        {
            var (x, y, angle) = LeafSpots[i];
            var size = 0.95 + (i * 37 % 10) / 25.0; // a little variety, the same every scan
            var leaf = new Path
            {
                Data = LeafGeometry,
                Fill = LeafBrushes[i % LeafBrushes.Length],
                RenderTransform = new TransformGroup
                {
                    Children = { new ScaleTransform(0, 0), new RotateTransform(angle) },
                },
                Opacity = 0,
            };
            Canvas.SetLeft(leaf, x);
            Canvas.SetTop(leaf, y);
            _treeLeaves.Add(leaf);
            leaf.Tag = size;
            TreeCanvas.Children.Add(leaf);
        }

        ScanOverlayTitle.Text = "Scanning for funding…";
        ScanOverlayDetail.Text = $"0 of {sourceCount} sources";
        ScanOverlay.BeginAnimation(OpacityProperty, null);
        ScanOverlay.Opacity = 0;
        ScanOverlay.Visibility = Visibility.Visible;
        ScanOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Quick));

        // Leaves sprout steadily up to what progress allows, so the tree never looks stuck.
        _leafTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(260), DispatcherPriority.Normal, (_, _) => GrowNextLeaf(), Dispatcher);
        _leafTimer.Interval = TimeSpan.FromMilliseconds(260);
        _leafTimer.Start();
    }

    /// <summary>A source finished: let the tree grow its share of leaves, and say which one.</summary>
    private void OnScanProgress(ScanProgress p)
    {
        // Run a little ahead of the finished count so leaves keep coming while slow sources work.
        _leavesAllowed = Math.Min(_treeLeaves.Count, (int)Math.Ceiling(_treeLeaves.Count * (p.Done + 0.6) / p.Total));
        ScanOverlayDetail.Text = $"{p.Done} of {p.Total} sources · {p.SourceName} {(p.Succeeded ? "done" : "had a problem")}";
    }

    private void GrowNextLeaf()
    {
        if (_leavesShown >= Math.Min(_leavesAllowed, _treeLeaves.Count)) return;
        var leaf = _treeLeaves[_leavesShown++];
        var size = (double)leaf.Tag;
        var scale = (ScaleTransform)((TransformGroup)leaf.RenderTransform).Children[0];
        var grow = new DoubleAnimation(0, size, new Duration(TimeSpan.FromMilliseconds(380)))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        leaf.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Quick));
    }

    /// <summary>Scan finished: fill the rest of the crown quickly, hold a moment, then fade away.</summary>
    private async Task EndScanOverlayAsync(string headline)
    {
        _leavesAllowed = _treeLeaves.Count;
        if (_leafTimer is not null) _leafTimer.Interval = TimeSpan.FromMilliseconds(45);
        ScanOverlayTitle.Text = headline;

        while (_leavesShown < _treeLeaves.Count && !_overlayDismissed)
            await Task.Delay(50);
        _leafTimer?.Stop();
        if (!_overlayDismissed) await Task.Delay(900);
        HideScanOverlay();
    }

    private void HideScanOverlay()
    {
        if (ScanOverlay.Visibility != Visibility.Visible) return;
        var fade = new DoubleAnimation(ScanOverlay.Opacity, 0, new Duration(TimeSpan.FromMilliseconds(260)));
        fade.Completed += (_, _) =>
        {
            ScanOverlay.Visibility = Visibility.Collapsed;
            ScanOverlay.BeginAnimation(OpacityProperty, null);
        };
        ScanOverlay.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Clicking the overlay hides it early; the scan carries on in the background.</summary>
    private void ScanOverlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _overlayDismissed = true;
        HideScanOverlay();
    }
}
