using System.ComponentModel;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
namespace FileViz.App.Services;

public static class DuplicateCoordinator
{
    public static async Task FindAsync(string database, long destination, long[] snapshots, string[] roots, string algorithm, string preferred, bool elevated, IProgress<string> progress, CancellationToken token)
    {
        using var store = new IndexStore(database);
        if (algorithm == "Name")
        {
            store.FindNameDuplicates(destination, snapshots, roots);
            return;
        }
        Hashing.Algorithm(algorithm);
        store.PrepareContentWork(snapshots, roots);
        await using var worker = await WorkerSession.StartAsync(elevated, token);
        for (var stage = 0; stage < 2; stage++)
        {
            var full = stage == 1;
            var requests = new List<HashRequest>(64);
            var completed = 0;
            async Task Flush()
            {
                if (requests.Count == 0)
                    return;
                var lookup = requests.ToDictionary(x => x.Entry.Path, StringComparer.Ordinal);
                await worker.ExecuteAsync(new("hash", Hashes: requests.ToArray()), message =>
                {
                    if (message.Hash is { } result)
                    {
                        store.SaveWorkHash(result, full);
                        store.SaveHash(lookup[result.Path], result);
                        if (result.Error != null)
                            store.AddError(destination, result.Path, result.Error);
                        progress.Report($"{(full ? "Full hashing" : "Sampling")} · {++completed:N0} files");
                    }
                    return Task.CompletedTask;
                }, token);
                requests.Clear();
            }
            foreach (var entry in store.WorkCandidates(full))
            {
                token.ThrowIfCancellationRequested();
                var request = new HashRequest(entry, algorithm, !full);
                try
                {
                    var current = Native.ReadEntry(entry.Path);
                    if (current.Identity == entry.Identity && current.Length == entry.Length && current.ModifiedTicks == entry.ModifiedTicks && current.ChangeTicks == entry.ChangeTicks && store.CachedHash(request, current) is { } hash)
                    {
                        store.SaveWorkHash(new(entry.Path, hash, current.Identity, current.Length, current.ModifiedTicks, current.ChangeTicks, null), full);
                        completed++;
                        continue;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception) { }
                if (requests.Count > 0 && requests.Sum(x => 6 * (x.Entry.Path.Length + x.Entry.Parent.Length + x.Entry.Name.Length) + 1024) + 6 * (entry.Path.Length + entry.Parent.Length + entry.Name.Length) + 1024 > 1024 * 1024)
                    await Flush();
                requests.Add(request);
                if (requests.Count == 64)
                    await Flush();
            }
            await Flush();
        }
        store.FinishContentWork(destination, algorithm, preferred);
    }
}
