using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FileViz.Core;
namespace FileViz.App.Views;

public sealed class Treemap : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items), typeof(IReadOnlyList<Breakdown>), typeof(Treemap), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<Breakdown>? Items
    {
        get => (IReadOnlyList<Breakdown>?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value);
    }
    public event EventHandler<string>? FolderChosen;
    private readonly List<(Rect Rect, Breakdown Item)> tiles = [];
    private static readonly Color[] Colors = [Color.FromRgb(31, 111, 139), Color.FromRgb(43, 138, 122), Color.FromRgb(92, 105, 171), Color.FromRgb(166, 107, 64), Color.FromRgb(117, 91, 156), Color.FromRgb(71, 130, 165)];
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        tiles.Clear();
        var values = Items?.Where(x => x.Bytes > 0).Take(100).OrderByDescending(x => x.Bytes).ToArray() ?? [];
        if (values.Length == 0)
        {
            dc.DrawRectangle(TryFindResource("SubtleFillColorSecondaryBrush") as Brush ?? Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            return;
        }
        Layout(values, 0, values.Length, new Rect(0, 0, ActualWidth, ActualHeight));
        for (var index = 0; index < tiles.Count; index++)
        {
            var (rect, item) = tiles[index];
            if (rect.Width < 2 || rect.Height < 2)
                continue;
            var box = new Rect(rect.X + 1, rect.Y + 1, Math.Max(0, rect.Width - 2), Math.Max(0, rect.Height - 2));
            dc.DrawRoundedRectangle(new SolidColorBrush(Colors[index % Colors.Length]), null, box, 3, 3);
            if (box.Width > 75 && box.Height > 30)
            {
                var name = System.IO.Path.GetFileName(item.Name.TrimEnd('\\'));
                if (name.Length == 0)
                    name = item.Name;
                var text = new FormattedText(name + "\n" + Format.Bytes(item.Bytes), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(1, box.Width - 12), MaxTextHeight = Math.Max(1, box.Height - 8), Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawText(text, new Point(box.X + 6, box.Y + 4));
            }
        }
    }
    private void Layout(Breakdown[] items, int start, int count, Rect rect)
    {
        if (count == 1)
        {
            tiles.Add((rect, items[start]));
            return;
        }
        var total = 0d;
        for (var i = start; i < start + count; i++)
            total += items[i].Bytes;
        var sum = 0d;
        var split = 1;
        for (var i = start; i < start + count - 1; i++)
        {
            sum += items[i].Bytes;
            split = i - start + 1;
            if (sum >= total / 2)
                break;
        }
        var fraction = sum / total;
        if (rect.Width >= rect.Height)
        {
            var width = rect.Width * fraction;
            Layout(items, start, split, new(rect.X, rect.Y, width, rect.Height));
            Layout(items, start + split, count - split, new(rect.X + width, rect.Y, rect.Width - width, rect.Height));
        }
        else
        {
            var height = rect.Height * fraction;
            Layout(items, start, split, new(rect.X, rect.Y, rect.Width, height));
            Layout(items, start + split, count - split, new(rect.X, rect.Y + height, rect.Width, rect.Height - height));
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        var tile = tiles.FirstOrDefault(x => x.Rect.Contains(point));
        ToolTip = tile.Item == null ? null : tile.Item.Name + "\n" + Format.Bytes(tile.Item.Bytes);
        Cursor = tile.Item == null ? Cursors.Arrow : Cursors.Hand;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var tile = tiles.FirstOrDefault(x => x.Rect.Contains(e.GetPosition(this)));
        if (tile.Item != null)
            FolderChosen?.Invoke(this, tile.Item.Name);
    }
}
