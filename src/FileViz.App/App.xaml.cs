using System.Windows;
using System.Windows.Media;
using FileViz.App.Views;
namespace FileViz.App;

public partial class App : Application
{
    private static bool dark;
    private Mutex? instance;
    public static void ToggleTheme()
    {
        dark = !dark;
        ApplyTheme();
    }
    public static void ApplyTheme()
    {
        Current.Resources["Surface"] = new SolidColorBrush(dark ? Color.FromRgb(22, 28, 38) : Color.FromRgb(244, 246, 250));
        Current.Resources["Card"] = new SolidColorBrush(dark ? Color.FromRgb(31, 39, 52) : Colors.White);
        Current.Resources["Ink"] = new SolidColorBrush(dark ? Color.FromRgb(230, 235, 244) : Color.FromRgb(30, 40, 56));
        Current.Resources["Line"] = new SolidColorBrush(dark ? Color.FromRgb(56, 69, 85) : Color.FromRgb(217, 223, 231));
        Current.Resources[SystemColors.WindowBrushKey] = Current.Resources["Card"];
        Current.Resources[SystemColors.WindowTextBrushKey] = Current.Resources["Ink"];
        Current.Resources[SystemColors.ControlBrushKey] = Current.Resources["Card"];
        Current.Resources[SystemColors.ControlTextBrushKey] = Current.Resources["Ink"];
        Current.Resources[SystemColors.GrayTextBrushKey] = new SolidColorBrush(dark ? Color.FromRgb(156, 169, 186) : Color.FromRgb(104, 115, 130));
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyTheme();
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
            window.ContentRendered += async (_, _) => { await window.Ready.Task; foreach (var drive in window.Model.Drives) drive.Selected = false; window.Model.ExtraRoots = string.Join(Environment.NewLine, System.Text.Json.JsonSerializer.Deserialize<string[]>(e.Args[1]) ?? []); window.Model.ChangedRoots(); };
        if (smoke)
            window.ContentRendered += async (_, _) =>
        {
            try
            {
                await window.Ready.Task;
                window.Model.PreferMft = false;
                window.Model.Drives.Clear();
                window.Model.Drives.Add(new FileViz.App.ViewModels.DriveRow(Path.GetFullPath(e.Args[1]), "Fixture · local sample data", "Disposable 40 MiB validation fixture", true));
                await window.Model.ScanAsync([Path.GetFullPath(e.Args[1])]);
                await window.Model.FindDuplicatesAsync();
                await Task.Delay(500);
                window.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)((FrameworkElement)window.Content).ActualWidth + 48, (int)((FrameworkElement)window.Content).ActualHeight + 48, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var output = File.Create(Path.Combine(Path.GetFullPath(e.Args[2]), "desktop.png")))
                    encoder.Save(output);
                ToggleTheme();
                await Task.Delay(100);
                window.UpdateLayout();
                var darkBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)((FrameworkElement)window.Content).ActualWidth + 48, (int)((FrameworkElement)window.Content).ActualHeight + 48, 96, 96, PixelFormats.Pbgra32);
                darkBitmap.Render(window);
                var darkEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                darkEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(darkBitmap));
                using (var darkOutput = File.Create(Path.Combine(Path.GetFullPath(e.Args[2]), "desktop-dark.png")))
                    darkEncoder.Save(darkOutput);
                File.WriteAllText(Path.Combine(Path.GetFullPath(e.Args[2]), "smoke.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    window.Model.Status,
                    window.Model.SummaryText,
                    Rows = window.Model.Files.Count,
                    DuplicateRows = window.Model.Duplicates.Count,
                    PeakWorkingSet = System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64,
                    Snapshots = window.Model.History.Count,
                    Errors = window.Model.Errors.Count,
                    RenderedVisibleWindow = true
                }));
                Shutdown(window.Model.Files.Count > 0 && window.Model.Errors.Count == 0 && window.Model.Duplicates.Count >= 2 ? 0 : 1);
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
