using System.Windows;
using System.Windows.Controls;

namespace GuiaSys.DiskManager.Controls;

/// <summary>
/// A stretch-to-width, proportional horizontal bar for partition extents. Unlike a
/// fixed 880px canvas it adapts to resizing/DPI and preserves a minimum hit area
/// for small ESP/MSR/recovery partitions (exact sizes remain in accessible tooltips).
/// Read-only visual layout: it cannot invoke disk commands.
/// </summary>
public sealed class ProportionalPartitionPanel : Panel
{
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

    /// <summary>
    /// Splits available width without ever guessing a partition's physical size.
    /// All returned widths sum to the full bar within floating-point tolerance.
    /// </summary>
    internal static double[] CalculateWidths(IReadOnlyList<long> sizes, double availableWidth)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        if (sizes.Any(s => s < 0))
            throw new ArgumentOutOfRangeException(nameof(sizes), "Extent must not be negative.");
        if (double.IsNaN(availableWidth) || availableWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(availableWidth));
        if (sizes.Count == 0) return [];
        var width = double.IsPositiveInfinity(availableWidth) ? 880d : availableWidth;
        if (width == 0) return new double[sizes.Count];
        // Reserve at most 38px per extent, so system partitions can be clicked.
        // When too many extents share a narrow area, reduce the minimum to fit.
        var minimum = Math.Min(38d, width / sizes.Count);
        var leftover = Math.Max(0d, width - minimum * sizes.Count);
        var weightTotal = sizes.Sum(v => (double)v);
        var result = new double[sizes.Count];
        var used = 0d;
        for (var i = 0; i < result.Length - 1; i++)
        {
            var fraction = weightTotal > 0 ? sizes[i] / weightTotal : 1d / sizes.Count;
            result[i] = minimum + leftover * fraction;
            used += result[i];
        }
        result[^1] = Math.Max(0d, width - used);
        return result;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var contentWidth = double.IsInfinity(availableSize.Width) ? 880d :
            Math.Max(0d, availableSize.Width);
        var sizes = InternalChildren.Cast<UIElement>()
            .Select(GetExtentBytes).ToArray();
        var widths = CalculateWidths(sizes, contentWidth);
        var desiredHeight = 72d;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Measure(new Size(widths[i], double.PositiveInfinity));
            desiredHeight = Math.Max(desiredHeight, InternalChildren[i].DesiredSize.Height);
        }
        return new Size(contentWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var widths = CalculateWidths(InternalChildren.Cast<UIElement>()
            .Select(GetExtentBytes).ToArray(), Math.Max(0d, finalSize.Width));
        var offset = 0d;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Arrange(new Rect(offset, 0d, widths[i], finalSize.Height));
            offset += widths[i];
        }
        return finalSize;
    }
}
