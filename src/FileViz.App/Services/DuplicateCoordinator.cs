using System.Diagnostics;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using Microsoft.Data.Sqlite;
namespace FileViz.App.Services;

public static class DuplicateCoordinator
{
    public static async Task FindAsync(string database, long destination, long[] snapshots, string[] roots, string algorithm, string preferred, bool elevated, IProgress<DuplicateProgress> progress, CancellationToken token)
    {
        var current = new DuplicateProgress("Opening saved inventory");
        var updates = Stopwatch.StartNew();
        void Report(bool force = false)
        {
            if (!force && updates.ElapsedMilliseconds < 250) return;
            progress.Report(current);
            updates.Restart();
        }
        Report(true);
        using var store = new IndexStore(database);
        using var observation = store.ObserveDuplicateWork(phase =>
        {
            current = current with { Phase = phase, CurrentFile = null };
            Report(true);
        }, token);
        try
        {
            if (algorithm == "Name")
            {
                store.FindNameDuplicates(destination, snapshots, roots);
                return;
            }
            Hashing.Algorithm(algorithm);
            store.PrepareContentWork(snapshots, roots);
            current = current with { Phase = "Starting read-only hashing worker" };
            Report(true);
            await using var worker = await WorkerSession.StartAsync(elevated, token);
            for (var stage = 0; stage < 2; stage++)
            {
                var full = stage == 1;
                var total = store.WorkCandidateCount(full);
                var phase = full ? "Full hashing" : "Sampling";
                current = current with { Phase = phase, Completed = 0, Total = total, CurrentFile = null };
                Report(true);
                var requests = new List<HashRequest>(64);
                async Task Flush()
                {
                    if (requests.Count == 0) return;
                    var lookup = requests.ToDictionary(x => x.Entry.Path, StringComparer.Ordinal);
                    var remaining = new Dictionary<string, HashRequest>(lookup, StringComparer.Ordinal);
                    current = current with { Phase = "Checking current file metadata", CurrentFile = new(requests[0].Entry.Path, 0, 0) };
                    Report(true);
                    // Provider reads belong in the disposable worker so a stalled share stays cancellable.
                    await worker.ExecuteAsync(new("metadata", MetadataPaths: requests.Select(x => x.Entry.Path).ToArray()), message =>
                    {
                        if (message.Entry is { } entry)
                        {
                            var request = lookup[entry.Path];
                            var saved = request.Entry;
                            if (entry.Identity == saved.Identity && entry.Length == saved.Length && entry.ModifiedTicks == saved.ModifiedTicks
                                && entry.ChangeTicks == saved.ChangeTicks && store.CachedHash(request, entry) is { } hash)
                            {
                                store.SaveWorkHash(new(entry.Path, hash, entry.Identity, entry.Length, entry.ModifiedTicks, entry.ChangeTicks, null), full);
                                remaining.Remove(entry.Path);
                                current = current with { Completed = current.Completed + 1, Cached = current.Cached + 1 };
                            }
                        }
                        Report();
                        return Task.CompletedTask;
                    }, token);
                    current = current with { Phase = phase, CurrentFile = null };
                    Report(true);
                    long fileRead = 0;
                    if (remaining.Count > 0)
                        await worker.ExecuteAsync(new("hash", Hashes: remaining.Values.ToArray()), message =>
                        {
                            if (message.HashProgress is { } update)
                            {
                                current = current with { Phase = phase, BytesRead = current.BytesRead + Math.Max(0, update.BytesRead - fileRead), CurrentFile = update };
                                fileRead = update.BytesRead;
                                Report();
                            }
                            if (message.Hash is { } result)
                            {
                                store.SaveWorkHash(result, full);
                                store.SaveHash(lookup[result.Path], result);
                                if (result.Error != null) store.AddError(destination, result.Path, result.Error);
                                current = current with { Phase = phase, Completed = current.Completed + 1,
                                    Errors = current.Errors + (result.Error == null ? 0 : 1), CurrentFile = null };
                                fileRead = 0;
                                Report();
                            }
                            return Task.CompletedTask;
                        }, token);
                    requests.Clear();
                    Report(true);
                }
                foreach (var entry in store.WorkCandidates(full))
                {
                    token.ThrowIfCancellationRequested();
                    if (requests.Count > 0 && requests.Sum(x => 6 * (x.Entry.Path.Length + x.Entry.Parent.Length + x.Entry.Name.Length) + 1024)
                        + 6 * (entry.Path.Length + entry.Parent.Length + entry.Name.Length) + 1024 > 1024 * 1024)
                        await Flush();
                    requests.Add(new(entry, algorithm, !full));
                    if (requests.Count == 64) await Flush();
                }
                await Flush();
            }
            store.FinishContentWork(destination, algorithm, preferred);
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 9 && token.IsCancellationRequested)
        {
            throw new OperationCanceledException("Duplicate analysis cancelled.", e, token);
        }
    }
}
