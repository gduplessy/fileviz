using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FileViz.App.Views;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using Microsoft.Win32;
namespace FileViz.App.ViewModels;

public enum ExplorerList
{
    Files, Folders, Types
}

/// <summary>One step of the Explorer breadcrumb.</summary>
public sealed record Crumb(string Name, string Path, bool IsLast);

/// <summary>A legend entry under the volume bar.</summary>
public sealed record LegendItem(Brush Fill, string Label, string Value);

/// <summary>A file row with its type swatch and a bar relative to the largest row.</summary>
public sealed record FileRow(FileEntry Entry, Brush Swatch, double BarWidth);

/// <summary>What the inspector shows for the selected file or folder.</summary>
public sealed record Inspection(string Path, string Name, string Kind, Brush Swatch, string Size, double SharePercent, string Share, string Folder, string Allocated, string Modified, string Attributes, string Identity, string Note, bool IsDirectory)
{
    public bool HasNote => Note.Length > 0;
    public bool IsFile => !IsDirectory;
}

/// <summary>Space map, largest files, folders and file types for the selected snapshot and root.</summary>
public sealed class ExplorerViewModel : Bindable, ISnapshotSection
{
    private readonly SessionViewModel session;
    private int page; private string? parent; private long refreshRevision; private long referenceTicks; private Composition? rootComposition;
    private (long Total, long Free, long Unattributed, bool Volume) volume;
    public SessionViewModel Session => session;
    public CleanupViewModel Cleanup
    {
        get;
    }
    public ObservableCollection<FileRow> Files { get; } = [];
    public ObservableCollection<Breakdown> Folders { get; } = [];
    public ObservableCollection<Breakdown> ExtensionsBreakdown { get; } = [];
    public ObservableCollection<Crumb> Breadcrumbs { get; } = [];
    private IReadOnlyList<MapNode> mapItems = []; public IReadOnlyList<MapNode> MapItems
    {
        get => mapItems; private set => Set(ref mapItems, value);
    }
    private IReadOnlyList<BarSegment> segments = []; public IReadOnlyList<BarSegment> Segments
    {
        get => segments; private set => Set(ref segments, value);
    }
    private IReadOnlyList<LegendItem> legend = []; public IReadOnlyList<LegendItem> Legend
    {
        get => legend; private set => Set(ref legend, value);
    }
    private long barTotal; public long BarTotal
    {
        get => barTotal; private set => Set(ref barTotal, value);
    }
    private bool colorByAge; public bool ColorByAge
    {
        get => colorByAge; set
        {
            if (!Set(ref colorByAge, value))
                return;
            Changed(nameof(ColorByType));
            Changed(nameof(MapHint));
            BuildSegments();
            RebuildFileRows();
            Reinspect();
            ColorModeChanged?.Invoke(this, value);
        }
    }
    public bool ColorByType
    {
        get => !ColorByAge; set => ColorByAge = !value;
    }
    /// <summary>Raised when the user switches between type and age coloring, so it can be remembered.</summary>
    public event EventHandler<bool>? ColorModeChanged;
    public bool ShowLogical
    {
        get => !Allocated; set { if (value && Allocated) SetAllocated(false); }
    }
    public bool ShowAllocated
    {
        get => Allocated; set { if (value && !Allocated) SetAllocated(true); }
    }
    public string MapHint => ColorByAge ? "Older is darker. Double-click a folder to open it." : "Colors show file type. Double-click a folder to open it.";
    private ExplorerList list = ExplorerList.Files; public ExplorerList List
    {
        get => list; set
        {
            if (Set(ref list, value))
            {
                Changed(nameof(ShowFiles));
                Changed(nameof(ShowFolders));
                Changed(nameof(ShowTypes));
            }
        }
    }
    public bool ShowFiles
    {
        get => List == ExplorerList.Files; set { if (value) List = ExplorerList.Files; }
    }
    public bool ShowFolders
    {
        get => List == ExplorerList.Folders; set { if (value) List = ExplorerList.Folders; }
    }
    public bool ShowTypes
    {
        get => List == ExplorerList.Types; set { if (value) List = ExplorerList.Types; }
    }
    private string volumeTitle = ""; public string VolumeTitle
    {
        get => volumeTitle; private set => Set(ref volumeTitle, value);
    }
    private string volumeCaption = ""; public string VolumeCaption
    {
        get => volumeCaption; private set => Set(ref volumeCaption, value);
    }
    private string filesText = ""; public string FilesText
    {
        get => filesText; private set => Set(ref filesText, value);
    }
    private string logicalText = ""; public string LogicalText
    {
        get => logicalText; private set => Set(ref logicalText, value);
    }
    private string allocatedText = ""; public string AllocatedText
    {
        get => allocatedText; private set => Set(ref allocatedText, value);
    }
    private string diagnosticsText = ""; public string DiagnosticsText
    {
        get => diagnosticsText; private set => Set(ref diagnosticsText, value);
    }
    private string coverage = ""; public string CoverageText
    {
        get => coverage; private set => Set(ref coverage, value);
    }
    private string summary = "No snapshot selected"; public string SummaryText
    {
        get => summary; private set => Set(ref summary, value);
    }
    private string locationText = ""; public string LocationText
    {
        get => locationText; private set => Set(ref locationText, value);
    }
    private string? selectedPath; public string? SelectedPath
    {
        get => selectedPath; set => Set(ref selectedPath, value);
    }
    private Inspection? inspection; public Inspection? Inspection
    {
        get => inspection; private set
        {
            if (Set(ref inspection, value))
                Changed(nameof(HasInspection));
        }
    }
    public bool HasInspection => Inspection != null;
    public string Search { get; set; } = ""; public string Extension { get; set; } = ""; public string MinimumMiB { get; set; } = "0";
    public DateTime? ModifiedAfter
    {
        get; set;
    }
    public bool Allocated { get; private set; }
    public bool HiddenOnly { get; set; } = false;
    public string BrowseLabel => parent ?? session.CurrentRoot ?? "No root selected";
    public string BrowseCaption => parent == null ? "Largest files anywhere in this root" : "Contents of this folder";
    public string PageLabel => $"Page {page + 1} · up to 250 rows";
    public ICommand FilterCommand
    {
        get;
    }
    public ICommand LargestCommand
    {
        get;
    }
    public ICommand UpCommand
    {
        get;
    }
    public ICommand PreviousCommand
    {
        get;
    }
    public ICommand NextCommand
    {
        get;
    }
    public ICommand ExportCommand
    {
        get;
    }
    public ICommand CrumbCommand
    {
        get;
    }
    public ExplorerViewModel(SessionViewModel session, CleanupViewModel cleanup)
    {
        this.session = session;
        Cleanup = cleanup;
        FilterCommand = new ActionCommand(() => { page = 0; session.Run(RefreshAsync); }, () => session.HasSnapshot && !session.Busy);
        LargestCommand = new ActionCommand(() => { parent = null; page = 0; ClearSelection(); ChangedLocation(); session.Run(RefreshAsync); }, () => session.HasSnapshot && !session.Busy);
        UpCommand = new ActionCommand(() =>
        {
            if (parent != null)
            {
                var above = Path.GetDirectoryName(parent);
                parent = above != null && session.CurrentRoot != null && Paths.Within(above, session.CurrentRoot) && !Paths.Normalize(above).Equals(Paths.Normalize(session.CurrentRoot), StringComparison.OrdinalIgnoreCase) ? above : null;
            }
            page = 0;
            ClearSelection();
            ChangedLocation();
            session.Run(RefreshAsync);
        }, () => session.HasSnapshot && !session.Busy && parent != null);
        PreviousCommand = new ActionCommand(() => { page = Math.Max(0, page - 1); session.Run(RefreshAsync); }, () => page > 0 && !session.Busy);
        NextCommand = new ActionCommand(() => { page++; session.Run(RefreshAsync); }, () => Files.Count == 250 && !session.Busy);
        ExportCommand = new ActionCommand(() => session.Run(ExportAsync), () => session.HasSnapshot && !session.Busy);
        CrumbCommand = new ParameterCommand(target =>
        {
            if (target is not string path)
                return;
            if (session.CurrentRoot != null && Paths.Normalize(path).Equals(Paths.Normalize(session.CurrentRoot), StringComparison.OrdinalIgnoreCase))
            {
                parent = null;
                page = 0;
                ClearSelection();
                ChangedLocation();
                session.Run(RefreshAsync);
            }
            else
                Navigate(path);
        }, () => !session.Busy);
    }
    private void ClearSelection()
    {
        inspected = null;
        Inspection = null;
        SelectedPath = null;
    }
    private void SetAllocated(bool value)
    {
        Allocated = value;
        Changed(nameof(ShowLogical));
        Changed(nameof(ShowAllocated));
        page = 0;
        session.Run(RefreshAsync);
    }
    private void ChangedLocation()
    {
        Changed(nameof(BrowseLabel));
        Changed(nameof(BrowseCaption));
        var root = session.CurrentRoot;
        Breadcrumbs.Clear();
        if (root == null)
            return;
        var normalizedRoot = Paths.Normalize(root);
        var steps = new List<string> { normalizedRoot };
        if (parent != null && Paths.Within(parent, normalizedRoot))
        {
            var relative = Path.GetRelativePath(normalizedRoot, parent);
            if (relative != ".")
            {
                var current = normalizedRoot;
                foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
                {
                    current = Path.Combine(current, part);
                    steps.Add(current);
                }
            }
        }
        for (var index = 0; index < steps.Count; index++)
        {
            var name = index == 0 ? RootName(steps[0]) : Path.GetFileName(steps[index]);
            Breadcrumbs.Add(new(name, steps[index], index == steps.Count - 1));
        }
    }
    private static string RootName(string root)
    {
        var name = Path.GetFileName(root.TrimEnd('\\'));
        return name.Length == 0 ? root.TrimEnd('\\') : name;
    }
    public void Reset()
    {
        parent = null;
        page = 0;
        inspected = null;
        Inspection = null;
        SelectedPath = null;
        ChangedLocation();
    }
    public void HistoryChanged()
    {
    }
    public void Navigate(string path)
    {
        if (session.Busy)
            return;
        if (!session.SnapshotRoots.Contains(path, StringComparer.OrdinalIgnoreCase) && !session.Store.IsDirectory(session.Active, path))
            return;
        parent = session.SnapshotRoots.Contains(path, StringComparer.OrdinalIgnoreCase) ? null : path;
        page = 0;
        ClearSelection();
        ChangedLocation();
        session.Run(RefreshAsync);
    }
    /// <summary>Applies a name or path search from the title bar.</summary>
    public void ApplySearch(string text)
    {
        Search = text;
        Changed(nameof(Search));
        List = ExplorerList.Files;
        page = 0;
        session.Run(RefreshAsync);
    }
    private object? inspected;
    /// <summary>Rebuilds the inspector after a color-mode change so its swatch matches the map.</summary>
    private void Reinspect()
    {
        var selected = SelectedPath;
        if (inspected is MapNode node)
            Inspect(node);
        else if (inspected is FileEntry entry)
            Inspect(entry);
        SelectedPath = selected;
    }
    /// <summary>Shows a space-map tile in the inspector.</summary>
    public void Inspect(MapNode node)
    {
        inspected = node;
        SelectedPath = node.Path;
        if (!node.IsDirectory && session.Store.Entry(session.Active, node.Path) is { } entry)
        {
            Inspect(entry);
            return;
        }
        var share = RootBytes > 0 ? 100d * node.Bytes / RootBytes : 0;
        var dominant = node.Composition.Dominant;
        var dominantBytes = node.Composition.Categories[(int)dominant];
        var mostly = node.Composition.Categories.Sum() > 0 ? $"Mostly {FileCategories.Name(dominant).ToLowerInvariant()} ({100d * dominantBytes / Math.Max(1, node.Composition.Categories.Sum()):0}%)" : "";
        Inspection = new(node.Path, node.Name, $"Folder · {node.Files:N0} files", Swatch(node.Composition), Format.Bytes(node.Bytes), share, $"{share:0.#}% of this root", Path.GetDirectoryName(node.Path) ?? "", "", "", "", "", mostly, true);
    }
    /// <summary>Shows a file or folder entry in the inspector.</summary>
    public void Inspect(FileEntry entry)
    {
        inspected = entry;
        SelectedPath = entry.Path;
        var share = RootBytes > 0 ? 100d * entry.Length / RootBytes : 0;
        var composition = Composition.ForFile(entry, referenceTicks);
        var kind = entry.IsDirectory ? "Folder" : $"{FileCategories.Name(FileCategories.Of(entry.Extension))}{(entry.Extension.Length > 0 ? " · " + entry.Extension : "")}";
        string note;
        if (entry.Allocated is not long allocated)
            note = "Windows did not report allocation for this file.";
        else if ((entry.Attributes & 0x200u) != 0 && allocated < entry.Length)
            note = $"Sparse file. Windows reports {Format.Bytes(allocated)} allocated, {Format.Bytes(entry.Length - allocated)} less than its logical size.";
        else if ((entry.Attributes & 0x800u) != 0 && allocated < entry.Length)
            note = $"Compressed file. It uses {Format.Bytes(allocated)} on disk.";
        else if (entry.IsPlaceholder)
            note = "Cloud placeholder. FileViz never opens or hydrates it.";
        else
            note = "";
        Inspection = new(entry.Path, entry.Name, kind, Swatch(composition), entry.SizeText, share, $"{share:0.#}% of this root", entry.Parent, entry.AllocatedText, entry.ModifiedText, AttributeNames(entry.Attributes), entry.Identity ?? "Unknown", note, entry.IsDirectory);
    }
    private long RootBytes => rootComposition?.Categories.Sum() is long sum && sum > 0 ? sum : session.Store.GetSummary(session.Active, session.CurrentRoot).Logical;
    private Brush Swatch(Composition composition) => Application.Current.TryFindResource(Treemap.Palette(composition, ColorByAge).Fill) as Brush ?? Brushes.Gray;
    private static string AttributeNames(uint attributes)
    {
        (uint Flag, string Name)[] names = [(1, "Read-only"), (2, "Hidden"), (4, "System"), (32, "Archive"), (256, "Temporary"), (512, "Sparse"), (1024, "Reparse point"), (2048, "Compressed"), (4096, "Offline"), (8192, "Not indexed"), (16384, "Encrypted"), (0x40000, "Recall on open"), (0x400000, "Recall on access")];
        var result = names.Where(x => (attributes & x.Flag) != 0).Select(x => x.Name).ToArray();
        return result.Length == 0 ? "None" : string.Join(" · ", result);
    }
    private void BuildSegments()
    {
        var result = new List<BarSegment>();
        var legendItems = new List<LegendItem>();
        if (rootComposition is { } composition)
        {
            if (ColorByAge)
                for (var bucket = 0; bucket < AgeBuckets.Count; bucket++)
                    Add(AgeBuckets.Name(bucket), composition.Ages[bucket], $"Age{bucket}Brush");
            else
                foreach (var category in Enum.GetValues<FileCategory>().OrderByDescending(x => composition.Categories[(int)x]))
                    Add(FileCategories.Name(category), composition.Categories[(int)category], Treemap.CategoryKey(category));
        }
        if (volume.Volume && volume.Unattributed > 0)
        {
            result.Add(new("Not attributed to files", volume.Unattributed, "Hatched", Format.Bytes(volume.Unattributed)));
            legendItems.Add(new(Application.Current.TryFindResource("ControlStrongStrokeColorDefaultBrush") as Brush ?? Brushes.Gray, "Not attributed", Format.Bytes(volume.Unattributed)));
        }
        Segments = result;
        Legend = legendItems;
        BarTotal = volume.Volume ? volume.Total : result.Sum(x => x.Bytes);

        void Add(string label, long bytes, string key)
        {
            if (bytes <= 0)
                return;
            result.Add(new(label, bytes, key, Format.Bytes(bytes)));
            legendItems.Add(new(Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray, label, Format.Bytes(bytes)));
        }
    }
    private List<FileEntry> rawFiles = [];
    private void RebuildFileRows()
    {
        var largest = Math.Max(1, rawFiles.Count == 0 ? 1 : rawFiles.Max(x => x.Length));
        SessionViewModel.Replace(Files, rawFiles.Select(x => new FileRow(x, Swatch(Composition.ForFile(x, referenceTicks)), Math.Max(2, 48d * x.Length / largest))));
    }
    public async Task RefreshAsync()
    {
        var id = session.Active;
        var revision = Interlocked.Increment(ref refreshRevision);
        if (id == 0)
            return;
        var database = session.DatabasePath;
        var root = session.CurrentRoot;
        var selectedParent = parent;
        var selectedPage = page;
        if (!double.TryParse(MinimumMiB, out var minimum) || minimum < 0 || minimum > long.MaxValue / 1048576d)
            throw new ArgumentException("Minimum size must be a nonnegative number of MiB.");
        var filter = new QueryFilter(Search, Extension, (long)(minimum * 1048576), ModifiedAfter: ModifiedAfter?.ToUniversalTime().Ticks, RequiredAttributes: HiddenOnly ? 2u : 0u, Parent: selectedParent, Allocated: Allocated, Root: root);
        var allocated = Allocated;
        var result = await Task.Run(() =>
        {
            using var reader = new IndexStore(database);
            var items = reader.Query(id, filter, selectedPage, filesOnly: selectedParent == null);
            var folders = reader.LargestFolders(id, allocated, root: root);
            var extensions = reader.Extensions(id, root);
            var totals = reader.GetSummary(id, root);
            var reference = reader.StartedTicks(id);
            var composition = root == null ? null : reader.FolderComposition(id, Paths.Normalize(root));
            (long Total, long Free, long Unattributed, bool Volume) drive = (0, 0, 0, false);
            string title = root == null ? "" : RootName(Paths.Normalize(root)), caption = "", fileSystem = "";
            if (root != null && !root.StartsWith(@"\\", StringComparison.Ordinal) && Paths.Normalize(Native.VolumeRoot(root)) == Paths.Normalize(root))
            {
                try
                {
                    var info = new DriveInfo(root);
                    var used = info.TotalSize - info.TotalFreeSpace;
                    drive = (info.TotalSize, info.TotalFreeSpace, Math.Max(0, used - totals.Allocated), true);
                    fileSystem = info.DriveFormat;
                    title = $"{(string.IsNullOrWhiteSpace(info.VolumeLabel) ? "Local Disk" : info.VolumeLabel)} ({root.TrimEnd('\\')})";
                    caption = $"{Format.Bytes(used)} used · {Format.Bytes(info.TotalFreeSpace)} free of {Format.Bytes(info.TotalSize)} · {fileSystem}";
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            if (caption.Length == 0)
                caption = root == null ? "" : $"{Paths.Normalize(root)} · {Format.Bytes(totals.Logical)}";
            var mapParent = selectedParent ?? root;
            var map = mapParent == null ? [] : reader.SpaceMap(id, mapParent, allocated);
            MapNode? here = null;
            if (mapParent != null)
            {
                var normalized = Paths.Normalize(mapParent);
                var size = selectedParent == null ? new Breakdown(normalized, allocated ? totals.Allocated : totals.Logical, totals.Files) : reader.LargestFolders(id, allocated, Path.GetDirectoryName(normalized)).FirstOrDefault(x => x.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
                if (size != null)
                    here = new(normalized, RootName(normalized), true, size.Bytes, size.Count, reader.FolderComposition(id, normalized) ?? Composition.Empty, []);
            }
            var location = here == null ? "" : $"{Format.Bytes(here.Bytes)} · {here.Files:N0} files";
            return (items, folders, extensions, totals, map, reference, composition, drive, title, caption, location, here);
        });
        if (session.Active != id || revision != Volatile.Read(ref refreshRevision) || parent != selectedParent || session.CurrentRoot != root || page != selectedPage)
            return;
        referenceTicks = result.reference;
        rootComposition = result.composition;
        volume = result.drive;
        rawFiles = result.items;
        RebuildFileRows();
        SessionViewModel.Replace(Folders, result.folders);
        SessionViewModel.Replace(ExtensionsBreakdown, result.extensions);
        MapItems = result.map;
        BuildSegments();
        Changed(nameof(PageLabel));
        ChangedLocation();
        var total = result.totals;
        VolumeTitle = result.title;
        VolumeCaption = result.caption;
        LocationText = result.location;
        // With nothing selected, the inspector describes the folder being shown.
        if (result.here != null && (Inspection == null || Inspection.IsDirectory && SelectedPath == null))
        {
            Inspect(result.here);
            SelectedPath = null;
        }
        FilesText = total.Files.ToString("N0");
        LogicalText = Format.Bytes(total.Logical);
        AllocatedText = Format.Bytes(total.Allocated);
        DiagnosticsText = total.Errors.ToString("N0");
        SummaryText = $"{total.Files:N0} files · {Format.Bytes(total.Logical)} logical · {Format.Bytes(total.Allocated)} reported allocation";
        CoverageText = result.composition == null
            ? "This snapshot predates type and age totals. Rescan to color the space map and usage bar."
            : $"{session.SelectedSnapshot?.Value.State ?? "Scanning"} · {total.UnknownAllocations:N0} files with unknown allocation · hard links counted once · filesystem overhead shown separately";
    }
    private async Task ExportAsync()
    {
        var id = session.Active;
        var dialog = new SaveFileDialog { Title = "Export snapshot", Filter = "CSV report|*.csv|JSON report|*.json", FileName = $"fileviz-{id}-{DateTime.Now:yyyyMMdd-HHmmss}", AddExtension = true };
        if (dialog.ShowDialog() != true)
            return;
        var database = session.DatabasePath;
        await Task.Run(() => { using var reader = new IndexStore(database); reader.Export(id, dialog.FileName, dialog.FilterIndex == 2); });
        session.Status = "Snapshot exported: " + dialog.FileName;
    }
}
