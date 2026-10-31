using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FileViz.App.Services;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;

namespace FileViz.App.ViewModels;

public sealed class PhotoRow(PhotosViewModel owner, PhotoInfo photo) : Bindable
{
    public PhotoInfo Photo { get; } = photo;
    public string Path => Photo.Entry.Path;
    public string Name => Photo.Entry.Name;
    public string Resolution => Photo.Resolution;
    public string Size => Photo.Entry.SizeText;
    public string Note => Photo.Error ?? (Photo.Frames > 1 ? $"{Photo.Frames} frames · largest dimensions" : "");
    public bool CanRemove => CanRemovePhoto(Photo);
    internal static bool CanRemovePhoto(PhotoInfo photo) => photo.Error == null && photo.Entry.Identity != null && !photo.Entry.IsPlaceholder && !photo.Entry.IsReparse && !CleanupService.Protected(photo.Entry.Path);
    private bool remove;
    public bool Remove
    {
        get => remove;
        set
        {
            if (owner.Session.Busy || (value && !CanRemove)) return;
            if (value && !owner.CanSelect(Path)) return;
            if (Set(ref remove, value)) owner.RemovalChanged(this);
        }
    }
    internal void RestoreSelection(bool value) { remove = value; Changed(nameof(Remove)); }
}

