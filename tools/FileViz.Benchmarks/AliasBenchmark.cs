using System.Diagnostics;
using System.Runtime.CompilerServices;
using FileViz.Core;
using FileViz.Data;

internal static class AliasBenchmark
{
    public static object Run(int count, string output, bool unbatched)
    {
        if (count < 1 || count > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(count));
        var db = Path.Combine(output, "aliases.db");
        if (File.Exists(db))
            throw new IOException("Use an empty output directory.");
        using var store = new IndexStore(db);
        const string root = @"C:\FileViz-Synthetic";
        var snapshot = store.CreateSnapshot([root]);
        var entries = new List<FileEntry>(256);
        FileEntry Entry(int i) => new(Path.Combine(root, $"a-{i:D7}"), root, $"a-{i:D7}", $"id-{i}", false, 10, 4096, 100, 100, 32);
        for (var i = 0; i < count; i++)
        {
            var a = Entry(i);
            entries.Add(a);
            entries.Add(a with { Path = Path.Combine(root, $"b-{i:D7}"), Name = $"b-{i:D7}" });
            if (entries.Count == 256 || i == count - 1)
            {
                store.AddBatch(snapshot, new(root, "Synthetic", entries.ToArray(), []));
                entries.Clear();
            }
        }
        var prepare = Stopwatch.StartNew();
        if (store.AliasPaths(snapshot).Count() != count)
            throw new InvalidDataException("Incorrect alias candidate count.");
        var prepareSeconds = prepare.Elapsed.TotalSeconds;
        double Refresh(bool changed)
        {
            var timer = Stopwatch.StartNew();
            var batch = new List<FileEntry>(64);
            for (var i = 0; i < count; i++)
            {
                var entry = changed ? Entry(i) with { Length = 11, ChangeTicks = 200 } : Entry(i);
                if (unbatched)
                {
                    if (!store.RefreshAlias(snapshot, entry))
                        throw new InvalidDataException("Identity verification failed.");
                }
                else
                {
                    batch.Add(entry);
                    if (batch.Count == 64 || i == count - 1)
                    {
                        RefreshBatch(store, snapshot, batch);
                        batch.Clear();
                    }
                }
            }
            return timer.Elapsed.TotalSeconds;
        }
        var unchangedSeconds = Refresh(false);
        var changedSeconds = Refresh(true);
        var summary = store.GetSummary(snapshot);
        if (summary.Files != count * 2L || summary.Logical != count * 22L || summary.Allocated != count * 4096L)
            throw new InvalidDataException("Alias canonicalization or allocation accounting failed.");
        return new { Kind = "Synthetic hard-link metadata writes (no filesystem reads)", Groups = count,
            Files = count * 2L, Mode = unbatched ? "Individual transactions" : "64 entries per transaction",
            PrepareSeconds = prepareSeconds, UnchangedSeconds = unchangedSeconds, ChangedSeconds = changedSeconds,
            Runtime = Environment.Version.ToString(), OS = Environment.OSVersion.ToString(), CpuCount = Environment.ProcessorCount };
    }

    // Keeping this separate also permits the unbatched runner to measure a pre-fix Data assembly.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RefreshBatch(IndexStore store, long snapshot, IReadOnlyList<FileEntry> entries)
    {
        if (store.RefreshAliases(snapshot, entries).Count != 0)
            throw new InvalidDataException("Identity verification failed.");
    }
}
