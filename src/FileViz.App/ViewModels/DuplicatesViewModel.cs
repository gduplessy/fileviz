using System.Collections.ObjectModel;
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
    public SessionViewModel Session => session;
    public CleanupViewModel Cleanup
    {
        get;
    }
    public ObservableCollection<DuplicateScopeRow> DuplicateRoots { get; } = [];
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
    public bool CrossDrive { get; set; } = false;
    public string PreferredFolder { get; set; } = "";
    public string ScopeText => CrossDrive ? "Selected roots" : session.CurrentRoot ?? "No root";
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
    public bool HasRun => Run != null;
    public bool NoRun => Run == null;
    public string VerifiedNote => Run == null ? "" : Run.Algorithm == "Name" ? Format.Count(Run.Groups, "name group") : $"files in {Format.Count(Run.Groups, "group")}";
    public string PageLabel => $"Groups page {page + 1}";
    public string SelectedBytes => Format.Bytes(removals.Values.Sum(x => x.Entry.Length));
    public string SelectedSummary => removals.Count == 0 ? "Select the copies to remove. Each group must keep one." : $"{Format.Count(removals.Count, "file")} in {Format.Count(removals.Values.Select(x => x.Group).Distinct().Count(), "group")} · every group keeps a copy";
    public bool HasSelection => removals.Count > 0;
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
        DuplicateCommand = new ActionCommand(() => session.Run(FindDuplicatesAsync), () => session.HasSnapshot && !session.Busy);
        NameCommand = new ActionCommand(() => session.Run(() => FindDuplicatesAsync("Name")), () => session.HasSnapshot && !session.Busy);
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
        session.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SessionViewModel.CurrentRoot)) Changed(nameof(ScopeText)); };
    }
    public void Reset()
    {
        page = 0;
        keepers.Clear();
        removals.Clear();
        ChangedSelection();
    }
    public void HistoryChanged()
    {
        var choices = session.History.SelectMany(x => (JsonSerializer.Deserialize<string[]>(x.Value.Roots) ?? []).Select(root => new { x.Value.Id, Root = root })).GroupBy(x => x.Root, StringComparer.OrdinalIgnoreCase).Select(x => x.OrderByDescending(y => y.Id).First()).ToArray();
        var selectedRoots = DuplicateRoots.Where(x => x.Selected).Select(x => x.Root).ToHashSet(StringComparer.OrdinalIgnoreCase);
        SessionViewModel.Replace(DuplicateRoots, choices.Select(x => new DuplicateScopeRow(x.Id, x.Root, selectedRoots.Contains(x.Root) || x.Root == session.CurrentRoot)));
    }
    public Task RefreshAsync()
    {
        RefreshDuplicates();
        return Task.CompletedTask;
    }
    public Task FindDuplicatesAsync() => FindDuplicatesAsync(Algorithm);
    public async Task FindDuplicatesAsync(string method)
    {
        var id = session.Active;
        var snapshots = new[] { id };
        var roots = session.CurrentRoot == null ? session.SnapshotRoots.ToArray() : new[] { session.CurrentRoot };
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
        try
        {
            var database = session.DatabasePath;
            var preferred = PreferredFolder;
            var elevated = session.Administrator;
            await Task.Run(() => DuplicateCoordinator.FindAsync(database, id, snapshots, roots, method, preferred, elevated, new Progress<string>(text => Application.Current.Dispatcher.Invoke(() => session.Status = text)), token));
            page = 0;
            keepers.Clear();
            removals.Clear();
            RefreshDuplicates();
            session.Status = method == "Name" ? "Name matches found. Names never establish duplicates; run content analysis before cleanup." : "Duplicate analysis complete. Cleanup performs fresh byte and stream verification.";
        }
        finally { session.EndWork(); }
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
