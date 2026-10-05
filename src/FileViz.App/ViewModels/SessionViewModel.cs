using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
namespace FileViz.App.ViewModels;

/// <summary>A section whose content depends on the selected snapshot.</summary>
public interface ISnapshotSection
{
    /// <summary>Selected snapshot or root changed: drop paging and navigation state.</summary>
    void Reset();
    /// <summary>The snapshot list was reloaded.</summary>
    void HistoryChanged();
    Task RefreshAsync();
}

/// <summary>State shared by every section: the index, the selected snapshot and root, and long-running work.</summary>
public sealed class SessionViewModel : Bindable, IDisposable
{
    private readonly List<ISnapshotSection> sections = [];
    private CancellationTokenSource? cancellation; private TaskCompletionSource? pause;
    public string DatabasePath
    {
        get;
    }
    internal IndexStore Store
    {
        get;
    }
    public long Active
    {
        get; private set;
    }
    public bool HasSnapshot => Active > 0;
    public ObservableCollection<SnapshotRow> History { get; } = [];
    public ObservableCollection<string> SnapshotRoots { get; } = [];
    private string status = "Choose a drive or folder, then scan."; public string Status
    {
        get => status; set => Set(ref status, value);
    }
    private bool busy; public bool Busy
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
    public bool CanInteract => !Busy;
    private bool scanning; public bool Scanning
    {
        get => scanning; private set => Set(ref scanning, value);
    }
    public string PauseLabel => pause == null ? "Pause" : "Resume";
    private bool administrator; public bool Administrator
    {
        get => administrator; set
        {
            if (Set(ref administrator, value))
                Changed(nameof(ElevationText));
        }
    }
    public string ElevationText => Native.IsElevated ? "Administrator" : Administrator ? "Standard user · elevated worker" : "Standard user";
    public string SnapshotText => SelectedSnapshot is { } row ? $"Snapshot #{row.Value.Id} · {row.Value.State}" : "No snapshot";
    private string? currentRoot; public string? CurrentRoot
    {
        get => currentRoot; set
        {
            if (Set(ref currentRoot, value))
            {
                ResetSections();
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
            Active = value?.Value.Id ?? 0;
            SnapshotRoots.Clear();
            if (value != null)
                foreach (var root in JsonSerializer.Deserialize<string[]>(value.Value.Roots) ?? [])
                    SnapshotRoots.Add(root);
            currentRoot = SnapshotRoots.FirstOrDefault();
            Changed(nameof(CurrentRoot));
            Changed(nameof(HasSnapshot));
            Changed(nameof(SnapshotText));
            ResetSections();
            Run(RefreshAsync);
        }
    }
    public ICommand CancelCommand
    {
        get;
    }
    public ICommand PauseCommand
    {
        get;
    }
    /// <summary>Raised after a scan finishes and its snapshot is selected.</summary>
    public event EventHandler? SnapshotOpened;
    public SessionViewModel(string? database = null)
    {
        DatabasePath = database ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileViz", "index.db");
        Store = new(DatabasePath);
        Store.RecoverInterrupted();
        var cleanup = new CleanupService(Store.SaveCleanup);
        foreach (var record in Store.CleanupHistory())
            cleanup.Recover(record);
        CancelCommand = new ActionCommand(() => { cancellation?.Cancel(); pause?.TrySetResult(); }, () => Busy);
        PauseCommand = new ActionCommand(() =>
        {
            if (pause == null)
                pause = new(TaskCreationOptions.RunContinuationsAsynchronously);
            else
            {
                pause.TrySetResult();
                pause = null;
            }
            Changed(nameof(PauseLabel));
            Status = pause == null ? "Resuming scan…" : "Paused at the next batch boundary.";
        }, () => Busy && Scanning);
    }
    public void Register(ISnapshotSection section) => sections.Add(section);
    /// <summary>Marks the session busy and returns the token for the work. Pair with <see cref="EndWork"/>.</summary>
    public CancellationToken BeginWork(bool scan = false)
    {
        Scanning = scan;
        Busy = true;
        cancellation = new();
        return cancellation.Token;
    }
    public void EndWork()
    {
        pause?.TrySetResult();
        pause = null;
        Changed(nameof(PauseLabel));
        Scanning = false;
        Busy = false;
        cancellation?.Dispose();
        cancellation = null;
        CommandManager.InvalidateRequerySuggested();
    }
    /// <summary>Waits while the user has paused the scan.</summary>
    public Task WaitIfPausedAsync(CancellationToken token) => pause is { } gate ? gate.Task.WaitAsync(token) : Task.CompletedTask;
    /// <summary>Points the session at a snapshot while it is being written, before it appears in history.</summary>
    internal void BeginSnapshot(long id)
    {
        Active = id;
        Changed(nameof(HasSnapshot));
    }
    internal void ClearPendingSnapshot()
    {
        Active = selectedSnapshot?.Value.Id ?? 0;
        Changed(nameof(HasSnapshot));
    }
    public void OpenSnapshot(long id)
    {
        ReloadHistory();
        SelectedSnapshot = History.FirstOrDefault(x => x.Value.Id == id);
        SnapshotOpened?.Invoke(this, EventArgs.Empty);
    }
    public void ReloadHistory()
    {
        var id = selectedSnapshot?.Value.Id;
        refreshingHistory = true;
        try { Replace(History, Store.Snapshots().Select(x => new SnapshotRow(x))); }
        finally { refreshingHistory = false; }
        selectedSnapshot = History.FirstOrDefault(x => x.Value.Id == id);
        Changed(nameof(SelectedSnapshot));
        Changed(nameof(SnapshotText));
        foreach (var section in sections)
            section.HistoryChanged();
    }
    private void ResetSections()
    {
        foreach (var section in sections)
            section.Reset();
    }
    public async Task RefreshAsync()
    {
        if (Active == 0)
            return;
        foreach (var section in sections)
            await section.RefreshAsync();
        CommandManager.InvalidateRequerySuggested();
    }
    internal static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values)
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
        Store.Dispose();
    }
}
