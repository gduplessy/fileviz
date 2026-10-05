using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using FileViz.Core;
namespace FileViz.App.Views;

/// <summary>
/// Two-level squarified space map. Top-level folders with children draw as labelled containers; leaves are colored by
/// file type or by age. Click selects, double-click or Enter opens a folder, arrow keys move between tiles.
/// </summary>
public sealed class Treemap : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items), typeof(IReadOnlyList<MapNode>), typeof(Treemap), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ColorByAgeProperty = DependencyProperty.Register(nameof(ColorByAge), typeof(bool), typeof(Treemap), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SelectedPathProperty = DependencyProperty.Register(nameof(SelectedPath), typeof(string), typeof(Treemap), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public IReadOnlyList<MapNode>? Items
    {
        get => (IReadOnlyList<MapNode>?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value);
    }
    public bool ColorByAge
    {
        get => (bool)GetValue(ColorByAgeProperty); set => SetValue(ColorByAgeProperty, value);
    }
    public string? SelectedPath
    {
        get => (string?)GetValue(SelectedPathProperty); set => SetValue(SelectedPathProperty, value);
    }
    public event EventHandler<string>? FolderChosen;
    public event EventHandler<MapNode>? TileSelected;
    private static readonly string[] CategoryKeys = ["CategoryDiskBrush", "CategoryVideoBrush", "CategoryCodeBrush", "CategoryImageBrush", "CategoryAudioBrush", "CategoryDocumentBrush", "CategoryArchiveBrush", "CategoryCacheBrush", "CategoryOtherBrush"];
    private static readonly bool[] CategoryDarkLabel = [false, false, false, false, false, false, true, true, true];
    private static readonly string[] AgeKeys = ["Age0Brush", "Age1Brush", "Age2Brush", "Age3Brush", "Age4Brush"];
    private static readonly bool[] AgeDarkLabel = [true, true, true, false, false];
    private readonly List<(Rect Rect, MapNode Node, bool Container)> tiles = [];
    private const double Header = 20, Gap = 2;
    public Treemap()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
    }
    /// <summary>Fill and label color for a leaf, from the file-type or age palette.</summary>
    public static (string Fill, bool DarkLabel) Palette(Composition composition, bool byAge)
    {
        if (byAge)
        {
            var bucket = composition.AgeBucket;
            return (AgeKeys[bucket], AgeDarkLabel[bucket]);
        }
        var category = (int)composition.Dominant;
        return (CategoryKeys[category], CategoryDarkLabel[category]);
    }
    /// <summary>Resource key of a file-type color.</summary>
    public static string CategoryKey(FileCategory category) => CategoryKeys[(int)category];
    private Brush Resource(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        tiles.Clear();
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var items = Items?.Where(x => x.Bytes > 0).ToArray() ?? [];
        var highContrast = SystemParameters.HighContrast;
        if (items.Length == 0)
        {
            dc.DrawRoundedRectangle(Resource("SubtleFillColorSecondaryBrush", Brushes.Transparent), null, bounds, 8, 8);
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = Resource("TextFillColorPrimaryBrush", Brushes.Black);
        var containerFill = highContrast ? SystemColors.WindowBrush : Resource("CardBackgroundFillColorSecondaryBrush", Brushes.WhiteSmoke);
        var containerStroke = new Pen(highContrast ? SystemColors.WindowTextBrush : Resource("CardStrokeColorDefaultBrush", Brushes.LightGray), 1);
        var accent = Resource("AccentFillColorDefaultBrush", Brushes.DodgerBlue);
        var ring = new Pen(accent, 2);
        var halo = new Pen(Resource("CardBackgroundFillColorDefaultBrush", Brushes.White), 2);
        var outline = new Pen(SystemColors.WindowTextBrush, 1);
        var rects = TreemapLayout.Squarify(items.Select(x => (double)x.Bytes).ToArray(), new(0, 0, bounds.Width, bounds.Height));
        Rect? selected = null;
        for (var index = 0; index < items.Length; index++)
        {
            var node = items[index];
            var r = rects[index];
            var outer = new Rect(r.X + Gap / 2, r.Y + Gap / 2, Math.Max(0, r.Width - Gap), Math.Max(0, r.Height - Gap));
            if (outer.Width < 2 || outer.Height < 2)
                continue;
            var children = node.Children.Where(x => x.Bytes > 0).ToArray();
            var nested = node.IsDirectory && children.Length > 0 && outer.Width > 70 && outer.Height > 56;
            if (!nested)
            {
                DrawLeaf(node, outer);
                continue;
            }
            dc.DrawRoundedRectangle(containerFill, containerStroke, outer, 4, 4);
            tiles.Add((outer, node, true));
            DrawText($"{node.Name}  {Format.Bytes(node.Bytes)}", new Point(outer.X + 6, outer.Y + 3), outer.Width - 12, Header - 4, text, 11, FontWeights.SemiBold);
            var inner = new TreemapRect(outer.X + 3, outer.Y + Header, outer.Width - 6, outer.Height - Header - 3);
            // Bytes in items too small to list stay as container background, so listed children keep true proportions.
            var remainder = Math.Max(0, node.Bytes - children.Sum(x => x.Bytes));
            var childRects = TreemapLayout.Squarify(children.Select(x => (double)x.Bytes).Append(remainder).ToArray(), inner);
            for (var child = 0; child < children.Length; child++)
            {
                var c = childRects[child];
                DrawLeaf(children[child], new Rect(c.X + Gap / 2, c.Y + Gap / 2, Math.Max(0, c.Width - Gap), Math.Max(0, c.Height - Gap)));
            }
        }
        if (selected is Rect s)
        {
            dc.DrawRoundedRectangle(null, halo, s, 3, 3);
            dc.DrawRoundedRectangle(null, ring, Rect.Inflate(s, 2, 2), 4, 4);
        }

        void DrawLeaf(MapNode node, Rect box)
        {
            if (box.Width < 2 || box.Height < 2)
                return;
            var (key, dark) = Palette(node.Composition, ColorByAge);
            var fill = highContrast ? SystemColors.WindowBrush : Resource(key, Brushes.SlateGray);
            dc.DrawRoundedRectangle(fill, highContrast ? outline : null, box, 3, 3);
            tiles.Add((box, node, false));
            if (string.Equals(node.Path, SelectedPath, StringComparison.OrdinalIgnoreCase))
                selected = box;
            if (box.Width >= 70 && box.Height >= 34)
            {
                var label = highContrast ? SystemColors.WindowTextBrush : dark ? Resource("TileDarkLabelBrush", Brushes.Black) : Resource("TileLightLabelBrush", Brushes.White);
                DrawText(node.Name, new Point(box.X + 6, box.Y + 4), box.Width - 12, 15, label, 11, FontWeights.SemiBold);
                DrawText(Format.Bytes(node.Bytes), new Point(box.X + 6, box.Y + 18), box.Width - 12, 15, label, 11, FontWeights.Normal);
            }
        }
        void DrawText(string value, Point origin, double width, double height, Brush brush, double size, FontWeight weight)
        {
            if (width < 8 || height < 8)
                return;
            var formatted = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush, dpi)
            { MaxTextWidth = width, MaxTextHeight = height, Trimming = TextTrimming.CharacterEllipsis, MaxLineCount = 1 };
            dc.DrawText(formatted, origin);
        }
    }
    private (Rect Rect, MapNode Node, bool Container)? Hit(Point point)
    {
        // Leaves are drawn after their container, so the last hit is the most specific.
        for (var index = tiles.Count - 1; index >= 0; index--)
            if (tiles[index].Rect.Contains(point))
                return tiles[index];
        return null;
    }
    private void Select(MapNode node)
    {
        SelectedPath = node.Path;
        TileSelected?.Invoke(this, node);
        UpdateAutomation(node);
    }
    private void UpdateAutomation(MapNode node)
    {
        var detail = ColorByAge ? AgeBuckets.Name(node.Composition.AgeBucket) : FileCategories.Name(node.Composition.Dominant);
        System.Windows.Automation.AutomationProperties.SetItemStatus(this, $"{node.Name}, {Format.Bytes(node.Bytes)}, {detail}{(node.IsDirectory ? ", folder" : "")}");
        UIElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(AutomationEvents.PropertyChanged);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Hit(e.GetPosition(this)) is not { } tile)
        {
            ToolTip = null;
            return;
        }
        var node = tile.Node;
        var detail = node.IsDirectory ? $"{node.Files:N0} files · mostly {FileCategories.Name(node.Composition.Dominant).ToLowerInvariant()}" : FileCategories.Name(node.Composition.Dominant);
        ToolTip = $"{node.Path}\n{Format.Bytes(node.Bytes)} · {detail}\nModified: {AgeBuckets.Name(node.Composition.AgeBucket).ToLowerInvariant()}";
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (Hit(e.GetPosition(this)) is not { } tile)
            return;
        if (e.ClickCount == 2 && tile.Node.IsDirectory)
            FolderChosen?.Invoke(this, tile.Node.Path);
        else
            Select(tile.Node);
        e.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (tiles.Count == 0)
            return;
        var order = tiles.Where(x => !x.Container).OrderBy(x => Math.Round(x.Rect.Y / 8)).ThenBy(x => x.Rect.X).ToList();
        var current = order.FindIndex(x => string.Equals(x.Node.Path, SelectedPath, StringComparison.OrdinalIgnoreCase));
        switch (e.Key)
        {
            case Key.Enter when current >= 0:
                var node = order[current].Node;
                if (node.IsDirectory)
                    FolderChosen?.Invoke(this, node.Path);
                e.Handled = true;
                return;
            case Key.Right or Key.Down:
                Select(order[Math.Min(order.Count - 1, current + 1)].Node);
                e.Handled = true;
                return;
            case Key.Left or Key.Up when e.KeyboardDevice.Modifiers == ModifierKeys.None:
                Select(order[Math.Max(0, current < 0 ? 0 : current - 1)].Node);
                e.Handled = true;
                return;
        }
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
