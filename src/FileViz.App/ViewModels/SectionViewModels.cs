using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
namespace FileViz.App.ViewModels;

/// <summary>Before/after comparison of two snapshots with the same roots.</summary>
public sealed class CompareViewModel : Bindable, ISnapshotSection
{
    private readonly SessionViewModel session;
    public SessionViewModel Session => session;
    public ObservableCollection<Difference> Differences { get; } = [];
    private SnapshotRow? before; public SnapshotRow? CompareBefore
    {
        get => before; set => Set(ref before, value);
    }
    public ICommand CompareCommand
    {
        get;
    }
    public CompareViewModel(SessionViewModel session)
    {
        this.session = session;
        CompareCommand = new ActionCommand(() => session.Run(CompareAsync), () => session.HasSnapshot && CompareBefore != null && !session.Busy);
    }
    public void Reset()
    {
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
        var database = session.DatabasePath;
        SessionViewModel.Replace(Differences, await Task.Run(() => { using var reader = new IndexStore(database); return reader.Compare(beforeId, after); }));
        session.Status = "Showing up to 250 added, removed, or grown files. Partial snapshots have incomplete coverage.";
    }
}

/// <summary>Quarantine journal: quarantine, restore, and Recycle Bin requests.</summary>
public sealed class CleanupViewModel : Bindable, ISnapshotSection
{
    private readonly SessionViewModel session;
    public SessionViewModel Session => session;
    public ObservableCollection<CleanupRecord> CleanupHistory { get; } = [];
    public int HeldCount => CleanupHistory.Count(x => x.State == "Quarantined");
    public string HeldLabel => HeldCount == 0 ? "" : $"{HeldCount} held";
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
        SessionViewModel.Replace(CleanupHistory, session.Store.CleanupHistory());
        Changed(nameof(HeldCount));
        Changed(nameof(HeldLabel));
    }
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
            session.Status = $"Quarantined {results.Count(x => x.State == "Quarantined")} of {results.Count} files. No space reclaimed; review failures in cleanup history.";
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

/// <summary>Coverage gaps and scan errors recorded with the snapshot.</summary>
public sealed class DiagnosticsViewModel : Bindable, ISnapshotSection
{
    private readonly SessionViewModel session;
    public SessionViewModel Session => session;
    public ObservableCollection<ScanError> Errors { get; } = [];
    public string CountLabel => Errors.Count == 0 ? "" : Errors.Count.ToString("N0");
    public string SummaryText => Errors.Count == 0 ? "No diagnostics for this snapshot." : $"{Errors.Count:N0} diagnostics. Unreadable folders are left out of totals, duplicates, and comparisons.";
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
        var errors = await Task.Run(() => { using var reader = new IndexStore(database); return reader.Errors(id); });
        if (session.Active != id)
            return;
        SessionViewModel.Replace(Errors, errors);
        Changed(nameof(CountLabel));
        Changed(nameof(SummaryText));
    }
}

/// <summary>Appearance and application information.</summary>
public sealed class SettingsViewModel : Bindable
{
    public string[] Themes { get; } = ["Use system setting", "Light", "Dark"];
    private string theme = "Use system setting"; public string Theme
    {
        get => theme; set
        {
            if (Set(ref theme, value))
                App.SetTheme(value switch { "Light" => System.Windows.ThemeMode.Light, "Dark" => System.Windows.ThemeMode.Dark, _ => System.Windows.ThemeMode.System });
        }
    }
    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "";
    public string IndexPath
    {
        get;
    }
    public ICommand ColorSettingsCommand { get; } = new ActionCommand(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:colors") { UseShellExecute = true }));
    public SettingsViewModel(SessionViewModel session) => IndexPath = session.DatabasePath;
}
