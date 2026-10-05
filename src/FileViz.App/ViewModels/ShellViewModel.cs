using System.ComponentModel;
using System.Windows.Input;
namespace FileViz.App.ViewModels;

/// <summary>One entry in the navigation pane.</summary>
public sealed class NavItem(ShellViewModel shell, string label, string glyph, object content, bool requiresSnapshot, string shortcut) : Bindable
{
    public string Label { get; } = label; public string Glyph { get; } = glyph; public object Content { get; } = content; public string Shortcut { get; } = shortcut;
    public bool RequiresSnapshot { get; } = requiresSnapshot;
    public bool IsEnabled => !RequiresSnapshot || shell.Session.HasSnapshot;
    private string badge = ""; public string Badge
    {
        get => badge; set => Set(ref badge, value);
    }
    public bool IsSelected
    {
        get => shell.SelectedNav == this; set
        {
            if (value)
                shell.SelectedNav = this;
        }
    }
    internal void Refresh()
    {
        Changed(nameof(IsSelected));
        Changed(nameof(IsEnabled));
    }
}

/// <summary>Window-level state: navigation, title bar search, and the sections.</summary>
public sealed class ShellViewModel : Bindable, IDisposable
{
    public SessionViewModel Session
    {
        get;
    }
    public HomeViewModel Home
    {
        get;
    }
    public ExplorerViewModel Explorer
    {
        get;
    }
    public DuplicatesViewModel Duplicates
    {
        get;
    }
    public CompareViewModel Compare
    {
        get;
    }
    public CleanupViewModel Cleanup
    {
        get;
    }
    public DiagnosticsViewModel Diagnostics
    {
        get;
    }
    public SettingsViewModel Settings
    {
        get;
    }
    public IReadOnlyList<NavItem> NavItems
    {
        get;
    }
    public NavItem SettingsNav
    {
        get;
    }
    private NavItem selectedNav; public NavItem SelectedNav
    {
        get => selectedNav; set
        {
            if (!value.IsEnabled || !Set(ref selectedNav, value))
                return;
            foreach (var item in NavItems.Append(SettingsNav))
                item.Refresh();
        }
    }
    public string SearchText { get; set; } = "";
    public ICommand SearchCommand
    {
        get;
    }
    public ICommand GoCommand
    {
        get;
    }
    public ShellViewModel(string? database = null)
    {
        Session = new(database);
        Home = new(Session);
        Cleanup = new(Session);
        Explorer = new(Session, Cleanup);
        Duplicates = new(Session, Cleanup);
        Compare = new(Session);
        Diagnostics = new(Session);
        Settings = new(Session, Home, Explorer, Duplicates);
        Session.Register(Home);
        Session.Register(Explorer);
        Session.Register(Duplicates);
        Session.Register(Compare);
        Session.Register(Cleanup);
        Session.Register(Diagnostics);
        NavItems =
        [
            new(this, "Home", "", Home, false, "Ctrl+1"),
            new(this, "Explorer", "", Explorer, true, "Ctrl+2"),
            new(this, "Duplicates", "", Duplicates, true, "Ctrl+3"),
            new(this, "Compare", "", Compare, true, "Ctrl+4"),
            new(this, "Cleanup", "", Cleanup, false, "Ctrl+5"),
            new(this, "Diagnostics", "", Diagnostics, true, "Ctrl+6"),
        ];
        SettingsNav = new(this, "Settings", "", Settings, false, "");
        selectedNav = NavItems[0];
        Session.PropertyChanged += SessionChanged;
        Session.SnapshotOpened += (_, _) => SelectedNav = NavItems[1];
        Cleanup.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CleanupViewModel.HeldLabel)) NavItems[4].Badge = Cleanup.HeldLabel; };
        Diagnostics.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DiagnosticsViewModel.CountLabel)) NavItems[5].Badge = Diagnostics.CountLabel; };
        SearchCommand = new ActionCommand(() =>
        {
            if (!Session.HasSnapshot)
                return;
            SelectedNav = NavItems[1];
            Explorer.ApplySearch(SearchText.Trim());
        }, () => Session.HasSnapshot && !Session.Busy);
        GoCommand = new ParameterCommand(parameter =>
        {
            if (int.TryParse(parameter as string, out var index) && index >= 0 && index < NavItems.Count)
                SelectedNav = NavItems[index];
        });
    }
    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SessionViewModel.HasSnapshot))
            return;
        foreach (var item in NavItems)
            item.Refresh();
    }
    public async Task InitializeAsync()
    {
        await Home.InitializeAsync();
        Settings.Load();
        Session.ReloadHistory();
        Cleanup.Reload();
        if (Session.History.Count > 0)
        {
            Session.SelectedSnapshot = Session.History[0];
            SelectedNav = NavItems[1];
        }
    }
    public void Dispose() => Session.Dispose();
}
