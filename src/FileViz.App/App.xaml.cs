using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FileViz.App.Views;
namespace FileViz.App;

public partial class App : Application
{
    private Mutex? instance;
    /// <summary>Switches between system, light and dark Fluent themes. Mica and the system accent follow automatically.</summary>
    public static void SetTheme(ThemeMode mode) => Current.ThemeMode = mode;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "FileViz", MessageBoxButton.OK, MessageBoxImage.Error); args.Handled = true; };
        var smoke = e.Args.Length == 3 && e.Args[0] == "--smoke";
        if (!smoke)
        {
            var wait = Array.IndexOf(e.Args, "--wait-parent");
            if (wait >= 0 && wait + 1 < e.Args.Length && int.TryParse(e.Args[wait + 1], out var parent))
            {
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(parent);
                    if (!process.WaitForExit(10000))
                    {
                        Shutdown(1);
                        return;
                    }
                }
                catch (ArgumentException) { }
            }
            bool created;
            try
            {
                instance = new Mutex(true, "Global\\FileViz-" + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value, out created);
            }
            catch (UnauthorizedAccessException) { MessageBox.Show("FileViz is already running with higher permissions for this Windows user.", "FileViz"); Shutdown(); return; }
            if (!created)
            {
                MessageBox.Show("FileViz is already running for this Windows user.", "FileViz");
                Shutdown();
                return;
            }
        }
        var databaseArgument = Array.IndexOf(e.Args, "--database");
        var database = databaseArgument >= 0 && databaseArgument + 1 < e.Args.Length ? Path.GetFullPath(e.Args[databaseArgument + 1]) : null;
        if (database?.StartsWith("\\\\", StringComparison.Ordinal) == true)
        {
            MessageBox.Show("The snapshot database must be stored on a local drive.", "FileViz");
            Shutdown(2);
            return;
        }
        var window = new MainWindow(smoke ? Path.Combine(Path.GetFullPath(e.Args[2]), "smoke.db") : database);
        window.Show();
        var rebuildArgument = Array.IndexOf(e.Args, "--rebuild-snapshot");
        if (rebuildArgument >= 0 && rebuildArgument + 1 < e.Args.Length && long.TryParse(e.Args[rebuildArgument + 1], out var rebuild) && rebuild > 0)
            window.ContentRendered += async (_, _) => { await window.Ready.Task; await window.Model.Home.RebuildViewsAsync(rebuild); };
        if (e.Args.Length >= 2 && e.Args[0] == "--roots")
            window.ContentRendered += async (_, _) => { await window.Ready.Task; foreach (var drive in window.Model.Home.Drives) drive.Selected = false; window.Model.Home.ExtraRoots = string.Join(Environment.NewLine, System.Text.Json.JsonSerializer.Deserialize<string[]>(e.Args[1]) ?? []); window.Model.Home.ChangedRoots(); };
        if (smoke)
            window.ContentRendered += async (_, _) =>
        {
            try
            {
                await window.Ready.Task;
                window.Model.Home.PreferMft = false;
                window.Model.Home.Drives.Clear();
                window.Model.Home.Drives.Add(new FileViz.App.ViewModels.DriveRow(Path.GetFullPath(e.Args[1]), "Fixture · local sample data", "Disposable 40 MiB validation fixture", true));
                // Mica is a DWM backdrop and does not appear in RenderTargetBitmap, so captures use the solid base color.
                window.SetResourceReference(Control.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
                async Task Capture(ThemeMode theme, string file, Window? target = null)
                {
                    target ??= window;
                    SetTheme(theme);
                    await Task.Delay(500);
                    target.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)((FrameworkElement)target.Content).ActualWidth, (int)((FrameworkElement)target.Content).ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(target);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(Path.GetFullPath(e.Args[2]), file));
                    encoder.Save(output);
                }
                var sections = Environment.GetEnvironmentVariable("FILEVIZ_SMOKE_SECTIONS") == "1";
                if (sections)
                {
                    await Capture(ThemeMode.Light, "section-firstrun.png");
                    await Capture(ThemeMode.Dark, "section-firstrun-dark.png");
                }
                var scanStages = new HashSet<string>(StringComparer.Ordinal);
                window.Model.Home.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(FileViz.App.ViewModels.HomeViewModel.ProgressText))
                        scanStages.Add(window.Model.Home.ProgressText);
                };
                await window.Model.Home.ScanAsync([Path.GetFullPath(e.Args[1])]);
                if (!scanStages.Contains("Identifying hard links; preparing identity index")
                    || !scanStages.Any(x => x.StartsWith("Preparing file views", StringComparison.Ordinal))
                    || !scanStages.Contains("Saving snapshot") || window.Model.Home.ScanEngine != "Finalizing")
                    throw new InvalidOperationException("Scan progress must expose hard-link refresh and snapshot finalization.");
                var rebuilt = Environment.GetEnvironmentVariable("FILEVIZ_SMOKE_RECOVERY") == "1";
                if (rebuilt)
                {
                    var saved = window.Model.Session.Active;
                    var before = window.Model.Session.Store.GetSummary(saved);
                    File.WriteAllText(Path.Combine(Path.GetFullPath(e.Args[1]), "not-in-saved-inventory.tmp"), "A rescan would include this file.");
                    window.Model.Session.Store.SetState(saved, "Interrupted");
                    await window.Model.Home.RebuildViewsAsync(saved);
                    if (window.Model.Session.Store.GetSummary(saved) != before || window.Model.Session.History.First(x => x.Value.Id == saved).Value.State != "Interrupted")
                        throw new InvalidOperationException("Rebuilding must preserve saved inventory and interrupted coverage without rescanning.");
                }
                await window.Model.Duplicates.FindDuplicatesAsync();
                if (!string.Equals(window.Model.Session.CurrentRoot, Path.GetFullPath(e.Args[1]), StringComparison.OrdinalIgnoreCase) || window.Model.Explorer.MapItems.Count == 0)
                    throw new InvalidOperationException("Snapshot root and populated treemap must remain selected after a scan.");
                await Capture(ThemeMode.Light, "desktop.png");
                await Capture(ThemeMode.Dark, "desktop-dark.png");
                if (sections)
                    foreach (var item in window.Model.NavItems.Append(window.Model.SettingsNav))
                    {
                        window.Model.SelectedNav = item;
                        await Capture(ThemeMode.Light, $"section-{item.Label.ToLowerInvariant()}.png");
                        await Capture(ThemeMode.Dark, $"section-{item.Label.ToLowerInvariant()}-dark.png");
                        if (item.Content == window.Model.Explorer)
                        {
                            window.Model.Explorer.ColorByAge = true;
                            await Capture(ThemeMode.Light, "section-explorer-age.png");
                            await Capture(ThemeMode.Dark, "section-explorer-age-dark.png");
                            window.Model.Explorer.ColorByAge = false;
                        }
                    }
                if (sections)
                {
                    // The review window prechecks the fixture's duplicate pair; shown non-modally so it can be captured.
                    window.Model.SelectedNav = window.Model.NavItems[2];
                    window.Model.Duplicates.SelectAllCommand.Execute(null);
                    await Capture(ThemeMode.Light, "section-duplicates-selected.png");
                    var review = new FileViz.App.Views.ReviewWindow(new(window.Model.Duplicates.BuildSelections() ?? [])) { Owner = window };
                    review.SetResourceReference(Control.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
                    review.Show();
                    await Task.Delay(1500);
                    await Capture(ThemeMode.Light, "dialog-review.png", review);
                    await Capture(ThemeMode.Dark, "dialog-review-dark.png", review);
                    review.Close();
                    window.Model.Duplicates.ClearCommand.Execute(null);
                    // A second snapshot of the unchanged fixture exercises the Compare layout.
                    await window.Model.Home.ScanAsync([Path.GetFullPath(e.Args[1])]);
                    window.Model.Compare.CompareBefore = window.Model.Session.History[1];
                    window.Model.SelectedNav = window.Model.NavItems[3];
                    window.Model.Compare.CompareCommand.Execute(null);
                    await Task.Delay(1500);
                    await Capture(ThemeMode.Light, "section-compare-result.png");
                    await Capture(ThemeMode.Dark, "section-compare-result-dark.png");
                    await window.Model.Duplicates.FindDuplicatesAsync();
                }
                File.WriteAllText(Path.Combine(Path.GetFullPath(e.Args[2]), "smoke.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    window.Model.Session.Status,
                    window.Model.Explorer.SummaryText,
                    Rows = window.Model.Explorer.Files.Count,
                    DuplicateRows = window.Model.Duplicates.Duplicates.Count,
                    PeakWorkingSet = System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64,
                    Snapshots = window.Model.Session.History.Count,
                    Errors = window.Model.Diagnostics.Errors.Count,
                    RebuiltSavedInventory = rebuilt,
                    RenderedVisibleWindow = true
                }));
                Shutdown(window.Model.Explorer.Files.Count > 0 && window.Model.Diagnostics.Errors.Count == 0 && window.Model.Duplicates.Duplicates.Count >= 2 ? 0 : 1);
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(Path.GetFullPath(e.Args[2]), "smoke-error.txt"), error.ToString()); Shutdown(1); }
        };
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (instance != null)
        {
            try
            {
                instance.ReleaseMutex();
            }
            catch (ApplicationException) { }
            instance.Dispose();
        }
        base.OnExit(e);
    }
}
