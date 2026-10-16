using System.Windows;
using System.Windows.Controls;

namespace GuiaSys.DiskManager.Controls;

/// <summary>
/// Read-only partition diagram: physical proportions are preserved as far as
/// possible, but labels for EFI/MSR/Recovery must remain readable.
/// If the minimum labels do not fit, the containing horizontal ScrollViewer
/// exposes the remaining extents instead of compressing text to "N...".
/// This panel never invokes disk operations.
/// </summary>
public sealed class ProportionalPartitionPanel : Panel
{
    /// <summary>Minimum visual width for each partition (WPF device-independent pixels).</summary>
    public const double MinimumReadableWidth = 100d;

    public static readonly DependencyProperty ExtentBytesProperty =
        DependencyProperty.RegisterAttached("ExtentBytes", typeof(long),
            typeof(ProportionalPartitionPanel),
            new FrameworkPropertyMetadata(0L,
                FrameworkPropertyMetadataOptions.AffectsParentMeasure |
                FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static long GetExtentBytes(DependencyObject element) =>
        (long)element.GetValue(ExtentBytesProperty);
    public static void SetExtentBytes(DependencyObject element, long value) =>
        element.SetValue(ExtentBytesProperty, value);

    // A ScrollViewer measures children at infinite width. Bind to its viewport
    // to still fill wide screens and to request overflow when labels do not fit.
    public static readonly DependencyProperty ViewportWidthProperty =
        DependencyProperty.Register(nameof(ViewportWidth), typeof(double),
            typeof(ProportionalPartitionPanel),
            new FrameworkPropertyMetadata(0d,
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ViewportWidth
    {
        get => (double)GetValue(ViewportWidthProperty);
        set => SetValue(ViewportWidthProperty, value);
    }

    /// <summary>
    /// Gets the *display* width. The viewport is never allowed to force
    /// a partition below the minimum legible label width.
    /// </summary>
    internal static double RequiredWidth(int extentCount, double viewportWidth)
    {
        if (extentCount < 0)
            throw new ArgumentOutOfRangeException(nameof(extentCount));
        if (double.IsNaN(viewportWidth) || viewportWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        var safeViewport = double.IsPositiveInfinity(viewportWidth) ? 0d : viewportWidth;
        return Math.Max(MinimumReadableWidth * extentCount, safeViewport);
    }

    /// <summary>
    /// Keeps large extents proportional with a guaranteed label width for
    /// small ones. Caller must pass RequiredWidth when used in the UI.
    /// </summary>
    internal static double[] CalculateWidths(IReadOnlyList<long> sizes, double availableWidth)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        if (sizes.Any(s => s < 0))
            throw new ArgumentOutOfRangeException(nameof(sizes), "Extent cannot be negative.");
        if (double.IsNaN(availableWidth) || availableWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(availableWidth));
        if (sizes.Count == 0) return [];
        var width = double.IsPositiveInfinity(availableWidth) ? 880d : availableWidth;
        if (width == 0) return new double[sizes.Count];

        // Keep compatibility for callers which ask for a mathematically smaller
        // canvas; the WPF panel itself always calls RequiredWidth first.
        var minimum = Math.Min(MinimumReadableWidth, width / sizes.Count);
        var extra = Math.Max(0d, width - minimum * sizes.Count);
        var total = sizes.Sum(s => (double)s);
        var result = new double[sizes.Count];
        var used = 0d;
        for (var i = 0; i < sizes.Count - 1; i++)
        {
            var weight = total > 0d ? sizes[i] / total : 1d / sizes.Count;
            result[i] = minimum + extra * weight;
            used += result[i];
        }
        result[^1] = Math.Max(0d, width - used);
        return result;
    }

    private long[] ExtentSizes() => InternalChildren.Cast<UIElement>()
        .Select(GetExtentBytes).ToArray();

    protected override Size MeasureOverride(Size availableSize)
    {
        var sizes = ExtentSizes();
        var target = double.IsInfinity(availableSize.Width)
            ? ViewportWidth
            : availableSize.Width;
        var width = RequiredWidth(sizes.Length, target);
        var widths = CalculateWidths(sizes, width);
        var height = 70d;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Measure(new Size(widths[i], double.PositiveInfinity));
            height = Math.Max(height, InternalChildren[i].DesiredSize.Height);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var sizes = ExtentSizes();
        var widths = CalculateWidths(sizes,
            RequiredWidth(sizes.Length, finalSize.Width));
        var x = 0d;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Arrange(new Rect(x, 0d, widths[i], finalSize.Height));
            x += widths[i];
        }
        return finalSize;
    }
}
