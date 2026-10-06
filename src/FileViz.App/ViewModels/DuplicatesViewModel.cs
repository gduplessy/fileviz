using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FileViz.App.Services;
using FileViz.App.Views;
using FileViz.Core;
using FileViz.Windows;
namespace FileViz.App.ViewModels;

/// <summary>One copy inside a duplicate group, with the user's keep/remove choice.</summary>
public sealed class DuplicateItem(DuplicateGroup group, FileEntry entry, string note) : Bindable
{
    public DuplicateGroup Group { get; } = group;
    public FileEntry Entry { get; } = entry;
    public string Note { get; } = note;
    public bool HasNote => Note.Length > 0;
    public string GroupKey => "group-" + Group.GroupId;
    private bool keep; public bool Keep
    {
        get => keep; set
        {
            if (!Set(ref keep, value))
                return;
            if (value)
            {
                Remove = false;
                Group.KeeperChanged(this);
            }
            Changed(nameof(CanRemove));
        }
    }
    private bool remove; public bool Remove
    {
        get => remove; set
        {
            if (value && (Keep || !Group.IsVerified))
                value = false;
            if (Set(ref remove, value))
                Group.RemovalChanged(this);
        }
    }
    public bool CanRemove => !Keep && Group.IsVerified;
    /// <summary>Sets the choice without notifying the group (used when restoring saved choices).</summary>
    internal void Restore(bool keepValue, bool removeValue)
    {
        keep = keepValue;
        remove = removeValue && !keepValue;
        Changed(nameof(Keep));
        Changed(nameof(Remove));
        Changed(nameof(CanRemove));
    }
}

/// <summary>A duplicate group as a card: evidence, size, and its copies.</summary>
public sealed class DuplicateGroup(DuplicatesViewModel owner, long groupId, string evidence) : Bindable
{
    public long GroupId { get; } = groupId;
    public string Evidence { get; } = evidence;
    public bool IsVerified => !Evidence.StartsWith("Name", StringComparison.Ordinal);
    public string EvidenceLabel => IsVerified ? "Verified" : "Name only";
    public ObservableCollection<DuplicateItem> Items { get; } = [];
    public string Name => Items.FirstOrDefault()?.Entry.Name ?? "";
    public Brush Swatch => Application.Current.TryFindResource(Treemap.CategoryKey(FileCategories.Of(Items.FirstOrDefault()?.Entry.Extension ?? ""))) as Brush ?? Brushes.Gray;
    public string Meta => $"{Items.Count} copies · {Format.Bytes(Items.FirstOrDefault()?.Entry.Length ?? 0)} each";
    public string Savings => IsVerified ? "−" + Format.Bytes((Items.FirstOrDefault()?.Entry.Length ?? 0) * Math.Max(0, Items.Count - 1)) : "No savings";
    public string Hash => Evidence;
    internal void KeeperChanged(DuplicateItem keeper)
    {
        foreach (var item in Items.Where(x => x != keeper && x.Keep))
            item.Restore(false, item.Remove);
        owner.ChoiceChanged(this);
    }
    internal void RemovalChanged(DuplicateItem item) => owner.ChoiceChanged(this);
}

