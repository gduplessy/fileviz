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
public sealed class HomeViewModel : Bindable
{
    private readonly SessionViewModel session;
    public SessionViewModel Session => session;
    public ObservableCollection<DriveRow> Drives { get; } = [];
    public ObservableCollection<ScanProfile> Profiles { get; } = [];
    public string ExtraRoots { get; set; } = ""; public string Exclusions { get; set; } = ".FileViz-Quarantine"; public bool PreferMft { get; set; } = true;
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
                Changed(nameof(Exclusions));
                Changed(nameof(PreferMft));
                Changed(nameof(ProfileName));
            }
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
                    if (drive.DriveType == DriveType.Network)
                    {
                        result.Add(new(drive.Name, drive.Name + " · Network", "Uses your Windows share credentials"));
                        continue;
                    }
                    if (!drive.IsReady)
                        continue;
                    result.Add(new(drive.Name, drive.Name + " · " + drive.VolumeLabel, $"{drive.DriveFormat} · {Format.Bytes(drive.TotalSize - drive.AvailableFreeSpace)} used · {Format.Bytes(drive.AvailableFreeSpace)} free / {Format.Bytes(drive.TotalSize)}", drive.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase)));
                }
                catch (IOException) { result.Add(new(drive.Name, drive.Name, "Unavailable")); }
                catch (UnauthorizedAccessException) { result.Add(new(drive.Name, drive.Name, "Access denied")); }
            }
            return result;
        });
        SessionViewModel.Replace(Drives, rows);
        SessionViewModel.Replace(Profiles, session.Store.Profiles());
    }
    public void ChangedRoots() => Changed(nameof(ExtraRoots));
    private void AddRoot()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a drive, folder, or network share" };
        if (dialog.ShowDialog() == true)
            Drives.Add(new(dialog.FolderName, dialog.FolderName, "Selected folder", true));
    }
    private string[] Roots() => Paths.DistinctRoots(Drives.Where(x => x.Selected).Select(x => Native.ResolveNetwork(x.Path)).Concat(Lines(ExtraRoots).Select(Native.ResolveNetwork)));
    private string[] Excluded() => Lines(Exclusions).Append(Path.GetDirectoryName(session.DatabasePath)!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string[] Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
        var progress = new Progress<string>(text => session.Status = text);
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
                    ((IProgress<string>)progress).Report($"{batch.Engine} · {count:N0} entries · {batch.Root}");
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
            finally { session.EndWork(); }
            session.OpenSnapshot(snapshot);
            await session.RefreshAsync();
            if (state == "Complete")
                session.Status = "Scan complete. Review diagnostics for any coverage gaps.";
        }
    }
}
