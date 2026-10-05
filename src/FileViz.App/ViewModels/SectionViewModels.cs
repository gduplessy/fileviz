using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using FileViz.App.Views;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
namespace FileViz.App.ViewModels;

/// <summary>A folder's change between snapshots with diverging bar widths (shrink left, growth right).</summary>
public sealed record FolderChangeRow(string Path, string Name, string Delta, double ShrinkWidth, double GrowWidth, bool Grew);

/// <summary>Before/after comparison of two snapshots with the same roots.</summary>
public sealed class CompareViewModel : Bindable, ISnapshotSection
{
    private const double BarWidth = 110;
    private readonly SessionViewModel session;
    public SessionViewModel Session => session;
    public ObservableCollection<Difference> Differences { get; } = [];
    public ObservableCollection<FolderChangeRow> Folders { get; } = [];
    private SnapshotRow? before; public SnapshotRow? CompareBefore
    {
        get => before; set
        {
            if (Set(ref before, value))
                ChangedWarning();
        }
    }
    private CompareSummary? totals; public CompareSummary? Totals
    {
        get => totals; private set
        {
            if (!Set(ref totals, value))
                return;
            Changed(nameof(HasResults));
            Changed(nameof(NoResults));
            Changed(nameof(Unchanged));
            Changed(nameof(NetText));
            Changed(nameof(AddedText));
            Changed(nameof(AddedFiles));
            Changed(nameof(RemovedText));
            Changed(nameof(RemovedFiles));
            Changed(nameof(GrownText));
            Changed(nameof(GrownFiles));
        }
    }
    public bool HasResults => Totals != null;
    public bool NoResults => Totals == null;
    public bool Unchanged => Totals is { AddedFiles: 0, RemovedFiles: 0, GrownFiles: 0, ShrunkFiles: 0 };
    public string NetText => Totals == null ? "" : Signed(Totals.NetBytes);
    public string AddedText => Totals == null ? "" : Totals.AddedBytes == 0 ? "0 B" : "+" + Format.Bytes(Totals.AddedBytes);
    public string AddedFiles => Totals == null ? "" : Format.Count(Totals.AddedFiles, "file");
    public string RemovedText => Totals == null ? "" : Totals.RemovedBytes == 0 ? "0 B" : "−" + Format.Bytes(Totals.RemovedBytes);
    public string RemovedFiles => Totals == null ? "" : Format.Count(Totals.RemovedFiles, "file");
    public string GrownText => Totals == null ? "" : Signed(Totals.GrownBytes - Totals.ShrunkBytes);
    public string GrownFiles => Totals == null ? "" : $"{Format.Count(Totals.GrownFiles, "file")} grew · {Format.Count(Totals.ShrunkFiles, "file")} shrank";
    private string change = "All"; public string Change
    {
        get => change; set
        {
            if (!Set(ref change, value))
                return;
            Changed(nameof(ShowAll));
            Changed(nameof(ShowAdded));
            Changed(nameof(ShowRemoved));
            Changed(nameof(ShowGrown));
            if (HasResults)
                session.Run(LoadFilesAsync);
        }
    }
    public bool ShowAll
    {
        get => Change == "All"; set { if (value) Change = "All"; }
    }
    public bool ShowAdded
    {
        get => Change == "Added"; set { if (value) Change = "Added"; }
    }
    public bool ShowRemoved
    {
        get => Change == "Removed"; set { if (value) Change = "Removed"; }
    }
    public bool ShowGrown
    {
        get => Change == "Grown"; set { if (value) Change = "Grown"; }
    }
    public bool HasWarning => !Complete(CompareBefore) || !Complete(session.SelectedSnapshot);
    public string WarningText => !Complete(CompareBefore) ? $"Snapshot #{CompareBefore?.Value.Id} is {CompareBefore?.Value.State}. Files in folders it could not read may show as Added here." : $"Snapshot #{session.SelectedSnapshot?.Value.Id} is {session.SelectedSnapshot?.Value.State}. Files in folders it could not read may show as Removed here.";
    public ICommand CompareCommand
    {
        get;
    }
    public ICommand SwapCommand
    {
        get;
    }
    public CompareViewModel(SessionViewModel session)
    {
        this.session = session;
        CompareCommand = new ActionCommand(() => session.Run(CompareAsync), () => session.HasSnapshot && CompareBefore != null && !session.Busy);
        SwapCommand = new ActionCommand(() =>
        {
            var current = session.SelectedSnapshot;
            var previous = CompareBefore;
            CompareBefore = current;
            session.SelectedSnapshot = previous;
        }, () => CompareBefore != null && session.SelectedSnapshot != null && !session.Busy);
        session.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SessionViewModel.SelectedSnapshot)) ChangedWarning(); };
    }
    private static bool Complete(SnapshotRow? row) => row == null || row.Value.State == "Complete";
    private static string Signed(long bytes) => bytes == 0 ? "0 B" : (bytes > 0 ? "+" : "−") + Format.Bytes(Math.Abs(bytes));
    private void ChangedWarning()
    {
        Changed(nameof(HasWarning));
        Changed(nameof(WarningText));
    }
    public void Reset()
    {
        Totals = null;
        Differences.Clear();
        Folders.Clear();
    }
    public void HistoryChanged()
    {
        var id = CompareBefore?.Value.Id;
        CompareBefore = session.History.FirstOrDefault(x => x.Value.Id == id) ?? session.History.Skip(1).FirstOrDefault();
    }
    public Task RefreshAsync() => Task.CompletedTask;
    private async Task CompareAsync()
    {
        if (CompareBefore == null || session.SelectedSnapshot == null)
            return;
        var beforeRoots = JsonSerializer.Deserialize<string[]>(CompareBefore.Value.Roots) ?? [];
        var afterRoots = session.SnapshotRoots.ToArray();
        if (!beforeRoots.Order().SequenceEqual(afterRoots.Order(), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Compare snapshots covering the same roots.");
        var beforeId = CompareBefore.Value.Id;
        var after = session.Active;
        var root = session.CurrentRoot ?? afterRoots.FirstOrDefault();
        var database = session.DatabasePath;
        var result = await Task.Run(() =>
        {
            using var reader = new IndexStore(database);
            return (reader.CompareTotals(beforeId, after), root == null ? [] : reader.FolderChanges(beforeId, after, root));
        });
        var largest = Math.Max(1, result.Item2.Count == 0 ? 1 : result.Item2.Max(x => Math.Abs(x.Delta)));
        SessionViewModel.Replace(Folders, result.Item2.Select(x =>
        {
            var width = Math.Max(2, BarWidth * Math.Abs(x.Delta) / largest);
            var name = root != null && Paths.Within(x.Path, root) ? Path.GetRelativePath(root, x.Path) : x.Path;
            return new FolderChangeRow(x.Path, name, Signed(x.Delta), x.Delta < 0 ? width : 0, x.Delta > 0 ? width : 0, x.Delta > 0);
        }));
        Totals = result.Item1;
        await LoadFilesAsync();
        session.Status = "Comparison ready. Partial snapshots have incomplete coverage.";
    }
    private async Task LoadFilesAsync()
    {
        if (CompareBefore == null)
            return;
        var beforeId = CompareBefore.Value.Id;
        var after = session.Active;
        var filter = Change == "All" ? null : Change;
        var database = session.DatabasePath;
        SessionViewModel.Replace(Differences, await Task.Run(() => { using var reader = new IndexStore(database); return reader.Compare(beforeId, after, change: filter); }));
    }
}

/// <summary>Quarantine journal: quarantine, restore, and Recycle Bin requests.</summary>
public sealed class CleanupViewModel : Bindable, ISnapshotSection
{
    private readonly SessionViewModel session;
    public SessionViewModel Session => session;
    public ObservableCollection<CleanupRecord> CleanupHistory { get; } = [];
    public ObservableCollection<CleanupRecord> Visible { get; } = [];
    public int HeldCount => CleanupHistory.Count(x => x.State == "Quarantined");
    public string HeldLabel => HeldCount == 0 ? "" : $"{HeldCount} held";
    private string heldText = ""; public string HeldText
    {
        get => heldText; private set => Set(ref heldText, value);
    }
    public int RestoredCount => CleanupHistory.Count(x => x.State == "Restored");
    public int DisposedCount => CleanupHistory.Count(x => x.State == "Windows disposal completed");
    public int ProblemCount => CleanupHistory.Count(x => x.State is "Failed" or "Needs review");
    private bool showAll; public bool ShowAll
    {
        get => showAll; set
        {
            if (Set(ref showAll, value))
            {
                Changed(nameof(ShowHeld));
                Filter();
            }
        }
    }
    public bool ShowHeld
    {
        get => !ShowAll; set => ShowAll = !value;
    }
    public string HeldFilterLabel => $"Held · {HeldCount:N0}";
    public string AllFilterLabel => $"All · {CleanupHistory.Count:N0}";
    private CleanupRecord? selected; public CleanupRecord? Selected
    {
        get => selected; set
        {
            if (Set(ref selected, value))
            {
                Changed(nameof(HasSelection));
                Changed(nameof(SelectedName));
                Changed(nameof(SelectedTime));
                Changed(nameof(SelectedIsHeld));
            }
        }
    }
    public bool HasSelection => Selected != null;
    public string SelectedName => Selected == null ? "" : Path.GetFileName(Selected.Original);
    public string SelectedTime => Selected == null ? "" : DateTime.TryParse(Selected.Time, null, System.Globalization.DateTimeStyles.RoundtripKind, out var time) ? time.ToLocalTime().ToString("g") : Selected.Time;
    public bool SelectedIsHeld => Selected?.State == "Quarantined";
    public CleanupViewModel(SessionViewModel session) => this.session = session;
    public void Reset()
    {
    }
    public void HistoryChanged()
    {
    }
    public Task RefreshAsync()
    {
        Reload();
        return Task.CompletedTask;
    }
    public void Reload()
    {
        var id = Selected?.Id;
        SessionViewModel.Replace(CleanupHistory, session.Store.CleanupHistory());
        long bytes = 0;
        foreach (var record in CleanupHistory.Where(x => x.State == "Quarantined"))
        {
            try { bytes += new FileInfo(record.Destination).Length; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        HeldText = $"{Format.Count(HeldCount, "file")} · {Format.Bytes(bytes)}";
        foreach (var name in new[] { nameof(HeldCount), nameof(HeldLabel), nameof(RestoredCount), nameof(DisposedCount), nameof(ProblemCount), nameof(HeldFilterLabel), nameof(AllFilterLabel) })
            Changed(name);
        Filter();
        Selected = Visible.FirstOrDefault(x => x.Id == id) ?? Visible.FirstOrDefault();
    }
    private void Filter() => SessionViewModel.Replace(Visible, ShowAll ? CleanupHistory : CleanupHistory.Where(x => x.State == "Quarantined"));
    public async Task CleanupAsync(CleanupSelection[] selections)
    {
        if (session.Busy || selections.Length == 0)
            return;
        var token = session.BeginWork();
        var database = session.DatabasePath;
        try
        {
            var results = await Task.Run(async () =>
            {
                using var writer = new IndexStore(database);
                var service = new CleanupService(writer.SaveCleanup);
                var result = new List<CleanupRecord>();
                foreach (var selection in selections)
                {
                    token.ThrowIfCancellationRequested();
                    var record = await service.QuarantineAsync(selection, token);
                    result.Add(record);
                    if (record.State == "Quarantined")
                        writer.MarkStale(record.Original);
                }
                return result;
            });
            Reload();
            session.Status = $"Quarantined {results.Count(x => x.State == "Quarantined")} of {results.Count} files. No space reclaimed yet; review any failures in Cleanup.";
        }
        finally { session.EndWork(); session.ReloadHistory(); }
    }
    public void Restore(CleanupRecord record)
    {
        if (session.Busy)
            return;
        var result = new CleanupService(session.Store.SaveCleanup).Restore(record);
        Reload();
        session.Status = result.Error ?? "Restored without overwriting existing files.";
    }
    public void Recycle(CleanupRecord record)
    {
        if (session.Busy)
            return;
        var result = new CleanupService(session.Store.SaveCleanup).Recycle(record);
        Reload();
        session.Status = result.Error ?? "Windows completed the Recycle Bin request. Review Windows prompts for the resulting disposal method.";
    }
}

/// <summary>A diagnostic with its kind, severity, and label.</summary>
public sealed record DiagnosticRow(string Path, string Message, string Kind, string Label, DiagnosticSeverity Severity)
{
    public string SeverityName => Severity.ToString();
}

/// <summary>Coverage gaps and scan errors recorded with the snapshot.</summary>
public sealed class DiagnosticsViewModel : Bindable, ISnapshotSection
{
    private readonly SessionViewModel session;
    private List<DiagnosticRow> all = [];
    public SessionViewModel Session => session;
    public ObservableCollection<ScanError> Errors { get; } = [];
    public ObservableCollection<DiagnosticRow> Rows { get; } = [];
    public string CountLabel => Errors.Count == 0 ? "" : Errors.Count.ToString("N0");
    public int GapCount => all.Count(x => x.Severity == DiagnosticSeverity.Gap);
    public int WarningCount => all.Count(x => x.Severity == DiagnosticSeverity.Warning);
    public int InfoCount => all.Count(x => x.Severity == DiagnosticSeverity.Info);
    public bool HasRows => all.Count > 0;
    public bool NoRows => all.Count == 0;
    private string headline = ""; public string Headline
    {
        get => headline; private set => Set(ref headline, value);
    }
    public string SummaryText => GapCount > 0 ? "Missing folders are left out of totals, duplicate analysis, and comparisons. FileViz never estimates their size." : all.Count > 0 ? "All folders were indexed. These notes explain recoveries and items listed but not followed." : "No diagnostics for this snapshot.";
    private string filter = "All"; public string Filter
    {
        get => filter; set
        {
            if (!Set(ref filter, value))
                return;
            Changed(nameof(ShowAll));
            Changed(nameof(ShowGaps));
            Changed(nameof(ShowWarnings));
            Changed(nameof(ShowInfo));
            Apply();
        }
    }
    public bool ShowAll
    {
        get => Filter == "All"; set { if (value) Filter = "All"; }
    }
    public bool ShowGaps
    {
        get => Filter == "Gap"; set { if (value) Filter = "Gap"; }
    }
    public bool ShowWarnings
    {
        get => Filter == "Warning"; set { if (value) Filter = "Warning"; }
    }
    public bool ShowInfo
    {
        get => Filter == "Info"; set { if (value) Filter = "Info"; }
    }
    public string AllLabel => $"All · {all.Count:N0}";
    public string GapLabel => $"Gaps · {GapCount:N0}";
    public string WarningLabel => $"Warnings · {WarningCount:N0}";
    public string InfoLabel => $"Not traversed · {InfoCount:N0}";
    public DiagnosticsViewModel(SessionViewModel session) => this.session = session;
    public void Reset()
    {
    }
    public void HistoryChanged()
    {
    }
    public async Task RefreshAsync()
    {
        var id = session.Active;
        var database = session.DatabasePath;
        var (errors, files) = await Task.Run(() => { using var reader = new IndexStore(database); return (reader.Errors(id), reader.GetSummary(id).Files); });
        if (session.Active != id)
            return;
        SessionViewModel.Replace(Errors, errors);
        all = errors.Select(x =>
        {
            var kind = DiagnosticKinds.Classify(x.Kind, x.Message);
            return new DiagnosticRow(x.Path, x.Message, kind, DiagnosticKinds.Label(kind), DiagnosticKinds.Severity(kind));
        }).OrderBy(x => x.Severity).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
        Headline = GapCount > 0 ? $"{files:N0} files indexed · {Format.Count(GapCount, "folder")} missing" : $"{files:N0} files indexed · complete coverage";
        foreach (var name in new[] { nameof(CountLabel), nameof(GapCount), nameof(WarningCount), nameof(InfoCount), nameof(HasRows), nameof(NoRows), nameof(SummaryText), nameof(AllLabel), nameof(GapLabel), nameof(WarningLabel), nameof(InfoLabel) })
            Changed(name);
        Apply();
    }
    private void Apply() => SessionViewModel.Replace(Rows, Filter == "All" ? all : all.Where(x => x.Severity.ToString() == Filter));
}
