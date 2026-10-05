using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using FileViz.App.Services;
using FileViz.Core;
namespace FileViz.App.ViewModels;

/// <summary>Duplicate analysis scope, groups, and keeper selection.</summary>
public sealed class DuplicatesViewModel : Bindable, ISnapshotSection
{
    private readonly SessionViewModel session;
    private int page;
    public SessionViewModel Session => session;
    public CleanupViewModel Cleanup
    {
        get;
    }
    public ObservableCollection<DuplicateScopeRow> DuplicateRoots { get; } = [];
    public ObservableCollection<DuplicateRow> Duplicates { get; } = [];
    public string[] Algorithms { get; } = ["SHA-256", "SHA-1", "MD5", "Name"];
    public string Algorithm { get; set; } = "SHA-256"; public bool CrossDrive { get; set; } = false; public string PreferredFolder { get; set; } = "";
    private string duplicateText = "Choose content matching for verified groups. Names alone are candidates."; public string DuplicateText
    {
        get => duplicateText; private set => Set(ref duplicateText, value);
    }
    public string PageLabel => $"Groups page {page + 1}";
    public ICommand DuplicateCommand
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
    public DuplicatesViewModel(SessionViewModel session, CleanupViewModel cleanup)
    {
        this.session = session;
        Cleanup = cleanup;
        DuplicateCommand = new ActionCommand(() => session.Run(FindDuplicatesAsync), () => session.HasSnapshot && !session.Busy);
        PreviousCommand = new ActionCommand(() => { page = Math.Max(0, page - 1); RefreshDuplicates(); }, () => page > 0 && !session.Busy);
        NextCommand = new ActionCommand(() => { page++; RefreshDuplicates(); }, () => Duplicates.Count == 250 && !session.Busy);
    }
    public void Reset() => page = 0;
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
    public async Task FindDuplicatesAsync()
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
            var algorithm = Algorithm;
            var preferred = PreferredFolder;
            var elevated = session.Administrator;
            await Task.Run(() => DuplicateCoordinator.FindAsync(database, id, snapshots, roots, algorithm, preferred, elevated, new Progress<string>(text => Application.Current.Dispatcher.Invoke(() => session.Status = text)), token));
            page = 0;
            RefreshDuplicates();
            session.Status = "Duplicate analysis complete. Cleanup performs fresh byte and stream verification.";
        }
        finally { session.EndWork(); }
    }
    private void RefreshDuplicates()
    {
        if (session.Active == 0)
            return;
        SessionViewModel.Replace(Duplicates, session.Store.Duplicates(session.Active, page));
        Changed(nameof(PageLabel));
        DuplicateText = $"Main-stream content candidates · {Format.Bytes(session.Store.DuplicatePotential(session.Active))} potential duplicate logical bytes; allocation and named streams require fresh verification. Suggested keepers are not automatic selections.";
    }
    public FileEntry? KeeperFor(DuplicateRow row, IEnumerable<string> removals) => session.Store.DuplicateGroup(session.Active, row.GroupId).FirstOrDefault(x => !removals.Contains(x.Path, StringComparer.Ordinal));
}
