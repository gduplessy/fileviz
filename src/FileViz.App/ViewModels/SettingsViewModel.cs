using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using FileViz.Core;
namespace FileViz.App.ViewModels;

/// <summary>Persisted preferences, stored in the index database's settings table, and application information.</summary>
public sealed class SettingsViewModel : Bindable
{
    private readonly SessionViewModel session;
    private readonly HomeViewModel home;
    private readonly ExplorerViewModel explorer;
    private readonly DuplicatesViewModel duplicates;
    private bool loading;
    public HomeViewModel Home => home;
    public string[] Themes { get; } = ["Use system setting", "Light", "Dark"];
    public string[] Algorithms { get; } = ["SHA-256", "SHA-1", "MD5"];
    private string theme = "Use system setting"; public string Theme
    {
        get => theme; set
        {
            if (!Set(ref theme, value))
                return;
            App.SetTheme(value switch { "Light" => ThemeMode.Light, "Dark" => ThemeMode.Dark, _ => ThemeMode.System });
            Save("theme", value);
        }
    }
    public bool MapByType
    {
        get => !explorer.ColorByAge; set { if (value) explorer.ColorByAge = false; }
    }
    public bool MapByAge
    {
        get => explorer.ColorByAge; set { if (value) explorer.ColorByAge = true; }
    }
    private bool preferMft = true; public bool PreferMft
    {
        get => preferMft; set
        {
            if (!Set(ref preferMft, value))
                return;
            home.PreferMft = value;
            Save("preferMft", value.ToString());
        }
    }
    private bool administrator; public bool Administrator
    {
        get => administrator; set
        {
            if (!Set(ref administrator, value))
                return;
            session.Administrator = value;
            Save("administrator", value.ToString());
        }
    }
    private string exclusions = ".FileViz-Quarantine"; public string Exclusions
    {
        get => exclusions; set
        {
            if (!Set(ref exclusions, value))
                return;
            home.Exclusions = value;
            Save("exclusions", value);
        }
    }
    private string algorithm = "SHA-256"; public string Algorithm
    {
        get => algorithm; set
        {
            if (!Set(ref algorithm, value))
                return;
            duplicates.Algorithm = value;
            Save("algorithm", value);
        }
    }
    private string preferredFolder = ""; public string PreferredFolder
    {
        get => preferredFolder; set
        {
            if (!Set(ref preferredFolder, value))
                return;
            duplicates.PreferredFolder = value;
            Save("preferredFolder", value);
        }
    }
    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "";
    public string IndexPath => session.DatabasePath;
    public string IndexSize
    {
        get
        {
            try { return Format.Bytes(new FileInfo(session.DatabasePath).Length); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
        }
    }
    public string SnapshotsText => session.History.Count == 0 ? "No snapshots yet" : $"{Format.Count(session.History.Count, "snapshot")} kept · newest #{session.History[0].Value.Id}";
    public ICommand ColorSettingsCommand { get; } = new ActionCommand(() => Process.Start(new ProcessStartInfo("ms-settings:colors") { UseShellExecute = true }));
    public ICommand OpenIndexFolderCommand
    {
        get;
    }
    public ICommand DeleteProfileCommand
    {
        get;
    }
    public SettingsViewModel(SessionViewModel session, HomeViewModel home, ExplorerViewModel explorer, DuplicatesViewModel duplicates)
    {
        this.session = session;
        this.home = home;
        this.explorer = explorer;
        this.duplicates = duplicates;
        OpenIndexFolderCommand = new ActionCommand(() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(session.DatabasePath)}\"") { UseShellExecute = true }));
        DeleteProfileCommand = new ParameterCommand(parameter =>
        {
            if (parameter is not ScanProfile profile)
                return;
            session.Store.DeleteProfile(profile.Name);
            SessionViewModel.Replace(home.Profiles, session.Store.Profiles());
            session.Status = $"Profile \"{profile.Name}\" removed.";
        }, () => !session.Busy);
        explorer.ColorModeChanged += (_, byAge) =>
        {
            Changed(nameof(MapByType));
            Changed(nameof(MapByAge));
            Save("mapColor", byAge ? "age" : "type");
        };
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionViewModel.SelectedSnapshot))
            {
                Changed(nameof(SnapshotsText));
                Changed(nameof(IndexSize));
            }
        };
    }
    /// <summary>Reads saved preferences and applies them to the sections. Missing keys keep defaults.</summary>
    public void Load()
    {
        loading = true;
        try
        {
            var store = session.Store;
            Theme = store.Setting("theme") ?? Theme;
            explorer.ColorByAge = store.Setting("mapColor") == "age";
            PreferMft = bool.TryParse(store.Setting("preferMft"), out var mft) ? mft : PreferMft;
            Administrator = bool.TryParse(store.Setting("administrator"), out var admin) && admin;
            Exclusions = store.Setting("exclusions") ?? Exclusions;
            Algorithm = store.Setting("algorithm") ?? Algorithm;
            PreferredFolder = store.Setting("preferredFolder") ?? PreferredFolder;
            home.PreferMft = PreferMft;
            home.Exclusions = Exclusions;
            session.Administrator = Administrator;
            duplicates.Algorithm = Algorithm;
            duplicates.PreferredFolder = PreferredFolder;
            home.ChangedRoots();
        }
        finally { loading = false; }
        Changed(nameof(SnapshotsText));
        Changed(nameof(IndexSize));
    }
    private void Save(string key, string value)
    {
        if (!loading)
            session.Store.SaveSetting(key, value);
    }
}
