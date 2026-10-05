using System.Collections.ObjectModel;
using FileViz.Core;
using FileViz.Windows;
namespace FileViz.App.ViewModels;

/// <summary>One file in the cleanup review with its precheck results.</summary>
public sealed class ReviewItem(CleanupSelection selection) : Bindable
{
    public CleanupSelection Selection { get; } = selection;
    public string Path => Selection.Target.Path;
    public string Keeper => Selection.Keeper?.Path ?? "No keeper (manual selection)";
    public string Size => Selection.Target.SizeText;
    private CleanupCheck? check; public CleanupCheck? Check
    {
        get => check; set
        {
            if (Set(ref check, value))
            {
                Changed(nameof(Identity));
                Changed(nameof(Metadata));
                Changed(nameof(Bytes));
                Changed(nameof(Streams));
                Changed(nameof(Status));
                Changed(nameof(Error));
                Changed(nameof(Blocked));
            }
        }
    }
    public string Identity => State(Check?.Identity);
    public string Metadata => State(Check?.Metadata);
    public string Bytes => State(Check?.Bytes);
    public string Streams => State(Check?.Streams);
    public string Status => Check == null ? "Checking" : Check.Ready ? "Ready" : "Blocked";
    public string Error => Check?.Error ?? "";
    public bool Blocked => Check is { Ready: false };
    private static string State(CheckState? state) => state switch { CheckState.Passed => "Passed", CheckState.Failed => "Failed", CheckState.NotChecked => "NotChecked", _ => "Pending" };
}

/// <summary>Cleanup review: prechecks every selected file, then quarantines only the ones that passed.</summary>
public sealed class ReviewViewModel : Bindable
{
    public ObservableCollection<ReviewItem> Items { get; } = [];
    private bool checking = true; public bool Checking
    {
        get => checking; private set
        {
            if (Set(ref checking, value))
                Changed(nameof(CanConfirm));
        }
    }
    public int ReadyCount => Items.Count(x => x.Check?.Ready == true);
    public int BlockedCount => Items.Count(x => x.Blocked);
    public string ReadyText => $"{Format.Count(ReadyCount, "file")} · {Format.Bytes(Items.Where(x => x.Check?.Ready == true).Sum(x => x.Selection.Target.Length))}";
    public string BlockedText => $"{Format.Count(BlockedCount, "file")} · {Format.Bytes(Items.Where(x => x.Blocked).Sum(x => x.Selection.Target.Length))}";
    public string Title => Checking ? $"Checking {Format.Count(Items.Count, "file")}…" : ReadyCount == 0 ? "No files can be moved" : $"Move {Format.Count(ReadyCount, "file")} to quarantine?";
    public string ConfirmLabel => $"Quarantine {Format.Count(ReadyCount, "file")}";
    public bool CanConfirm => !Checking && ReadyCount > 0;
    public bool HasBlocked => BlockedCount > 0;
    public string BlockedSummary => Items.FirstOrDefault(x => x.Blocked) is { } first ? $"{System.IO.Path.GetFileName(first.Path)}: {first.Error}{(BlockedCount > 1 ? $" (+{BlockedCount - 1:N0} more)" : "")} Blocked files stay in place." : "";
    public ReviewViewModel(IEnumerable<CleanupSelection> selections)
    {
        foreach (var selection in selections)
            Items.Add(new(selection));
    }
    public async Task CheckAsync(CancellationToken token)
    {
        foreach (var item in Items)
        {
            token.ThrowIfCancellationRequested();
            item.Check = await Task.Run(() => CleanupService.PrecheckAsync(item.Selection, token), token);
            Changed(nameof(ReadyText));
            Changed(nameof(BlockedText));
        }
        Checking = false;
        Changed(nameof(ReadyCount));
        Changed(nameof(BlockedCount));
        Changed(nameof(Title));
        Changed(nameof(ConfirmLabel));
        Changed(nameof(HasBlocked));
        Changed(nameof(BlockedSummary));
    }
    public CleanupSelection[] ReadySelections => Items.Where(x => x.Check?.Ready == true).Select(x => x.Selection).ToArray();
}
