using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using FileViz.App.Services;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using Microsoft.Win32;
namespace FileViz.App.ViewModels;

public sealed class ShellViewModel : Bindable, IDisposable
{
    public string DatabasePath
    {
        get;
    }
    private readonly IndexStore store; private CancellationTokenSource? cancellation; private TaskCompletionSource? pause; private int page, duplicatePage; private long active;
    public ObservableCollection<DuplicateScopeRow> DuplicateRoots { get; } = [];
    public ObservableCollection<DriveRow> Drives { get; } = [];
    public ObservableCollection<SnapshotRow> History { get; } = [];
    public ObservableCollection<string> SnapshotRoots { get; } = [];
    public ObservableCollection<ScanProfile> Profiles { get; } = [];
    public ObservableCollection<FileEntry> Files { get; } = [];
    public ObservableCollection<Breakdown> Folders { get; } = [];
    public ObservableCollection<Breakdown> ExtensionsBreakdown { get; } = [];
    public ObservableCollection<DuplicateRow> Duplicates { get; } = [];
    public ObservableCollection<Difference> Differences { get; } = [];
    public ObservableCollection<ScanError> Errors { get; } = [];
    public ObservableCollection<CleanupRecord> CleanupHistory { get; } = [];
    private IReadOnlyList<Breakdown> mapItems = []; public IReadOnlyList<Breakdown> MapItems
    {
        get => mapItems; private set => Set(ref mapItems, value);
    }
    public string[] Algorithms { get; } = ["SHA-256", "SHA-1", "MD5", "Name"];
    private string status = "Choose a drive or folder, then scan."; public string Status
    {
        get => status; set => Set(ref status, value);
    }
    private string summary = "Your drives, at a glance"; public string SummaryText
    {
        get => summary; private set => Set(ref summary, value);
    }
    private string coverage = "Analysis stays on this computer. Cleanup always requires review."; public string CoverageText
    {
        get => coverage; private set => Set(ref coverage, value);
    }
    private string duplicateText = "Choose content matching for verified groups. Names alone are candidates."; public string DuplicateText
    {
        get => duplicateText; private set => Set(ref duplicateText, value);
    }
    private long refreshRevision; private bool scanning; private bool busy; public bool Busy
    {
        get => busy; private set
        {
            if (Set(ref busy, value))
            {
                Changed(nameof(CanInteract));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
    public bool CanInteract => !Busy; public string PauseLabel => pause == null ? "Pause" : "Resume"; public string PageLabel => $"Page {page + 1} · up to 250 rows";
    public string ExtraRoots { get; set; } = ""; public string Exclusions { get; set; } = ".FileViz-Quarantine"; public bool Administrator { get; set; } = false; public bool PreferMft { get; set; } = true;
    public string ProfileName { get; set; } = "My scan"; public string Search { get; set; } = ""; public string Extension { get; set; } = ""; public string MinimumMiB { get; set; } = "0";
    public DateTime? ModifiedAfter
    {
        get; set;
    }
    public bool Allocated { get; set; } = false; public bool HiddenOnly { get; set; } = false; public string Algorithm { get; set; } = "SHA-256"; public bool CrossDrive { get; set; } = false; public string PreferredFolder { get; set; } = "";
    private string? parent; public string BrowseLabel => parent ?? "Largest files in the current root";
    private string? currentRoot; public string? CurrentRoot
    {
        get => currentRoot; set
        {
            if (Set(ref currentRoot, value))
            {
                parent = null;
                page = 0;
                Changed(nameof(BrowseLabel));
                Run(RefreshAsync);
            }
        }
    }
    private bool refreshingHistory;
    private SnapshotRow? selectedSnapshot; public SnapshotRow? SelectedSnapshot
    {
        get => selectedSnapshot; set
        {
            if (refreshingHistory || !Set(ref selectedSnapshot, value))
                return;
            active = value?.Value.Id ?? 0;
            page = 0;
            duplicatePage = 0;
            parent = null;
            SnapshotRoots.Clear();
            if (value != null)
                foreach (var root in JsonSerializer.Deserialize<string[]>(value.Value.Roots) ?? [])
                    SnapshotRoots.Add(root);
            currentRoot = SnapshotRoots.FirstOrDefault();
            Changed(nameof(CurrentRoot));
            Changed(nameof(BrowseLabel));
            Run(RefreshAsync);
        }
    }
    public SnapshotRow? CompareBefore
    {
        get; set;
    }
    private ScanProfile? selectedProfile; public ScanProfile? SelectedProfile
    {
        get => selectedProfile; set
        {
            if (Set(ref selectedProfile, value) && value != null)
            {
                foreach (var drive in Drives)
                    drive.Selected = false;
                ExtraRoots = string.Join(Environment.NewLine, value.Roots);
                Exclusions = string.Join(Environment.NewLine, value.Exclusions);
                PreferMft = value.PreferMft;
                ProfileName = value.Name;
                Changed(nameof(ExtraRoots));
                Changed(nameof(Exclusions));
                Changed(nameof(PreferMft));
                Changed(nameof(ProfileName));
            }
        }
    }
    public ICommand ScanCommand
    {
        get;
    }
    public ICommand CancelCommand
    {
        get;
    }
    public ICommand PauseCommand
    {
        get;
    }
    public ICommand AddRootCommand
    {
        get;
    }
    public ICommand SaveProfileCommand
    {
        get;
    }
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
    public ICommand DuplicateCommand
    {
        get;
    }
    public ICommand DuplicatePreviousCommand
    {
        get;
    }
    public ICommand DuplicateNextCommand
    {
        get;
    }
    public ICommand CompareCommand
    {
        get;
    }
    public ICommand ExportCommand
    {
        get;
    }
    public ICommand ElevateCommand
    {
        get;
    }
    public ShellViewModel(string? database = null)
    {
        DatabasePath = database ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileViz", "index.db");
        store = new(DatabasePath);
        store.RecoverInterrupted();
        var cleanup = new CleanupService(store.SaveCleanup);
        foreach (var record in store.CleanupHistory())
            cleanup.Recover(record);
        ScanCommand = new ActionCommand(() => Run(() => ScanAsync()), () => !Busy);
        CancelCommand = new ActionCommand(() => { cancellation?.Cancel(); pause?.TrySetResult(); }, () => Busy);
        PauseCommand = new ActionCommand(() => { if (pause == null) pause = new(TaskCreationOptions.RunContinuationsAsynchronously); else { pause.TrySetResult(); pause = null; } Changed(nameof(PauseLabel)); Status = pause == null ? "Resuming scan…" : "Paused at the next batch boundary."; }, () => Busy && scanning);
        AddRootCommand = new ActionCommand(AddRoot, () => !Busy);
        SaveProfileCommand = new ActionCommand(() => { if (string.IsNullOrWhiteSpace(ProfileName)) throw new ArgumentException("Enter a profile name."); store.SaveProfile(new(ProfileName, Roots(), Excluded(), PreferMft)); Replace(Profiles, store.Profiles()); Status = "Scan profile saved."; }, () => !Busy);
        FilterCommand = new ActionCommand(() => { page = 0; Run(RefreshAsync); }, () => active > 0 && !Busy);
        LargestCommand = new ActionCommand(() => { parent = null; page = 0; Changed(nameof(BrowseLabel)); Run(RefreshAsync); }, () => active > 0 && !Busy);
        UpCommand = new ActionCommand(() => { if (parent != null) { var above = System.IO.Path.GetDirectoryName(parent); parent = above != null && currentRoot != null && Paths.Within(above, currentRoot) ? above : null; } page = 0; Changed(nameof(BrowseLabel)); Run(RefreshAsync); }, () => active > 0 && !Busy);
        PreviousCommand = new ActionCommand(() => { page = Math.Max(0, page - 1); Run(RefreshAsync); }, () => page > 0 && !Busy);
        NextCommand = new ActionCommand(() => { page++; Run(RefreshAsync); }, () => Files.Count == 250 && !Busy);
        DuplicateCommand = new ActionCommand(() => Run(FindDuplicatesAsync), () => active > 0 && !Busy);
        DuplicatePreviousCommand = new ActionCommand(() => { duplicatePage = Math.Max(0, duplicatePage - 1); RefreshDuplicates(); }, () => duplicatePage > 0 && !Busy);
        DuplicateNextCommand = new ActionCommand(() => { duplicatePage++; RefreshDuplicates(); }, () => Duplicates.Count == 250 && !Busy);
        CompareCommand = new ActionCommand(() => Run(CompareAsync), () => active > 0 && CompareBefore != null && !Busy);
        ExportCommand = new ActionCommand(() => Run(ExportAsync), () => active > 0 && !Busy);
        ElevateCommand = new ActionCommand(() =>
        {
            if (Native.IsElevated)
            {
                Status = "FileViz is already running as administrator.";
                return;
            }
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            info.ArgumentList.Add("--roots");
            info.ArgumentList.Add(JsonSerializer.Serialize(Roots()));
            info.ArgumentList.Add("--wait-parent");
            info.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(info);
            Application.Current.Shutdown();
        }, () => !Busy);
    }
    public async Task InitializeAsync()
    {
        var rows = await Task.Run(() =>
        {
            var result = new List<DriveRow>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    var network = drive.DriveType == DriveType.Network;
                    if (network)
                    {
                        result.Add(new(drive.Name, drive.Name + " · Network", "Uses your Windows share credentials"));
                        continue;
                    }
                    if (!drive.IsReady)
                        continue;
                    result.Add(new(drive.Name, drive.Name + " · " + drive.VolumeLabel, $"{drive.DriveFormat} · {Format.Bytes(drive.TotalSize - drive.AvailableFreeSpace)} used · {Format.Bytes(drive.AvailableFreeSpace)} free / {Format.Bytes(drive.TotalSize)}", drive.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase)));
                }
                catch (IOException) { result.Add(new(drive.Name, drive.Name, "Unavailable")); }
                catch (UnauthorizedAccessException) { result.Add(new(drive.Name, drive.Name, "Access denied")); }
            }
            return result;
        });
        Replace(Drives, rows);
        ReloadHistory();
        Replace(Profiles, store.Profiles());
        Replace(CleanupHistory, store.CleanupHistory());
        if (History.Count > 0)
            SelectedSnapshot = History[0];
    }
    public void ChangedRoots() => Changed(nameof(ExtraRoots));
    private void AddRoot()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a drive, folder, or network share" };
        if (dialog.ShowDialog() == true)
        {
            Drives.Add(new(dialog.FolderName, dialog.FolderName, "Selected folder", true));
        }
    }
    private string[] Roots() => Paths.DistinctRoots(Drives.Where(x => x.Selected).Select(x => Native.ResolveNetwork(x.Path)).Concat(Lines(ExtraRoots).Select(Native.ResolveNetwork)));
    private string[] Excluded() => Lines(Exclusions).Append(System.IO.Path.GetDirectoryName(DatabasePath)!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string[] Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public async Task ScanAsync(string[]? rootsOverride = null)
    {
        if (Busy)
            return;
        var roots = rootsOverride ?? Roots();
        if (roots.Length == 0)
        {
            Status = "Select at least one drive or folder.";
            return;
        }
        scanning = true;
        Busy = true;
        cancellation = new();
        var token = cancellation.Token;
        var snapshot = store.CreateSnapshot(roots);
        active = snapshot;
        long count = 0;
        var exclusions = Excluded();
        var state = "Complete";
        var progress = new Progress<string>(text => Status = text);
        Status = "Starting scan…";
        try
        {
            await Task.Run(async () =>
            {
                using var writer = new IndexStore(DatabasePath);
                await using var worker = await WorkerSession.StartAsync(Administrator, token);
                await worker.ExecuteAsync(new("scan", Scopes: roots.Select(x => new ScanScope(x, exclusions, PreferMft)).ToArray()), async message =>
                {
                    if (message.Batch is not { } batch)
                        return;
                    if (pause is { } gate)
                        await gate.Task.WaitAsync(token);
                    writer.AddBatch(snapshot, batch);
                    count += batch.Entries.LongLength;
                    ((IProgress<string>)progress).Report($"{batch.Engine} · {count:N0} entries · {batch.Root}");
                }, token);
                var aliases = new List<string>(64);
                var aliasBytes = 0;
                async Task RefreshAliases()
                {
                    if (aliases.Count == 0)
                        return;
                    await worker.ExecuteAsync(new("metadata", MetadataPaths: aliases.ToArray()), message =>
                    {
                        if (message.Entry is { } entry && !writer.RefreshAlias(snapshot, entry))
                            writer.AddError(snapshot, entry.Path, "Hard-link identity changed during metadata refresh.");
                        if (message.Kind == "metadata-error")
                            writer.AddError(snapshot, message.Path ?? "", message.Text ?? "Hard-link metadata refresh failed.");
                        return Task.CompletedTask;
                    }, token);
                    aliases.Clear();
                    aliasBytes = 0;
                }
                foreach (var path in writer.AliasPaths(snapshot))
                {
                    token.ThrowIfCancellationRequested();
                    if (aliases.Count > 0 && aliasBytes + 6 * path.Length + 1024 > 1024 * 1024)
                        await RefreshAliases();
                    aliases.Add(path);
                    aliasBytes += 6 * path.Length + 1024;
                    if (aliases.Count == 64)
                        await RefreshAliases();
                }
                await RefreshAliases();
            }, token);
        }
        catch (OperationCanceledException) { state = "Cancelled"; Status = "Scan cancelled. Partial results remain available."; }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { state = "Cancelled"; Status = "Administrator request was cancelled."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception) { state = "Failed"; store.AddError(snapshot, string.Join(';', roots), e.Message); Status = "Scan failed: " + e.Message; }
        finally
        {
            pause?.TrySetResult();
            pause = null;
            Changed(nameof(PauseLabel));
            try
            {
                await Task.Run(() => { using var writer = new IndexStore(DatabasePath); writer.Finish(snapshot, state); });
            }
            finally { scanning = false; Busy = false; cancellation.Dispose(); cancellation = null; }
            ReloadHistory();
            SelectedSnapshot = History.First(x => x.Value.Id == snapshot);
            await RefreshAsync();
            if (state == "Complete")
                Status = "Scan complete. Review diagnostics for any coverage gaps.";
        }
    }
    public async Task RefreshAsync()
    {
        var id = active;
        var revision = Interlocked.Increment(ref refreshRevision);
        if (id == 0)
            return;
        var root = currentRoot;
        var selectedParent = parent;
        var selectedPage = page;
        if (!double.TryParse(MinimumMiB, out var minimum) || minimum < 0 || minimum > long.MaxValue / 1048576d)
            throw new ArgumentException("Minimum size must be a nonnegative number of MiB.");
        var filter = new QueryFilter(Search, Extension, (long)(minimum * 1048576), ModifiedAfter: ModifiedAfter?.ToUniversalTime().Ticks, RequiredAttributes: HiddenOnly ? 2u : 0u, Parent: selectedParent, Allocated: Allocated, Root: root);
        var result = await Task.Run(() =>
        {
            using var reader = new IndexStore(DatabasePath);
            var items = reader.Query(id, filter, selectedPage, filesOnly: selectedParent == null);

            var folders = reader.LargestFolders(id, Allocated, root: root);
            var extensions = reader.Extensions(id, root);
            var errors = reader.Errors(id);
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
            var map = mapParent == null ? new List<Breakdown>() : reader.LargestFolders(id, Allocated, mapParent);
            if (mapParent != null)
                map.AddRange(reader.Query(id, new(Parent: mapParent, Allocated: Allocated), 0, 100, filesOnly: true).Select(x => new Breakdown(x.Path, Allocated ? x.Allocated ?? 0 : x.Length, 1)));
            return (items, folders, extensions, errors, totals, map, overhead);
        });
        if (active != id || revision != Volatile.Read(ref refreshRevision) || parent != selectedParent || currentRoot != root || page != selectedPage)
            return;
        Replace(Files, result.items);
        Replace(Folders, result.folders);
        Replace(ExtensionsBreakdown, result.extensions);
        Replace(Errors, result.errors);
        MapItems = result.map;
        Changed(nameof(PageLabel));
        Changed(nameof(BrowseLabel));
        var total = result.totals;
        SummaryText = $"{total.Files:N0} files · {Format.Bytes(total.Logical)} logical · {Format.Bytes(total.Allocated)} reported allocation";
        CoverageText = $"Snapshot #{id} · {SelectedSnapshot?.Value.Started ?? ""} · {SelectedSnapshot?.Value.State ?? "Scanning"} · {total.Errors:N0} diagnostics · {total.UnknownAllocations:N0} files with unknown allocation. Hard links share allocation; filesystem overhead is separate. {result.overhead}";
        RefreshDuplicates();
        Replace(CleanupHistory, store.CleanupHistory());
        CommandManager.InvalidateRequerySuggested();
    }
    public void Navigate(string path)
    {
        if (Busy)
            return;
        if (!SnapshotRoots.Contains(path, StringComparer.OrdinalIgnoreCase) && !store.IsDirectory(active, path))
            return;
        parent = path;
        page = 0;
        Changed(nameof(BrowseLabel));
        Run(RefreshAsync);
    }
    public async Task FindDuplicatesAsync()
    {
        Busy = true;
        cancellation = new();
        var id = active;
        var snapshots = new[] { id };
        var roots = currentRoot == null ? SnapshotRoots.ToArray() : new[] { currentRoot };
        if (CrossDrive)
        {
            var selected = DuplicateRoots.Where(x => x.Selected).ToArray();
            if (selected.Length == 0)
            {
                Busy = false;
                cancellation.Dispose();
                cancellation = null;
                Status = "Select roots for duplicate analysis.";
                return;
            }
            snapshots = selected.Select(x => x.Snapshot).Distinct().ToArray();
            roots = selected.Select(x => x.Root).Distinct().ToArray();
        }
        try
        {
            await Task.Run(() => DuplicateCoordinator.FindAsync(DatabasePath, id, snapshots, roots, Algorithm, PreferredFolder, Administrator, new Progress<string>(text => Application.Current.Dispatcher.Invoke(() => Status = text)), cancellation.Token));
            duplicatePage = 0;
            RefreshDuplicates();
            Status = "Duplicate analysis complete. Cleanup performs fresh byte and stream verification.";
        }
        finally { Busy = false; cancellation.Dispose(); cancellation = null; CommandManager.InvalidateRequerySuggested(); }
    }
    private void RefreshDuplicates()
    {
        if (active == 0)
            return;
        Replace(Duplicates, store.Duplicates(active, duplicatePage));
        DuplicateText = $"Main-stream content candidates · {Format.Bytes(store.DuplicatePotential(active))} potential duplicate logical bytes; allocation and named streams require fresh verification. Suggested keepers are not automatic selections.";
    }
    private async Task CompareAsync()
    {
        if (CompareBefore == null || SelectedSnapshot == null)
            return;
        var beforeRoots = JsonSerializer.Deserialize<string[]>(CompareBefore.Value.Roots) ?? [];
        var afterRoots = SnapshotRoots.ToArray();
        if (!beforeRoots.Order().SequenceEqual(afterRoots.Order(), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Compare snapshots covering the same roots.");
        var before = CompareBefore.Value.Id;
        var after = active;
        Replace(Differences, await Task.Run(() => { using var reader = new IndexStore(DatabasePath); return reader.Compare(before, after); }));
        Status = "Showing up to 250 added, removed, or grown files. Partial snapshots have incomplete coverage.";
    }
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog { Title = "Export snapshot", Filter = "CSV report|*.csv|JSON report|*.json", FileName = $"fileviz-{active}-{DateTime.Now:yyyyMMdd-HHmmss}", AddExtension = true };
        if (dialog.ShowDialog() != true)
            return;
        var id = active;
        await Task.Run(() => { using var reader = new IndexStore(DatabasePath); reader.Export(id, dialog.FileName, dialog.FilterIndex == 2); });
        Status = "Snapshot exported: " + dialog.FileName;
    }
    public async Task CleanupAsync(CleanupSelection[] selections)
    {
        if (Busy || selections.Length == 0)
            return;
        Busy = true;
        cancellation = new();
        try
        {
            var results = await Task.Run(async () => { using var writer = new IndexStore(DatabasePath); var service = new CleanupService(writer.SaveCleanup); var result = new List<CleanupRecord>(); foreach (var selection in selections) { cancellation.Token.ThrowIfCancellationRequested(); var record = await service.QuarantineAsync(selection, cancellation.Token); result.Add(record); if (record.State == "Quarantined") writer.MarkStale(record.Original); } return result; });
            Replace(CleanupHistory, store.CleanupHistory());
            Status = $"Quarantined {results.Count(x => x.State == "Quarantined")} of {results.Count} files. No space reclaimed; review failures in cleanup history.";
        }
        finally { Busy = false; cancellation?.Dispose(); cancellation = null; ReloadHistory(); }
    }
    public FileEntry? KeeperFor(DuplicateRow row, IEnumerable<string> removals) => store.DuplicateGroup(active, row.GroupId).FirstOrDefault(x => !removals.Contains(x.Path, StringComparer.Ordinal));
    public void Restore(CleanupRecord record)
    {
        if (Busy)
            return;
        var result = new CleanupService(store.SaveCleanup).Restore(record);
        Replace(CleanupHistory, store.CleanupHistory());
        Status = result.Error ?? "Restored without overwriting existing files.";
    }
    public void Recycle(CleanupRecord record)
    {
        if (Busy)
            return;
        var result = new CleanupService(store.SaveCleanup).Recycle(record);
        Replace(CleanupHistory, store.CleanupHistory());
        Status = result.Error ?? "Windows completed the Recycle Bin request. Review Windows prompts for the resulting disposal method.";
    }
    private void ReloadHistory()
    {
        var id = active;
        var comparisonId = CompareBefore?.Value.Id;
        refreshingHistory = true;
        try { Replace(History, store.Snapshots().Select(x => new SnapshotRow(x))); }
        finally { refreshingHistory = false; }
        selectedSnapshot = History.FirstOrDefault(x => x.Value.Id == id);
        Changed(nameof(SelectedSnapshot));
        CompareBefore = History.FirstOrDefault(x => x.Value.Id == comparisonId) ?? History.Skip(1).FirstOrDefault();
        Changed(nameof(CompareBefore));
        var choices = History.SelectMany(x => (JsonSerializer.Deserialize<string[]>(x.Value.Roots) ?? []).Select(root => new { x.Value.Id, Root = root })).GroupBy(x => x.Root, StringComparer.OrdinalIgnoreCase).Select(x => x.OrderByDescending(y => y.Id).First()).ToArray();
        var selectedRoots = DuplicateRoots.Where(x => x.Selected).Select(x => x.Root).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Replace(DuplicateRoots, choices.Select(x => new DuplicateScopeRow(x.Id, x.Root, selectedRoots.Contains(x.Root) || x.Root == currentRoot)));
    }
    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
            collection.Add(value);
    }
    public async void Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) { Status = "Operation cancelled."; }
        catch (Exception e) when (e is not OutOfMemoryException) { Status = e.Message; }
    }
    public void Dispose()
    {
        cancellation?.Cancel();
        pause?.TrySetResult();
        store.Dispose();
    }
}
