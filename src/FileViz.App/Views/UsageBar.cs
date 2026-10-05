using System.Windows;
using System.Windows.Media;
namespace FileViz.App.Views;

/// <summary>One segment of a usage bar. <paramref name="Brush"/> is a resource key; "Hatched" draws a striped segment.</summary>
public sealed record BarSegment(string Label, long Bytes, string Brush, string Value);

/// <summary>Stacked horizontal bar: segments proportional to <see cref="Total"/>, the remainder shown as the track.</summary>
public sealed class UsageBar : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(nameof(Segments), typeof(IReadOnlyList<BarSegment>), typeof(UsageBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TotalProperty = DependencyProperty.Register(nameof(Total), typeof(long), typeof(UsageBar), new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<BarSegment>? Segments
    {
        get => (IReadOnlyList<BarSegment>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value);
    }
    public long Total
    {
        get => (long)GetValue(TotalProperty); set => SetValue(TotalProperty, value);
    }
    public UsageBar() => Height = 10;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var radius = bounds.Height / 2;
        dc.PushClip(new RectangleGeometry(bounds, radius, radius));
        dc.DrawRectangle(TryFindResource("ControlAltFillColorSecondaryBrush") as Brush ?? Brushes.Gainsboro, null, bounds);
        var segments = Segments ?? [];
        var total = Math.Max(Total, segments.Sum(x => x.Bytes));
        if (total > 0)
        {
            var x = 0d;
            foreach (var segment in segments)
            {
                var width = bounds.Width * segment.Bytes / total;
                if (width <= 0)
                    continue;
                var brush = segment.Brush == "Hatched" ? Hatch() : TryFindResource(segment.Brush) as Brush ?? Brushes.Gray;
                if (SystemParameters.HighContrast)
                    brush = SystemColors.WindowTextBrush;
                dc.DrawRectangle(brush, null, new Rect(x, 0, Math.Max(0, width - 1.5), bounds.Height));
                x += width;
            }
        }
        dc.Pop();
    }
    private Brush Hatch()
    {
        var stroke = TryFindResource("ControlStrongStrokeColorDefaultBrush") as Brush ?? Brushes.Gray;
        var geometry = new GeometryGroup();
        geometry.Children.Add(new LineGeometry(new Point(0, 6), new Point(6, 0)));
        var drawing = new GeometryDrawing(null, new Pen(stroke, 1.4), geometry);
        return new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 6, 6), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 6, 6), ViewboxUnits = BrushMappingMode.Absolute };
    }
}
