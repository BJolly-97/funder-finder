using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FunderFinder.Native;

/// <summary>
/// Attached property giving a list or scroll area a fixed, gentle mouse-wheel step
/// in pixels. WPF lists otherwise scroll by whole items — three tall call rows per
/// wheel notch — which feels far too quick. Set in a style:
/// <c>&lt;Setter Property="native:WheelScroll.PixelsPerNotch" Value="40"/&gt;</c>.
/// </summary>
public static class WheelScroll
{
    public static readonly DependencyProperty PixelsPerNotchProperty = DependencyProperty.RegisterAttached(
        "PixelsPerNotch", typeof(double), typeof(WheelScroll), new PropertyMetadata(0.0, OnChanged));

    public static double GetPixelsPerNotch(DependencyObject d) => (double)d.GetValue(PixelsPerNotchProperty);
    public static void SetPixelsPerNotch(DependencyObject d, double value) => d.SetValue(PixelsPerNotchProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewMouseWheel -= OnPreviewMouseWheel;
        if ((double)e.NewValue > 0) element.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject d) return;
        var scroller = d as ScrollViewer ?? FindScrollViewer(d);
        if (scroller is null || scroller.ScrollableHeight <= 0) return; // let it bubble to an outer scroller

        // Pixel scrolling, so the step is in pixels rather than items.
        scroller.CanContentScroll = false;
        var step = GetPixelsPerNotch(d) * e.Delta / 120.0; // 120 = one wheel notch
        scroller.ScrollToVerticalOffset(scroller.VerticalOffset - step);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScrollViewer(child) is { } deeper) return deeper;
        }
        return null;
    }
}