/// <summary>Explicit image review; dimensions are indexed on demand, never during a drive scan.</summary>
public sealed class PhotosViewModel : Bindable, ISnapshotSection, IDisposable
{
    public SessionViewModel Session { get; }
    public CleanupViewModel Cleanup { get; }
    public ObservableCollection<DuplicateScopeRow> AvailableScopes { get; } = [];
    public ObservableCollection<PhotoRow> Photos { get; } = [];
    private readonly Dictionary<string, PhotoInfo> removals = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch elapsed = new();
    private readonly Stopwatch heartbeat = new();
    private CancellationTokenSource? previewCancellation;
    private PhotoRow? previewRow;
    private Task previewTask = Task.CompletedTask;
    private int? eligibleMatches;
    private long revision;
    private int page;
    private bool syncing;
    private DuplicateScopeRow? selectedScope;
    public DuplicateScopeRow? SelectedScope
    {
        get => selectedScope;
        set
        {
            if (syncing || value == null) return;
            if (Session.Busy) { Changed(nameof(SelectedScope)); return; }
            var snapshot = Session.History.FirstOrDefault(x => x.Value.Id == value.Snapshot);
            if (snapshot == null || !Set(ref selectedScope, value)) return;
            syncing = true;
            try { Session.SelectedSnapshot = snapshot; Session.CurrentRoot = value.Root; }
            finally { syncing = false; }
            SynchronizeScope();
        }
    }
    public string ScopeDescription => SelectedScope is { } scope ? $"Saved scan #{scope.Snapshot} · {scope.State}. Scan another drive or folder in Home to add it here." : "Scan a drive or folder in Home first.";
    public string MinimumShortEdge { get; set; } = "720";
    public string MinimumMegapixels { get; set; } = "0";
    public string Search { get; set; } = "";
    private bool errorsOnly;
    public bool ErrorsOnly { get => errorsOnly; set { if (Session.Busy) return; if (Set(ref errorsOnly, value)) ApplyFilter(); } }
    private PhotoFilter filter = new();
    private bool analyzing;
    public bool IsAnalyzing => analyzing;
    private bool hasActivity;
    public bool HasActivity => hasActivity;
    private PhotoProgress activity = new("Ready", 0, 0, 0);
    public string ActivityPhase => activity.Phase;
    public string ActivityPath => activity.Path;
    public string ActivityText => $"{activity.Done:N0} / {activity.Total:N0} images · {activity.Errors:N0} unreadable · elapsed {Format.Elapsed(elapsed.Elapsed)}";
    public string ActivityHeartbeat => !IsAnalyzing ? "" : heartbeat.Elapsed.TotalSeconds < 3 ? "Receiving image updates" : $"Waiting for an image read · last update {Format.Elapsed(heartbeat.Elapsed)} ago";
    public bool IsIndeterminate => activity.Total == 0;
    public double Percent => activity.Total > 0 ? 100d * activity.Done / activity.Total : 0;
    private string summary = "Analyze image dimensions to find photos below your thresholds.";
    public string Summary { get => summary; private set => Set(ref summary, value); }
    private long matches;
    public string PageLabel => $"Page {page + 1} · {matches:N0} matches · up to 100 rows";
    public bool HasSelection => removals.Count > 0 && !Session.Busy;
    public bool? AllMatchesSelected => removals.Count == 0 ? false : removals.Count == (eligibleMatches ?? matches) ? true : null;
    public string SelectionText => $"{Format.Count(removals.Count, "photo")} selected · {Format.Bytes(removals.Values.Sum(x => x.Entry.Length))}";
    public string SelectionNote => removals.Values.Where(x => x.Entry.Identity != null).GroupBy(x => x.Entry.Identity).Any(x => x.Skip(1).Any())
        ? "Selection includes hard-link aliases; removing an alias may release no allocation."
        : "Low resolution does not mean unwanted. Review the originals before moving them.";
    private PhotoRow? selected;
    public PhotoRow? Selected
    {
        get => selected;
        set
        {
            if (Set(ref selected, value))
            {
                ClearPreview();
                Changed(nameof(SelectedPath));
                Changed(nameof(SelectedDetails));
                CommandManager.InvalidateRequerySuggested();
                if (value != null && !Session.Busy) _ = LoadPreviewAsync();
            }
        }
    }
    public string SelectedPath => Selected?.Path ?? "Select a photo to inspect it.";
    public string SelectedDetails => Selected == null ? "" : $"{Selected.Resolution} · {Selected.Size}\n{Selected.Note}";
    private BitmapSource? preview;
    public BitmapSource? Preview { get => preview; private set => Set(ref preview, value); }
    private string previewNote = "Select a photo to preview it automatically.";
    public string PreviewNote { get => previewNote; private set => Set(ref previewNote, value); }
    private bool previewing;
    public bool Previewing { get => previewing; private set { Set(ref previewing, value); CommandManager.InvalidateRequerySuggested(); } }
    internal Task PreviewCompletion => previewTask;
    public ICommand AnalyzeCommand { get; }
    public ICommand FilterCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand SelectPageCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand SelectAllCommand { get; }
    public PhotosViewModel(SessionViewModel session, CleanupViewModel cleanup)
    {
        Session = session; Cleanup = cleanup;
        AnalyzeCommand = new ActionCommand(() => session.Run(AnalyzeAsync), () => session.HasSnapshot && SelectedScope != null && !session.Busy);
        FilterCommand = new ActionCommand(ApplyFilter, () => session.HasSnapshot && !session.Busy);
        PreviousCommand = new ActionCommand(() => { page--; session.Run(RefreshAsync); }, () => page > 0 && !session.Busy);
        NextCommand = new ActionCommand(() => { page++; session.Run(RefreshAsync); }, () => (long)(page + 1) * 100 < matches && !session.Busy);
        SelectPageCommand = new ActionCommand(() => { foreach (var row in Photos.Where(x => x.CanRemove)) row.Remove = true; }, () => !session.Busy && !ErrorsOnly && Photos.Count > 0);
        ClearCommand = new ActionCommand(ClearSelection, () => removals.Count > 0 && !session.Busy);
        SelectAllCommand = new ActionCommand(() => session.Run(ToggleAllMatchesAsync), () => session.HasSnapshot && !session.Busy && !ErrorsOnly && matches > 0);
        clock.Tick += (_, _) => { Changed(nameof(ActivityText)); Changed(nameof(ActivityHeartbeat)); };
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionViewModel.SelectedSnapshot) or nameof(SessionViewModel.CurrentRoot)) SynchronizeScope();
            if (e.PropertyName == nameof(SessionViewModel.Busy))
            {
                Changed(nameof(HasSelection));
                if (session.Busy) ClearPreview();
                else if (Selected != null) _ = LoadPreviewAsync();
            }
        };
    }
    public void ApplyFilter()
    {
        Session.Run(async () =>
        {
            var candidate = ParseFilter(); filter = candidate; page = 0; ClearSelection();
            await RefreshAsync();
        });
    }
    private PhotoFilter ParseFilter()
    {
        if (!int.TryParse(MinimumShortEdge, out var edge) || !double.TryParse(MinimumMegapixels, out var megapixels)) throw new ArgumentException("Enter numeric pixel and megapixel thresholds. Zero disables a threshold.");
        var result = new PhotoFilter(edge, megapixels, Search.Trim(), ErrorsOnly); result.Validate(); return result;
    }
    public void Reset()
    {
        Interlocked.Increment(ref revision); page = 0; Photos.Clear(); Selected = null; ClearSelection();
        Summary = "Analyze image dimensions or review the cached results for this saved inventory.";
        if (!IsAnalyzing) { hasActivity = false; Changed(nameof(HasActivity)); }
    }
    public void HistoryChanged()
    {
        syncing = true;
        try
        {
            var scopes = Session.History.SelectMany(x => x.Roots.Select(root => new DuplicateScopeRow(x.Value.Id, root, false, x.Value.State)))
                .GroupBy(x => x.Root, StringComparer.OrdinalIgnoreCase).Select(x => x.OrderByDescending(y => y.Snapshot).First()).OrderBy(x => x.Root, StringComparer.OrdinalIgnoreCase);
            SessionViewModel.Replace(AvailableScopes, scopes);
        }
        finally { syncing = false; }
        SynchronizeScope();
    }
    private void SynchronizeScope()
    {
        if (syncing) return;
        syncing = true;
        try
        {
            selectedScope = AvailableScopes.FirstOrDefault(x => x.Snapshot == Session.Active && x.Root.Equals(Session.CurrentRoot, StringComparison.OrdinalIgnoreCase));
            if (selectedScope == null && Session.SelectedSnapshot is { } snapshot && Session.CurrentRoot is { } root)
            {
                selectedScope = new(Session.Active, root, false, snapshot.Value.State); AvailableScopes.Add(selectedScope);
            }
            Changed(nameof(SelectedScope)); Changed(nameof(ScopeDescription)); CommandManager.InvalidateRequerySuggested();
        }
        finally { syncing = false; }
    }
    public async Task RefreshAsync()
    {
        var id = Session.Active; var root = Session.CurrentRoot; if (id == 0 || root == null) return;
        var current = Interlocked.Increment(ref revision); var query = filter; var selectedPage = page; var database = Session.DatabasePath;
        var result = await Task.Run(() => { using var store = new IndexStore(database); return (store.QueryPhotos(id, root, query, selectedPage), store.PhotoSummary(id, root, query), store.SavedPhotoCandidateCount(id, root)); });
        if (current != revision || Session.Active != id || Session.CurrentRoot != root) return;
        Selected = null;
        SessionViewModel.Replace(Photos, result.Item1.Select(info => { var row = new PhotoRow(this, info); row.RestoreSelection(removals.ContainsKey(row.Path)); return row; }));
        matches = result.Item2.Matches;
        Summary = new PhotoCoverage(result.Item3, result.Item2.Inspected, result.Item2.Errors, matches).Description + " Cached results may be stale; cleanup rechecks file identity and timestamps.";
        Changed(nameof(AllMatchesSelected));
        Changed(nameof(PageLabel)); CommandManager.InvalidateRequerySuggested();
    }
    public async Task AnalyzeAsync()
    {
        if (Session.Busy || !Session.HasSnapshot || Session.CurrentRoot == null) return;
        filter = ParseFilter(); ClearSelection(); ClearPreview();
        var snapshot = Session.Active; var root = Session.CurrentRoot; var database = Session.DatabasePath; var elevate = Session.Administrator;
        var token = Session.BeginWork(); analyzing = true; hasActivity = true; activity = new("Preparing image analysis", 0, 0, 0);
        elapsed.Restart(); heartbeat.Restart(); clock.Start(); ChangedActivity();
        try
        {
            var progress = new Progress<PhotoProgress>(update => { if (!IsAnalyzing) return; activity = update; heartbeat.Restart(); ChangedActivity(); Session.Status = "Photos · " + update.Phase; });
            await Task.Run(async () => { await using var coordinator = new PhotoCoordinator(elevate); await coordinator.AnalyzeAsync(database, snapshot, root, progress, token); });
            activity = activity with { Phase = "Photo analysis complete", Path = "" };
            Session.Status = "Photo dimensions ready. Review selections before quarantine.";
        }
        catch (OperationCanceledException) { activity = activity with { Phase = "Photo analysis cancelled · completed metadata retained", Path = "" }; Session.Status = activity.Phase; }
        catch (Exception e) when (e is not OutOfMemoryException) { activity = activity with { Phase = "Photo analysis failed · " + e.Message, Path = "" }; Session.Status = activity.Phase; }
        finally
        {
            analyzing = false; elapsed.Stop(); clock.Stop(); Session.EndWork(); ChangedActivity(); page = 0;
            await RefreshAsync();
        }
    }
    private void ChangedActivity()
    {
        foreach (var name in new[] { nameof(IsAnalyzing), nameof(HasActivity), nameof(ActivityPhase), nameof(ActivityPath), nameof(ActivityText), nameof(ActivityHeartbeat), nameof(IsIndeterminate), nameof(Percent) }) Changed(name);
    }
    private void ClearPreview()
    {
        previewCancellation?.Cancel(); previewCancellation = null; previewRow = null;
        Preview = null; Previewing = false; PreviewNote = "Select a photo to preview it automatically.";
    }
    public Task LoadPreviewAsync()
    {
        if (Selected is not { } row || Session.Busy) return Task.CompletedTask;
        if (row.Photo.Error != null) { PreviewNote = row.Photo.Error; return Task.CompletedTask; }
        if (previewRow == row && previewCancellation is { IsCancellationRequested: false }) return previewTask;
        ClearPreview();
        var cancellation = new CancellationTokenSource(); previewCancellation = cancellation; previewRow = row;
        Previewing = true; PreviewNote = "Loading preview…";
        // Cancel superseded reads and serialize workers. Fast keyboard navigation
        // should start only the latest preview, not a process for every row.
        previewTask = LoadSelectedPreviewAsync(previewTask, row, cancellation, Session.Administrator);
        return previewTask;
    }
    private async Task LoadSelectedPreviewAsync(Task previous, PhotoRow row, CancellationTokenSource cancellation, bool elevate)
    {
        var token = cancellation.Token;
        bool Current() => previewCancellation == cancellation && Selected == row && !token.IsCancellationRequested;
        try
        {
            await previous;
            await Task.Delay(120, token);
            var result = await Task.Run(async () => { await using var coordinator = new PhotoCoordinator(elevate); return await coordinator.ReadAsync(new(row.Photo.Entry), true, token); });
            if (!Current()) return;
            if (result.Preview == null) { PreviewNote = result.Error ?? "Preview unavailable."; return; }
            using var stream = new MemoryStream(result.Preview);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = stream; bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze();
            Preview = bitmap; PreviewNote = "Preview of the first frame. Dimensions use the largest frame. Open the original for a full review.";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is not OutOfMemoryException) { if (Current()) PreviewNote = "Preview unavailable: " + e.Message; }
        finally
        {
            if (previewCancellation == cancellation) { previewCancellation = null; previewRow = null; Previewing = false; }
            cancellation.Dispose();
        }
    }
    public async Task ToggleAllMatchesAsync()
    {
        if (Session.Busy || ErrorsOnly) return;
        if (AllMatchesSelected == true) { ClearSelection(); return; }
        await SelectAllMatchesAsync();
    }
    public async Task SelectAllMatchesAsync()
    {
        if (Session.Busy || ErrorsOnly || matches == 0 || Session.CurrentRoot == null) return;
        if (matches > 5000) { Session.Status = "More than 5,000 matching photos. Narrow the filters before selecting all; your current selection is unchanged."; Changed(nameof(AllMatchesSelected)); return; }
        var snapshot = Session.Active; var root = Session.CurrentRoot; var query = filter; var version = revision; var database = Session.DatabasePath;
        var token = Session.BeginWork();
        Session.Status = "Selecting matching photos across all pages…";
        try
        {
            var candidates = await Task.Run(() => { using var store = new IndexStore(database); return store.PhotoSelectionCandidates(snapshot, root, query, token); });
            token.ThrowIfCancellationRequested();
            if (snapshot != Session.Active || root != Session.CurrentRoot || version != revision || query != filter) return;
            if (candidates.Count > 5000) throw new InvalidOperationException("More than 5,000 matching photos. Narrow the filters; your current selection is unchanged.");
            var selectable = candidates.Where(PhotoRow.CanRemovePhoto).ToArray();
            removals.Clear(); foreach (var photo in selectable) removals[photo.Entry.Path] = photo;
            eligibleMatches = selectable.Length;
            foreach (var row in Photos) row.RestoreSelection(removals.ContainsKey(row.Path));
            Session.Status = $"{removals.Count:N0} matching photos selected across all pages. Review removals before quarantine.";
        }
        finally { Session.EndWork(); ChangedSelection(); }
    }
    internal bool CanSelect(string path)
    {
        if (removals.ContainsKey(path) || removals.Count < 5000) return true;
        Session.Status = "Review the current selection before adding more than 5,000 photos."; return false;
    }
    internal void RemovalChanged(PhotoRow row)
    {
        if (row.Remove) removals[row.Path] = row.Photo; else removals.Remove(row.Path);
        ChangedSelection();
    }
    private void ChangedSelection() { Changed(nameof(HasSelection)); Changed(nameof(SelectionText)); Changed(nameof(SelectionNote)); Changed(nameof(AllMatchesSelected)); }
    public void ClearSelection()
    {
        removals.Clear(); eligibleMatches = null; foreach (var row in Photos) row.RestoreSelection(false); ChangedSelection();
    }
    public CleanupSelection[] BuildSelections() => Session.Busy ? [] : removals.Values.Select(x => new CleanupSelection(x.Entry)).ToArray();
    public void Dispose() { clock.Stop(); ClearPreview(); }
}
