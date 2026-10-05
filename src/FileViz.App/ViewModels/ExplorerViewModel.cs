using System.Collections.ObjectModel;
using System.Windows.Input;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using Microsoft.Win32;
namespace FileViz.App.ViewModels;

public enum ExplorerList
{
    Files, Folders, Types
}

/// <summary>Space map, largest files, folders and file types for the selected snapshot and root.</summary>
public sealed class ExplorerViewModel : Bindable, ISnapshotSection
{
    private readonly SessionViewModel session;
    private int page; private string? parent; private long refreshRevision;
    public SessionViewModel Session => session;
    public CleanupViewModel Cleanup
    {
        get;
    }
    public ObservableCollection<FileEntry> Files { get; } = [];
    public ObservableCollection<Breakdown> Folders { get; } = [];
    public ObservableCollection<Breakdown> ExtensionsBreakdown { get; } = [];
    private IReadOnlyList<Breakdown> mapItems = []; public IReadOnlyList<Breakdown> MapItems
    {
        get => mapItems; private set => Set(ref mapItems, value);
    }
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
    private string summary = "No snapshot selected"; public string SummaryText
    {
        get => summary; private set => Set(ref summary, value);
    }
    private string coverage = "Analysis stays on this computer. Cleanup always requires review."; public string CoverageText
    {
        get => coverage; private set => Set(ref coverage, value);
    }
    public string Search { get; set; } = ""; public string Extension { get; set; } = ""; public string MinimumMiB { get; set; } = "0";
    public DateTime? ModifiedAfter
    {
        get; set;
    }
    public bool Allocated { get; set; } = false; public bool HiddenOnly { get; set; } = false;
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
    public ExplorerViewModel(SessionViewModel session, CleanupViewModel cleanup)
    {
        this.session = session;
        Cleanup = cleanup;
        FilterCommand = new ActionCommand(() => { page = 0; session.Run(RefreshAsync); }, () => session.HasSnapshot && !session.Busy);
        LargestCommand = new ActionCommand(() => { parent = null; page = 0; ChangedLocation(); session.Run(RefreshAsync); }, () => session.HasSnapshot && !session.Busy);
        UpCommand = new ActionCommand(() =>
        {
            if (parent != null)
            {
                var above = Path.GetDirectoryName(parent);
                parent = above != null && session.CurrentRoot != null && Paths.Within(above, session.CurrentRoot) ? above : null;
            }
            page = 0;
            ChangedLocation();
            session.Run(RefreshAsync);
        }, () => session.HasSnapshot && !session.Busy);
        PreviousCommand = new ActionCommand(() => { page = Math.Max(0, page - 1); session.Run(RefreshAsync); }, () => page > 0 && !session.Busy);
        NextCommand = new ActionCommand(() => { page++; session.Run(RefreshAsync); }, () => Files.Count == 250 && !session.Busy);
        ExportCommand = new ActionCommand(() => session.Run(ExportAsync), () => session.HasSnapshot && !session.Busy);
    }
    private void ChangedLocation()
    {
        Changed(nameof(BrowseLabel));
        Changed(nameof(BrowseCaption));
    }
    public void Reset()
    {
        parent = null;
        page = 0;
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
        parent = path;
        page = 0;
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
            string? overhead = null;
            if (root != null && !root.StartsWith(@"\\", StringComparison.Ordinal) && Paths.Normalize(Native.VolumeRoot(root)) == Paths.Normalize(root))
            {
                try
                {
                    var drive = new DriveInfo(root);
                    var used = drive.TotalSize - drive.TotalFreeSpace;
                    overhead = $"{Format.Bytes(Math.Max(0, used - totals.Allocated))} unaccounted/excluded vs current drive usage (metadata, named streams, sharing and coverage gaps included).";
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            var mapParent = selectedParent ?? root;
            var map = mapParent == null ? new List<Breakdown>() : reader.LargestFolders(id, allocated, mapParent);
            if (mapParent != null)
                map.AddRange(reader.Query(id, new(Parent: mapParent, Allocated: allocated), 0, 100, filesOnly: true).Select(x => new Breakdown(x.Path, allocated ? x.Allocated ?? 0 : x.Length, 1)));
            return (items, folders, extensions, totals, map, overhead);
        });
        if (session.Active != id || revision != Volatile.Read(ref refreshRevision) || parent != selectedParent || session.CurrentRoot != root || page != selectedPage)
            return;
        SessionViewModel.Replace(Files, result.items);
        SessionViewModel.Replace(Folders, result.folders);
        SessionViewModel.Replace(ExtensionsBreakdown, result.extensions);
        MapItems = result.map;
        Changed(nameof(PageLabel));
        ChangedLocation();
        var total = result.totals;
        SummaryText = $"{total.Files:N0} files · {Format.Bytes(total.Logical)} logical · {Format.Bytes(total.Allocated)} reported allocation";
        CoverageText = $"{session.SelectedSnapshot?.Value.State ?? "Scanning"} · {total.Errors:N0} diagnostics · {total.UnknownAllocations:N0} files with unknown allocation. Hard links share allocation; filesystem overhead is separate. {result.overhead}";
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
