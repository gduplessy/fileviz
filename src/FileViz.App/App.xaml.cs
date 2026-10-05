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
        var window = new MainWindow(smoke ? Path.Combine(Path.GetFullPath(e.Args[2]), "smoke.db") : null);
        window.Show();
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
                await window.Model.Home.ScanAsync([Path.GetFullPath(e.Args[1])]);
                await window.Model.Duplicates.FindDuplicatesAsync();
                if (!string.Equals(window.Model.Session.CurrentRoot, Path.GetFullPath(e.Args[1]), StringComparison.OrdinalIgnoreCase) || window.Model.Explorer.MapItems.Count == 0)
                    throw new InvalidOperationException("Snapshot root and populated treemap must remain selected after a scan.");
                // Mica is a DWM backdrop and does not appear in RenderTargetBitmap, so captures use the solid base color.
                window.SetResourceReference(Control.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
                async Task Capture(ThemeMode theme, string file)
                {
                    SetTheme(theme);
                    await Task.Delay(500);
                    window.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)((FrameworkElement)window.Content).ActualWidth, (int)((FrameworkElement)window.Content).ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(Path.GetFullPath(e.Args[2]), file));
                    encoder.Save(output);
                }
                await Capture(ThemeMode.Light, "desktop.png");
                await Capture(ThemeMode.Dark, "desktop-dark.png");
                if (Environment.GetEnvironmentVariable("FILEVIZ_SMOKE_SECTIONS") == "1")
                    foreach (var item in window.Model.NavItems.Append(window.Model.SettingsNav))
                    {
                        window.Model.SelectedNav = item;
                        await Capture(ThemeMode.Light, $"section-{item.Label.ToLowerInvariant()}.png");
                        await Capture(ThemeMode.Dark, $"section-{item.Label.ToLowerInvariant()}-dark.png");
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
