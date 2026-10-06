using System.Windows;
using FileViz.Core;
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
        var smoke = e.Args.Length == 3 && e.Args[0] == "--smoke";
        var handlingFailure = false;
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            if (smoke)
            {
                File.WriteAllText(Path.Combine(Path.GetFullPath(e.Args[2]), "smoke-error.txt"), args.Exception.ToString());
                Shutdown(1);
                return;
            }
            // A modal error pumps layout again; a failing template must not recursively open dialogs.
            if (handlingFailure) { Shutdown(1); return; }
            handlingFailure = true;
            try { MessageBox.Show(args.Exception.Message, "FileViz", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { handlingFailure = false; }
        };
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
                async Task Capture(ThemeMode theme, string file, Window? target = null, bool wait = true)
                {
                    target ??= window;
                    SetTheme(theme);
                    if (wait) await Task.Delay(500);
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
                var duplicateScope = Environment.GetEnvironmentVariable("FILEVIZ_SMOKE_DUPLICATE_SCOPE") == "1";
                if (duplicateScope)
                {
                    var original = window.Model.Session.Active;
                    var originalRoot = Path.GetFullPath(e.Args[1]);
                    // Keep fixtures outside the database directory, which scans intentionally exclude.
                    var otherRoot = Path.Combine(Path.GetDirectoryName(originalRoot)!, "another-drive-fixture");
                    Directory.CreateDirectory(otherRoot);
                    File.WriteAllText(Path.Combine(otherRoot, "duplicate-a.bin"), "Other scope: same bytes");
                    File.WriteAllText(Path.Combine(otherRoot, "duplicate-b.bin"), "Other scope: same bytes");
                    await window.Model.Home.ScanAsync([otherRoot]);
                    var other = window.Model.Session.Active;
                    window.Model.SelectedNav = window.Model.NavItems[2];
                    var picker = window.Model.Duplicates;
                    var originalScope = picker.AvailableScopes.First(x => x.Snapshot == original && x.Root == originalRoot);
                    var otherScope = picker.AvailableScopes.First(x => x.Snapshot == other && x.Root == otherRoot);
                    picker.SelectedScope = originalScope;
                    if (window.Model.Session.Active != original || window.Model.Session.CurrentRoot != originalRoot || window.Model.SelectedNav.Content != picker)
                        throw new InvalidOperationException("Drive selection must change snapshot/root without leaving Duplicates.");
                    picker.SelectedScope = otherScope;
                    await picker.FindDuplicatesAsync();
                    if (picker.Duplicates.Count != 2 || picker.Duplicates.Any(x => !Paths.Within(x.Entry.Path, otherRoot)))
                        throw new InvalidOperationException("Single-drive duplicate results must come only from the chosen scope.");
                    var busy = picker.FindDuplicatesAsync();
                    picker.SelectedScope = originalScope;
                    if (picker.SelectedScope != otherScope || window.Model.Session.Active != other)
                        throw new InvalidOperationException("Drive selection must be locked during analysis.");
                    window.Model.Session.CancelCommand.Execute(null);
                    await busy;
                    picker.SelectedScope = originalScope;
                    picker.CrossDrive = true;
                    foreach (var scope in picker.DuplicateRoots) scope.Selected = true;
                    await picker.FindDuplicatesAsync();
                    if (!picker.Duplicates.Any(x => Paths.Within(x.Entry.Path, originalRoot)) || !picker.Duplicates.Any(x => Paths.Within(x.Entry.Path, otherRoot)))
                        throw new InvalidOperationException("Cross-drive selection must include both selected inventories.");
                    await Capture(ThemeMode.Light, "duplicate-drive-selection.png");
                    await Capture(ThemeMode.Dark, "duplicate-drive-selection-dark.png");
                    picker.CrossDrive = false;
                    await picker.FindDuplicatesAsync();
                    if (picker.Duplicates.Any(x => !Paths.Within(x.Entry.Path, originalRoot)))
                        throw new InvalidOperationException("Returning to one drive must exclude the other drive.");
                    // A reloaded historical snapshot remains represented when a newer scan exists.
                    await window.Model.Home.ScanAsync([otherRoot]);
                    window.Model.Session.SelectedSnapshot = window.Model.Session.History.First(x => x.Value.Id == other);
                    if (picker.SelectedScope?.Snapshot != other || picker.SelectedScope.Root != otherRoot)
                        throw new InvalidOperationException("An explicitly reopened historical scope must remain selectable.");
                    picker.SelectedScope = picker.AvailableScopes.First(x => x.Snapshot == original && x.Root == originalRoot);
                    window.Model.SelectedNav = window.Model.NavItems[2];
                }
                var duplicateActivity = Environment.GetEnvironmentVariable("FILEVIZ_SMOKE_DUPLICATE_ACTIVITY") == "1";
                if (duplicateActivity)
                {
                    window.Model.SelectedNav = window.Model.NavItems[2];
                    var analysis = window.Model.Duplicates.FindDuplicatesAsync();
                    if (!window.Model.Duplicates.IsAnalyzing || !window.Model.Duplicates.HasActivity || window.Model.Duplicates.NoRun
                        || !window.Model.Session.Status.StartsWith("Analyzing content", StringComparison.Ordinal))
                        throw new InvalidOperationException("Duplicate activity must be visible immediately, before preparation finishes.");
                    await Capture(ThemeMode.Light, "duplicate-active.png", wait: false);
                    await analysis;
                    await window.Model.Duplicates.FindDuplicatesAsync();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(window.Model.Duplicates.ActivityCounts, @"[1-9][0-9,]* cached"))
                        throw new InvalidOperationException("Revalidated hash cache hits must appear in activity counters.");
                    var previousRun = window.Model.Duplicates.Run;
                    var cancelled = window.Model.Duplicates.FindDuplicatesAsync();
                    window.Model.Session.CancelCommand.Execute(null);
                    await cancelled;
                    if (window.Model.Duplicates.IsAnalyzing || window.Model.Duplicates.AnalysisHeading != "Duplicate analysis cancelled"
                        || window.Model.Duplicates.Run != previousRun || window.Model.Session.Busy)
                        throw new InvalidOperationException("Cancelled duplicate analysis must leave the UI idle and retain previous results.");
                    await Capture(ThemeMode.Dark, "duplicate-cancelled.png");
                    await window.Model.Duplicates.FindDuplicatesAsync();
                    await Capture(ThemeMode.Dark, "duplicate-complete.png");
                }
                else await window.Model.Duplicates.FindDuplicatesAsync();
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
                var photos = Environment.GetEnvironmentVariable("FILEVIZ_SMOKE_PHOTOS") == "1";
                if (photos)
                {
                    void PhotoStage(string stage) => File.AppendAllText(Path.Combine(Path.GetFullPath(e.Args[2]), "photo-stages.txt"), stage + Environment.NewLine);
                    PhotoStage("Preparing disposable image fixture");
                    var original = window.Model.Session.Active;
                    var photoRoot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!, "photo-fixture");
                    Directory.CreateDirectory(photoRoot);
                    void WriteImage(string name, int width, int height)
                    {
                        var drawing = new DrawingVisual();
                        using (var context = drawing.RenderOpen())
                        {
                            context.DrawRectangle(new LinearGradientBrush(Colors.DarkSlateBlue, Colors.Teal, 45), null, new Rect(0, 0, width, height));
                            context.DrawEllipse(Brushes.Gold, null, new Point(width * .75, height * .3), width * .12, width * .12);
                            context.DrawRectangle(Brushes.DarkSlateGray, null, new Rect(0, height * .7, width, height * .3));
                        }
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
                        System.Windows.Media.Imaging.BitmapEncoder encoder = name.EndsWith(".jpg", StringComparison.Ordinal) ? new System.Windows.Media.Imaging.JpegBitmapEncoder() : new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(photoRoot, name)); encoder.Save(file);
                    }
                    WriteImage("landscape-small.png", 400, 300);
                    WriteImage("portrait-small.jpg", 320, 900);
                    WriteImage("boundary.png", 1280, 720);
                    WriteImage("large.png", 1600, 1200);
                    File.WriteAllText(Path.Combine(photoRoot, "unreadable.png"), "Not an image; must never become a removal candidate.");
                    await window.Model.Home.ScanAsync([photoRoot]);
                    PhotoStage("Photo inventory scanned");
                    window.Model.SelectedNav = window.Model.NavItems.First(x => x.Label == "Photos");
                    PhotoStage("Photos navigation selected");
                    var model = window.Model.Photos;
                    await model.AnalyzeAsync();
                    PhotoStage("Photo dimensions analyzed");
                    if (model.Photos.Count != 2 || model.Photos.Any(x => x.Photo.ShortEdge >= 720) || model.BuildSelections().Length != 0)
                        throw new InvalidOperationException("Photo thresholds must respect orientation and never select removals automatically.");
                    model.MinimumMegapixels = "1"; model.ApplyFilter(); await model.RefreshAsync();
                    if (model.Photos.Count != 3) throw new InvalidOperationException("Either enabled photo threshold must select the boundary image.");
                    model.MinimumMegapixels = "0"; model.Search = "landscape"; model.ApplyFilter(); await model.RefreshAsync();
                    if (model.Photos.Count != 1) throw new InvalidOperationException("Photo name/path filtering must narrow resolution matches.");
                    model.Search = ""; model.ApplyFilter(); await model.RefreshAsync();
                    model.Selected = model.Photos.First(x => x.Name == "portrait-small.jpg");
                    await model.LoadPreviewAsync();
                    PhotoStage("Preview loaded");
                    if (model.Preview == null || Math.Max(model.Preview.PixelWidth, model.Preview.PixelHeight) > 512)
                        throw new InvalidOperationException("Photo preview must be bounded and rendered.");
                    model.Selected.Remove = true;
                    await Capture(ThemeMode.Light, "photos-review.png");
                    await Capture(ThemeMode.Dark, "photos-review-dark.png");
                    var selections = model.BuildSelections();
                    var review = new FileViz.App.ViewModels.ReviewViewModel(selections);
                    await review.CheckAsync(CancellationToken.None);
                    if (review.ReadySelections.Length != 1 || selections[0].Keeper != null)
                        throw new InvalidOperationException("Photo removal must use explicit manual cleanup prechecks.");
                    model.ErrorsOnly = true; await model.RefreshAsync();
                    if (model.Photos.Count != 1 || model.Photos[0].CanRemove) throw new InvalidOperationException("Unreadable photos must stay unselectable.");
                    model.ErrorsOnly = false; await model.RefreshAsync();
                    // Cancelling a repeated analysis retains cached work and restores controls.
                    var analysis = model.AnalyzeAsync();
                    if (!model.IsAnalyzing || !model.HasActivity || !window.Model.Session.Busy) throw new InvalidOperationException("Photo analysis must show immediate activity.");
                    var activePhotoScope = model.SelectedScope;
                    model.SelectedScope = model.AvailableScopes.First(x => x.Snapshot == original);
                    if (model.SelectedScope != activePhotoScope) throw new InvalidOperationException("Photo scope edits must be blocked while busy.");
                    window.Model.Session.CancelCommand.Execute(null); await analysis;
                    if (model.IsAnalyzing || window.Model.Session.Busy || model.Photos.Count != 2) throw new InvalidOperationException("Photo cancellation must retain completed results.");
                    model.Selected = model.Photos[0]; model.Selected.Remove = true;
                    var selected = model.BuildSelections();
                    var previousBytes = File.ReadAllBytes(selected[0].Target.Path);
                    await window.Model.Cleanup.CleanupAsync(selected);
                    var moved = window.Model.Cleanup.CleanupHistory.First(x => x.Original == selected[0].Target.Path && x.State == "Quarantined");
                    window.Model.Cleanup.Restore(moved);
                    if (!File.ReadAllBytes(selected[0].Target.Path).SequenceEqual(previousBytes)) throw new InvalidOperationException("Disposable photo restore must retain all original bytes.");
                    model.SelectedScope = model.AvailableScopes.First(x => x.Snapshot == original);
                    await window.Model.Session.RefreshAsync();
                    if (model.Photos.Count != 0 || window.Model.SelectedNav.Label != "Photos") throw new InvalidOperationException("Photo scope selection must isolate cached matches without leaving Photos.");
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
                    DuplicateActivityValidated = duplicateActivity,
                    DuplicateScopeValidated = duplicateScope,
                    PhotosValidated = photos,
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
