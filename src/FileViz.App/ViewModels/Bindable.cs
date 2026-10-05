using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
namespace FileViz.App.ViewModels;

public abstract class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Changed(name);
        return true;
    }
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class ActionCommand(Action action, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => action();
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}
public sealed class ParameterCommand(Action<object?> action, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => action(parameter);
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}
/// <summary>A drive or folder that can be scanned, with capacity when known.</summary>
public sealed class DriveRow(string path, string label, string detail, bool selected = false, long used = 0, long total = 0, string engine = "") : Bindable
{
    public string Path { get; } = path; public string Label { get; } = label; public string Detail { get; } = detail;
    public string Engine { get; } = engine;
    public bool HasCapacity => total > 0;
    public double UsedPercent => total > 0 ? 100d * used / total : 0;
    public bool NearlyFull => total > 0 && used > 0.85 * total;
    public string UsageText => total > 0 ? $"{FileViz.Core.Format.Bytes(used)} of {FileViz.Core.Format.Bytes(total)}" : "";
    private bool chosen = selected; public bool Selected
    {
        get => chosen; set => Set(ref chosen, value);
    }
    private string lastState = ""; public string LastState
    {
        get => lastState; set => Set(ref lastState, value);
    }
}
public sealed record SnapshotRow(FileViz.Core.Snapshot Value)
{
    public string Label => $"#{Value.Id} · {Value.State} · {DateTime.Parse(Value.Started).ToLocalTime():g} · {Value.Files:N0} files";
    public string StartedText => DateTime.Parse(Value.Started).ToLocalTime().ToString("g");
    public string RootsText => string.Join(", ", System.Text.Json.JsonSerializer.Deserialize<string[]>(Value.Roots) ?? []);
    public string LogicalText => FileViz.Core.Format.Bytes(Value.Logical);
    public string[] Roots => System.Text.Json.JsonSerializer.Deserialize<string[]>(Value.Roots) ?? [];
}