/// <summary>Duplicate analysis scope, pipeline counts, groups, and keeper selection.</summary>
public sealed class DuplicatesViewModel : Bindable, ISnapshotSection
{
    private readonly SessionViewModel session;
    private int page;
    private readonly Dictionary<long, FileEntry> keepers = [];
    private readonly Dictionary<string, (long Group, FileEntry Entry)> removals = new(StringComparer.OrdinalIgnoreCase);
    private bool restoring;
    private readonly System.Windows.Threading.DispatcherTimer activityClock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch elapsed = new();
    private readonly Stopwatch heartbeat = new();
    private long analysisGeneration;
    private DuplicateProgress activity = new("Ready");
    private bool analyzing;
    public bool IsAnalyzing => analyzing;
    private bool hasActivity;
    public bool HasActivity => hasActivity;
    private string analysisHeading = "Duplicate analysis";
    public string AnalysisHeading => analysisHeading;
    public string ActivityPhase => activity.Phase;
    public string ActivityCounts => $"{(activity.Total is long total ? $"{activity.Completed:N0} / {total:N0} files" : activity.Completed > 0 ? $"{activity.Completed:N0} files processed" : "File total pending")} · {Format.Bytes(activity.BytesRead)} read · {activity.Cached:N0} cached · {activity.Errors:N0} errors";
    public string ActivityFile => activity.CurrentFile?.Path ?? "";
    public string FileProgressText => activity.CurrentFile is { TotalBytes: > 0 } file ? $"Current file: {Format.Bytes(file.BytesRead)} / {Format.Bytes(file.TotalBytes)}" : "";
    public string ActivityElapsed => "Elapsed " + Format.Elapsed(elapsed.Elapsed);
    public string ActivityHeartbeat => !IsAnalyzing ? "" : heartbeat.Elapsed.TotalSeconds < 5 ? "Receiving work updates" : $"Waiting for the next work update · last activity {Format.Elapsed(heartbeat.Elapsed)} ago";
    public bool IsIndeterminate => activity.Total is not > 0;
    public double ActivityPercent => activity.Total is > 0 ? Math.Clamp(100d * (activity.Completed +
        (activity.CurrentFile is { TotalBytes: > 0 } file ? (double)file.BytesRead / file.TotalBytes : 0)) / activity.Total.Value, 0, 100) : 0;
    public string AnalyzeLabel => IsAnalyzing ? "Analyzing…" : "Analyze content";
    public SessionViewModel Session => session;
    public CleanupViewModel Cleanup
    {
        get;
    }
    public ObservableCollection<DuplicateScopeRow> DuplicateRoots { get; } = [];
    public ObservableCollection<DuplicateScopeRow> AvailableScopes { get; } = [];
    private bool synchronizingScope;
    private DuplicateScopeRow? selectedScope;
    /// <summary>Choose a saved drive/folder directly without navigating away from Duplicates.</summary>
    public DuplicateScopeRow? SelectedScope
    {
        get => selectedScope;
        set
        {
            if (synchronizingScope || value == null) return;
            if (session.Busy) { Changed(nameof(SelectedScope)); return; }
            var snapshot = session.History.FirstOrDefault(x => x.Value.Id == value.Snapshot);
            if (snapshot == null || !Set(ref selectedScope, value)) return;
            synchronizingScope = true;
            try
            {
                if (session.Active != value.Snapshot) session.SelectedSnapshot = snapshot;
                session.CurrentRoot = value.Root;
                foreach (var scope in DuplicateRoots) scope.Selected = scope.Root.Equals(value.Root, StringComparison.OrdinalIgnoreCase);
            }
            finally { synchronizingScope = false; }
            SynchronizeScope();
        }
    }
    public string ScopeDescription => session.Busy ? "Finish or cancel the current operation before changing drives."
        : CrossDrive ? "Uses the latest saved scan for each selected drive or folder. Scan another drive in Home to add it here."
        : SelectedScope is { } scope
        ? $"Saved scan #{scope.Snapshot} · {scope.State}. Scan another drive in Home to add it here."
        : "Scan a drive or folder in Home to make it available here.";
    public ObservableCollection<DuplicateRow> Duplicates { get; } = [];
    public ObservableCollection<DuplicateGroup> Groups { get; } = [];
    private string algorithm = "SHA-256"; public string Algorithm
    {
        get => algorithm; set
        {
            if (Set(ref algorithm, value))
            {
                Changed(nameof(UseSha256));
                Changed(nameof(UseSha1));
                Changed(nameof(UseMd5));
            }
        }
    }
    public bool UseSha256
    {
        get => Algorithm == "SHA-256"; set { if (value) Algorithm = "SHA-256"; }
    }
    public bool UseSha1
    {
        get => Algorithm == "SHA-1"; set { if (value) Algorithm = "SHA-1"; }
    }
    public bool UseMd5
    {
        get => Algorithm == "MD5"; set { if (value) Algorithm = "MD5"; }
    }
    private bool crossDrive;
    public bool CrossDrive
    {
        get => crossDrive; set
        {
            if (session.Busy) { Changed(nameof(CrossDrive)); return; }
            if (Set(ref crossDrive, value)) { Changed(nameof(ScopeText)); Changed(nameof(ScopeDescription)); CommandManager.InvalidateRequerySuggested(); }
        }
    }
    private string preferredFolder = ""; public string PreferredFolder
    {
        get => preferredFolder; set => Set(ref preferredFolder, value);
    }
    public string ScopeText => CrossDrive ? string.Join(", ", DuplicateRoots.Where(x => x.Selected).Select(x => x.Root)) : SelectedScope?.Root ?? "Choose a drive";
    private DuplicateRun? run; public DuplicateRun? Run
    {
        get => run; private set
        {
            if (Set(ref run, value))
            {
                Changed(nameof(HasRun));
                Changed(nameof(NoRun));
                Changed(nameof(VerifiedNote));
            }
        }
    }
    public bool HasRun => Run != null && !IsAnalyzing;
    public bool NoRun => Run == null && !HasActivity;
    public string VerifiedNote => Run == null ? "" : Run.Algorithm == "Name" ? Format.Count(Run.Groups, "name group") : $"files in {Format.Count(Run.Groups, "group")}";
    public string PageLabel => $"Groups page {page + 1}";
    public string SelectedBytes => Format.Bytes(removals.Values.Sum(x => x.Entry.Length));
    public string SelectedSummary => removals.Count == 0 ? "Select the copies to remove. Each group must keep one." : $"{Format.Count(removals.Count, "file")} in {Format.Count(removals.Values.Select(x => x.Group).Distinct().Count(), "group")} · every group keeps a copy";
    public bool HasSelection => removals.Count > 0 && !session.Busy;
    public string ReviewLabel => removals.Count == 0 ? "Review removals…" : $"Review {Format.Count(removals.Count, "removal")}…";
    public ICommand DuplicateCommand
    {
        get;
    }
    public ICommand NameCommand
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
    public ICommand SelectAllCommand
    {
        get;
    }
    public ICommand ClearCommand
    {
        get;
    }
    public DuplicatesViewModel(SessionViewModel session, CleanupViewModel cleanup)
    {
        this.session = session;
        Cleanup = cleanup;
        activityClock.Tick += (_, _) => { Changed(nameof(ActivityElapsed)); Changed(nameof(ActivityHeartbeat)); };
        DuplicateCommand = new ActionCommand(() => session.Run(FindDuplicatesAsync), CanAnalyze);
        NameCommand = new ActionCommand(() => session.Run(() => FindDuplicatesAsync("Name")), CanAnalyze);
        PreviousCommand = new ActionCommand(() => { page = Math.Max(0, page - 1); RefreshDuplicates(); }, () => page > 0 && !session.Busy);
        NextCommand = new ActionCommand(() => { page++; RefreshDuplicates(); }, () => Duplicates.Count == 250 && !session.Busy);
        SelectAllCommand = new ActionCommand(() =>
        {
            foreach (var item in Groups.Where(x => x.IsVerified).SelectMany(x => x.Items).Where(x => !x.Keep))
                item.Remove = true;
        }, () => Groups.Any(x => x.IsVerified));
        ClearCommand = new ActionCommand(() =>
        {
            removals.Clear();
            RefreshDuplicates();
        }, () => removals.Count > 0);
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionViewModel.CurrentRoot)) SynchronizeScope();
            if (e.PropertyName == nameof(SessionViewModel.Busy)) { Changed(nameof(HasSelection)); Changed(nameof(ScopeDescription)); }
        };
    }
    public void Reset()
    {
        page = 0;
        keepers.Clear();
        removals.Clear();
        if (!IsAnalyzing) { hasActivity = false; Changed(nameof(HasActivity)); Changed(nameof(NoRun)); }
        ChangedSelection();
    }
    public void HistoryChanged()
    {
        var choices = session.History.SelectMany(x => (JsonSerializer.Deserialize<string[]>(x.Value.Roots) ?? []).Select(root => new { x.Value.Id, x.Value.State, Root = root })).GroupBy(x => x.Root, StringComparer.OrdinalIgnoreCase).Select(x => x.OrderByDescending(y => y.Id).First()).OrderBy(x => x.Root, StringComparer.OrdinalIgnoreCase).ToArray();
        var selectedRoots = DuplicateRoots.Where(x => x.Selected).Select(x => x.Root).ToHashSet(StringComparer.OrdinalIgnoreCase);
        synchronizingScope = true;
        try
        {
            SessionViewModel.Replace(DuplicateRoots, choices.Select(x => new DuplicateScopeRow(x.Id, x.Root, selectedRoots.Contains(x.Root) || x.Root == session.CurrentRoot, x.State)));
            foreach (var scope in DuplicateRoots)
                scope.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DuplicateScopeRow.Selected)) { Changed(nameof(ScopeText)); CommandManager.InvalidateRequerySuggested(); } };
            SessionViewModel.Replace(AvailableScopes, DuplicateRoots);
        }
        finally { synchronizingScope = false; }
        SynchronizeScope();
    }
    private bool CanAnalyze() => session.HasSnapshot && !session.Busy && (CrossDrive ? DuplicateRoots.Any(x => x.Selected) : SelectedScope != null);
    private void SynchronizeScope()
    {
        if (synchronizingScope) return;
        synchronizingScope = true;
        try
        {
            var scope = AvailableScopes.FirstOrDefault(x => x.Snapshot == session.Active && x.Root.Equals(session.CurrentRoot, StringComparison.OrdinalIgnoreCase));
            // Keep an explicitly reopened historical snapshot selectable alongside the latest scans.
            if (scope == null && session.CurrentRoot is { } root && session.SelectedSnapshot is { } snapshot)
            {
                scope = new(session.Active, root, false, snapshot.Value.State);
                AvailableScopes.Add(scope);
            }
            selectedScope = scope;
            if (!DuplicateRoots.Any(x => x.Selected))
                foreach (var row in DuplicateRoots) row.Selected = row.Root.Equals(session.CurrentRoot, StringComparison.OrdinalIgnoreCase);
            Changed(nameof(SelectedScope));
            Changed(nameof(ScopeText));
            Changed(nameof(ScopeDescription));
            CommandManager.InvalidateRequerySuggested();
        }
        finally { synchronizingScope = false; }
    }
    public Task RefreshAsync()
    {
        RefreshDuplicates();
        return Task.CompletedTask;
    }
    public Task FindDuplicatesAsync() => FindDuplicatesAsync(Algorithm);
    public async Task FindDuplicatesAsync(string method)
    {
        if (!CanAnalyze()) return;
        var id = session.Active;
        var snapshots = new[] { id };
        var roots = SelectedScope is { } scope ? new[] { scope.Root } : [];
        if (CrossDrive)
        {
            var selected = DuplicateRoots.Where(x => x.Selected).ToArray();
            if (selected.Length == 0)
            {
                session.Status = "Select roots for duplicate analysis.";
                return;
            }
            snapshots = selected.Select(x => x.Snapshot).Distinct().ToArray();
            roots = selected.Select(x => x.Root).Distinct().ToArray();
        }
        var token = session.BeginWork();
        var generation = ++analysisGeneration;
        analyzing = true;
        hasActivity = true;
        analysisHeading = method == "Name" ? "Finding name matches" : "Analyzing content · " + method;
        activity = new("Preparing duplicate analysis");
        elapsed.Restart();
        heartbeat.Restart();
        activityClock.Start();
        ChangedActivity();
        session.Status = analysisHeading + " · " + ActivityPhase;
        try
        {
            var database = session.DatabasePath;
            var preferred = PreferredFolder;
            var elevated = session.Administrator;
            var progress = new Progress<DuplicateProgress>(update =>
            {
                if (!IsAnalyzing || generation != analysisGeneration) return;
                activity = update;
                heartbeat.Restart();
                ChangedActivity();
                session.Status = analysisHeading + " · " + update.Phase;
            });
            await Task.Run(() => DuplicateCoordinator.FindAsync(database, id, snapshots, roots, method, preferred, elevated, progress, token));
            page = 0;
            keepers.Clear();
            removals.Clear();
            RefreshDuplicates();
            session.Status = method == "Name" ? "Name matches found. Names never establish duplicates; run content analysis before cleanup." : "Duplicate analysis complete. Cleanup performs fresh byte and stream verification.";
            analysisHeading = method == "Name" ? "Name matching complete" : "Content analysis complete";
            activity = activity with { Phase = Groups.Count == 0 ? "No matching groups found in this scope." : "Results ready for review.", CurrentFile = null };
        }
        catch (OperationCanceledException)
        {
            analysisHeading = "Duplicate analysis cancelled";
            activity = activity with { Phase = "Analysis stopped. Previously completed results are retained.", CurrentFile = null };
            session.Status = analysisHeading;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            analysisHeading = "Duplicate analysis failed";
            activity = activity with { Phase = e.Message, CurrentFile = null };
            session.Status = analysisHeading + ": " + e.Message;
        }
        finally
        {
            elapsed.Stop();
            activityClock.Stop();
            analyzing = false;
            session.EndWork();
            ChangedActivity();
        }
    }
    private void ChangedActivity()
    {
        foreach (var property in new[] { nameof(IsAnalyzing), nameof(HasActivity), nameof(AnalysisHeading), nameof(ActivityPhase),
            nameof(ActivityCounts), nameof(ActivityFile), nameof(FileProgressText), nameof(ActivityElapsed), nameof(ActivityHeartbeat),
            nameof(IsIndeterminate), nameof(ActivityPercent), nameof(AnalyzeLabel), nameof(HasRun), nameof(NoRun) }) Changed(property);
    }
    private void RefreshDuplicates()
    {
        if (session.Active == 0)
            return;
        Run = session.Store.LastDuplicateRun(session.Active);
        SessionViewModel.Replace(Duplicates, session.Store.Duplicates(session.Active, page));
        restoring = true;
        try
        {
            var groups = new List<DuplicateGroup>();
            foreach (var rows in Duplicates.GroupBy(x => x.GroupId))
            {
                var first = rows.First();
                var group = new DuplicateGroup(this, first.GroupId, first.Evidence);
                foreach (var row in rows)
                    group.Items.Add(new(group, row.Entry, row.SuggestedKeeper && group.IsVerified ? "Suggested keeper" : ""));
                var keeper = keepers.TryGetValue(group.GroupId, out var chosen) ? group.Items.FirstOrDefault(x => x.Entry.Path == chosen.Path) : null;
                keeper ??= group.Items.FirstOrDefault(x => rows.Any(r => r.SuggestedKeeper && r.Entry.Path == x.Entry.Path)) ?? group.Items.First();
                foreach (var item in group.Items)
                    item.Restore(item == keeper, removals.ContainsKey(item.Entry.Path));
                groups.Add(group);
            }
            SessionViewModel.Replace(Groups, groups);
        }
        finally { restoring = false; }
        Changed(nameof(PageLabel));
        ChangedSelection();
    }
    internal void ChoiceChanged(DuplicateGroup group)
    {
        if (restoring)
            return;
        if (group.Items.FirstOrDefault(x => x.Keep) is { } keeper)
            keepers[group.GroupId] = keeper.Entry;
        foreach (var item in group.Items)
        {
            if (item.Remove)
                removals[item.Entry.Path] = (group.GroupId, item.Entry);
            else
                removals.Remove(item.Entry.Path);
        }
        ChangedSelection();
    }
    private void ChangedSelection()
    {
        Changed(nameof(SelectedBytes));
        Changed(nameof(SelectedSummary));
        Changed(nameof(HasSelection));
        Changed(nameof(ReviewLabel));
        CommandManager.InvalidateRequerySuggested();
    }
    /// <summary>Builds cleanup selections; every removal is paired with its group's keeper, which must not itself be removed.</summary>
    public CleanupSelection[]? BuildSelections()
    {
        var result = new List<CleanupSelection>();
        foreach (var removal in removals.Values)
        {
            var keeper = keepers.TryGetValue(removal.Group, out var chosen) ? chosen : session.Store.DuplicateGroup(session.Active, removal.Group).FirstOrDefault(x => !removals.ContainsKey(x.Path));
            if (keeper == null || removals.ContainsKey(keeper.Path))
            {
                session.Status = "Keep at least one copy in every duplicate group.";
                return null;
            }
            result.Add(new(removal.Entry, keeper));
        }
        return result.ToArray();
    }
    /// <summary>Clears the selection after files were moved to quarantine.</summary>
    public void Completed()
    {
        removals.Clear();
        RefreshDuplicates();
    }
    public FileEntry? KeeperFor(DuplicateRow row, IEnumerable<string> removed) => session.Store.DuplicateGroup(session.Active, row.GroupId).FirstOrDefault(x => !removed.Contains(x.Path, StringComparer.Ordinal));
}
