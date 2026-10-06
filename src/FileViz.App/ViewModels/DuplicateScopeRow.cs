namespace FileViz.App.ViewModels;

public sealed class DuplicateScopeRow(long snapshot, string root, bool selected, string state = "") : Bindable
{
    public long Snapshot { get; } = snapshot; public string Root { get; } = root;
    public string State { get; } = state;
    public string Label => $"{Root} · scan #{Snapshot}";
    private bool chosen = selected; public bool Selected
    {
        get => chosen; set => Set(ref chosen, value);
    }
}
