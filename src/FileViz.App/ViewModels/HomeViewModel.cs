using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using Microsoft.Win32;
namespace FileViz.App.ViewModels;

/// <summary>Scan scope, options, profiles, and the scan itself.</summary>
public sealed class HomeViewModel : Bindable, ISnapshotSection
{
    private readonly SessionViewModel session;
    private readonly System.Windows.Threading.DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime scanStarted;
    public SessionViewModel Session => session;
    public ObservableCollection<DriveRow> Drives { get; } = [];
    public ObservableCollection<ScanProfile> Profiles { get; } = [];
    public string ExtraRoots { get; set; } = "";
    private string exclusions = ".FileViz-Quarantine"; public string Exclusions
    {
        get => exclusions; set => Set(ref exclusions, value);
    }
    private bool preferMft = true; public bool PreferMft
    {
        get => preferMft; set => Set(ref preferMft, value);
    }
    public string ProfileName { get; set; } = "My scan";
    private ScanProfile? selectedProfile; public ScanProfile? SelectedProfile
    {
        get => selectedProfile; set
        {
            if (Set(ref selectedProfile, value) && value != null)
            {
                foreach (var drive in Drives)
                    drive.Selected = false;
                ExtraRoots = string.Join(Environment.NewLine, value.Roots);
                Exclusions = string.Join(Environment.NewLine, value.Exclusions);
                PreferMft = value.PreferMft;
                ProfileName = value.Name;
                Changed(nameof(ExtraRoots));
                Changed(nameof(ProfileName));
                Changed(nameof(ScanButtonText));
            }
        }
    }
    /// <summary>No snapshot exists yet: Home shows the first-run layout.</summary>
    public bool IsFirstRun => session.History.Count == 0 && !session.Scanning;
    public bool HasHistory => !IsFirstRun;
    private string scanTarget = ""; public string ScanTarget
    {
        get => scanTarget; private set => Set(ref scanTarget, value);
    }
    private string scanEngine = ""; public string ScanEngine
    {
        get => scanEngine; private set => Set(ref scanEngine, value);
    }
    public string ScanWorker => session.Administrator ? "Read-only elevated worker" : "Read-only worker";
    private long scanFiles; public long ScanFiles
    {
        get => scanFiles; private set => Set(ref scanFiles, value);
    }
    private string scanBytes = "0 B"; public string ScanBytes
    {
        get => scanBytes; private set => Set(ref scanBytes, value);
    }
    private string scanElapsed = "0:00"; public string ScanElapsed
    {
        get => scanElapsed; private set => Set(ref scanElapsed, value);
    }
    private bool progressKnown; public bool ProgressKnown
    {
        get => progressKnown; private set => Set(ref progressKnown, value);
    }
    private double progressValue; public double ProgressValue
    {
        get => progressValue; private set => Set(ref progressValue, value);
    }
    private string progressText = ""; public string ProgressText
    {
        get => progressText; private set => Set(ref progressText, value);
    }
    public string ScanButtonText
    {
        get
        {
            var count = Drives.Count(x => x.Selected) + Lines(ExtraRoots).Length;
            return count switch { 0 => "Scan", 1 => "Scan 1 selected root", _ => $"Scan {count} selected roots" };
        }
    }
    public ICommand ScanCommand
    {
        get;
    }
    public ICommand RescanCommand
    {
        get;
    }
    public ICommand AddRootCommand
    {
        get;
    }
    public ICommand SaveProfileCommand
    {
        get;
    }
    public ICommand ElevateCommand
    {
        get;
    }
    public HomeViewModel(SessionViewModel session)
    {
        this.session = session;
        clock.Tick += (_, _) => ScanElapsed = (DateTime.UtcNow - scanStarted).ToString(@"m\:ss");
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionViewModel.Scanning))
            {
                Changed(nameof(IsFirstRun));
                Changed(nameof(HasHistory));
            }
            if (e.PropertyName == nameof(SessionViewModel.Administrator))
                Changed(nameof(ScanWorker));
        };
        Drives.CollectionChanged += (_, e) =>
        {
            foreach (var row in e.NewItems?.OfType<DriveRow>() ?? [])
                row.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(DriveRow.Selected)) Changed(nameof(ScanButtonText)); };
            Changed(nameof(ScanButtonText));
        };
        ScanCommand = new ActionCommand(() => session.Run(() => ScanAsync()), () => !session.Busy);
        RescanCommand = new ActionCommand(() => session.Run(() => ScanAsync(session.SnapshotRoots.ToArray())), () => !session.Busy && session.SnapshotRoots.Count > 0);
        AddRootCommand = new ActionCommand(AddRoot, () => !session.Busy);
        SaveProfileCommand = new ActionCommand(() =>
        {
            if (string.IsNullOrWhiteSpace(ProfileName))
                throw new ArgumentException("Enter a profile name.");
            session.Store.SaveProfile(new(ProfileName, Roots(), Excluded(), PreferMft));
            SessionViewModel.Replace(Profiles, session.Store.Profiles());
            session.Status = "Scan profile saved.";
        }, () => !session.Busy);
        ElevateCommand = new ActionCommand(() =>
        {
            if (Native.IsElevated)
            {
                session.Status = "FileViz is already running as administrator.";
                return;
            }
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            info.ArgumentList.Add("--roots");
            info.ArgumentList.Add(JsonSerializer.Serialize(Roots()));
            info.ArgumentList.Add("--wait-parent");
            info.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(info);
            Application.Current.Shutdown();
        }, () => !session.Busy);
    }
    public async Task InitializeAsync()
    {
        var rows = await Task.Run(() =>
        {
            var result = new List<DriveRow>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    var letter = drive.Name.TrimEnd('\\');
                    if (drive.DriveType == DriveType.Network)
                    {
                        result.Add(new(drive.Name, $"Network ({letter})", "Network share", engine: "Directory scan · Windows credentials"));
                        continue;
                    }
                    if (!drive.IsReady)
                        continue;
                    var name = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
                    var kind = drive.DriveType == DriveType.Removable ? "removable" : "local";
                    var engine = drive.DriveFormat == "NTFS" && drive.DriveType == DriveType.Fixed ? "Raw MFT with administrator scan" : "Directory scan";
                    result.Add(new(drive.Name, $"{name} ({letter})", $"{drive.DriveFormat} · {kind}", drive.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase), drive.TotalSize - drive.TotalFreeSpace, drive.TotalSize, engine));
                }
                catch (IOException) { result.Add(new(drive.Name, drive.Name, "Unavailable")); }
                catch (UnauthorizedAccessException) { result.Add(new(drive.Name, drive.Name, "Access denied")); }
            }
            return result;
        });
        SessionViewModel.Replace(Drives, rows);
        SessionViewModel.Replace(Profiles, session.Store.Profiles());
        HistoryChanged();
    }
    public void ChangedRoots()
    {
        Changed(nameof(ExtraRoots));
        Changed(nameof(ScanButtonText));
    }
    public void Reset()
    {
    }
    public Task RefreshAsync() => Task.CompletedTask;
    /// <summary>Shows each drive's most recent snapshot state and switches between first-run and normal layouts.</summary>
    public void HistoryChanged()
    {
        foreach (var drive in Drives)
        {
            var latest = session.History.FirstOrDefault(x => x.Roots.Any(root => Paths.Normalize(root).Equals(Paths.Normalize(drive.Path), StringComparison.OrdinalIgnoreCase)));
            drive.LastState = latest?.Value.State ?? "";
        }
        Changed(nameof(IsFirstRun));
        Changed(nameof(HasHistory));
    }
    private void AddRoot()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a drive, folder, or network share" };
        if (dialog.ShowDialog() == true)
            Drives.Add(new(dialog.FolderName, dialog.FolderName, "Selected folder", true));
    }
    private string[] Roots() => Paths.DistinctRoots(Drives.Where(x => x.Selected).Select(x => Native.ResolveNetwork(x.Path)).Concat(Lines(ExtraRoots).Select(Native.ResolveNetwork)));
    private string[] Excluded() => Lines(Exclusions).Append(Path.GetDirectoryName(session.DatabasePath)!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string[] Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private readonly record struct ScanUpdate(string Text, string Engine, long Files, long Bytes, ScanProgress? Work);
    public async Task ScanAsync(string[]? rootsOverride = null)
    {
        if (session.Busy)
            return;
        var roots = rootsOverride ?? Roots();
        if (roots.Length == 0)
        {
            session.Status = "Select at least one drive or folder.";
            return;
        }
        var token = session.BeginWork(scan: true);
        var database = session.DatabasePath;
        var snapshot = session.Store.CreateSnapshot(roots);
        session.BeginSnapshot(snapshot);
        long count = 0;
        var exclusions = Excluded();
        var preferMft = PreferMft;
        var administrator = session.Administrator;
        var state = "Complete";
        ScanTarget = roots.Length == 1 ? roots[0] : $"{roots.Length} roots";
        ScanEngine = "";
        ScanFiles = 0;
        ScanBytes = "0 B";
        ScanElapsed = "0:00";
        ProgressKnown = false;
        ProgressValue = 0;
        ProgressText = "";
        long bytes = 0;
        scanStarted = DateTime.UtcNow;
        clock.Start();
        var progress = new Progress<ScanUpdate>(update =>
        {
            session.Status = update.Text;
            ScanEngine = update.Engine.StartsWith("MFT", StringComparison.Ordinal) || update.Engine == "Raw MFT" ? "Raw MFT" : "Directory";
            ScanFiles = update.Files;
            ScanBytes = Format.Bytes(update.Bytes);
            ProgressKnown = update.Work is { Total: > 0 };
            if (update.Work is { Total: > 0 } work)
            {
                ProgressValue = 100d * work.Done / work.Total;
                ProgressText = $"MFT records {work.Done / 2:N0} of {work.Total / 2:N0} · pass {(work.Done * 2 <= work.Total ? 1 : 2)} of 2";
            }
        });
        session.Status = "Starting scan…";
        try
        {
            await Task.Run(async () =>
            {
                using var writer = new IndexStore(database);
                await using var worker = await WorkerSession.StartAsync(administrator, token);
                await worker.ExecuteAsync(new("scan", Scopes: roots.Select(x => new ScanScope(x, exclusions, preferMft)).ToArray()), async message =>
                {
                    if (message.Batch is not { } batch)
                        return;
                    await session.WaitIfPausedAsync(token);
                    writer.AddBatch(snapshot, batch);
                    count += batch.Entries.LongLength;
                    foreach (var entry in batch.Entries)
                        if (!entry.IsDirectory)
                            bytes += entry.Length;
                    ((IProgress<ScanUpdate>)progress).Report(new($"{batch.Engine} · {count:N0} entries · {batch.Root}", batch.Engine, count, bytes, batch.Progress));
                }, token);
                var aliases = new List<string>(64);
                var aliasBytes = 0;
                async Task RefreshAliases()
                {
                    if (aliases.Count == 0)
                        return;
                    await worker.ExecuteAsync(new("metadata", MetadataPaths: aliases.ToArray()), message =>
                    {
                        if (message.Entry is { } entry && !writer.RefreshAlias(snapshot, entry))
                            writer.AddError(snapshot, entry.Path, "Hard-link identity changed during metadata refresh.");
                        if (message.Kind == "metadata-error")
                            writer.AddError(snapshot, message.Path ?? "", message.Text ?? "Hard-link metadata refresh failed.");
                        return Task.CompletedTask;
                    }, token);
                    aliases.Clear();
                    aliasBytes = 0;
                }
                foreach (var path in writer.AliasPaths(snapshot))
                {
                    token.ThrowIfCancellationRequested();
                    if (aliases.Count > 0 && aliasBytes + 6 * path.Length + 1024 > 1024 * 1024)
                        await RefreshAliases();
                    aliases.Add(path);
                    aliasBytes += 6 * path.Length + 1024;
                    if (aliases.Count == 64)
                        await RefreshAliases();
                }
                await RefreshAliases();
            }, token);
        }
        catch (OperationCanceledException) { state = "Cancelled"; session.Status = "Scan cancelled. Partial results remain available."; }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { state = "Cancelled"; session.Status = "Administrator request was cancelled."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception) { state = "Failed"; session.Store.AddError(snapshot, string.Join(';', roots), e.Message); session.Status = "Scan failed: " + e.Message; }
        finally
        {
            try
            {
                await Task.Run(() => { using var writer = new IndexStore(database); writer.Finish(snapshot, state); });
            }
            finally { clock.Stop(); session.EndWork(); }
            session.OpenSnapshot(snapshot);
            await session.RefreshAsync();
            if (state == "Complete")
                session.Status = "Scan complete. Review diagnostics for any coverage gaps.";
        }
    }
}
