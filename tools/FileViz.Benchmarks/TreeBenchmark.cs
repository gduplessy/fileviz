using System.Diagnostics;
using FileViz.Core;
using FileViz.Data;

internal static class TreeBenchmark
{
    public static object Run(int branches, string output)
    {
        if (branches < 1 || branches > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(branches));
        var db = Path.Combine(output, "tree.db");
        if (File.Exists(db))
            throw new IOException("Use an empty output directory.");
        const string root = @"C:\FileViz-Synthetic";
        using var store = new IndexStore(db);
        var snapshot = store.CreateSnapshot([root]);
        var entries = new List<FileEntry>(256);
        var timestamp = DateTime.UtcNow.Ticks;
        var ingest = Stopwatch.StartNew();
        for (var i = 0; i < branches; i++)
        {
            var branch = Path.Combine(root, $"branch-{i:D7}");
            var leaf = Path.Combine(branch, "leaf");
            entries.Add(new(branch, root, Path.GetFileName(branch), $"dir-{i}", true, 0, 0, timestamp, timestamp, 16));
            entries.Add(new(leaf, branch, "leaf", $"leaf-{i}", true, 0, 0, timestamp, timestamp, 16));
            entries.Add(new(Path.Combine(leaf, "file.bin"), leaf, "file.bin", $"file-{i}", false, 17, 4096, timestamp, timestamp, 32));
            if (entries.Count >= 256 || i == branches - 1)
            {
                store.AddBatch(snapshot, new(root, "Synthetic", entries.ToArray(), []));
                entries.Clear();
            }
        }
        var ingestSeconds = ingest.Elapsed.TotalSeconds;
        var stages = new List<object>();
        var timer = Stopwatch.StartNew();
        var previous = "Starting";
        var start = 0d;
        store.Finish(snapshot, "Complete", stage =>
        {
            var now = timer.Elapsed.TotalSeconds;
            stages.Add(new { Stage = previous, Seconds = now - start });
            Console.WriteLine($"{previous}: {now - start:0.000}s");
            previous = stage;
            start = now;
        });
        stages.Add(new { Stage = previous, Seconds = timer.Elapsed.TotalSeconds - start });
        var totals = store.GetSummary(snapshot);
        var folders = store.LargestFolders(snapshot);
        var composition = store.FolderComposition(snapshot, root);
        if (totals.Files != branches || totals.Folders != branches * 2L || totals.Logical != branches * 17L
            || totals.Allocated != branches * 4096L || folders[0].Bytes != branches * 17L
            || composition?.Categories.Sum() != branches * 17L || composition.Ages.Sum() != branches * 17L)
            throw new InvalidDataException("Branching-tree totals disagree.");
        return new { Kind = "Synthetic branching folder rollup (no filesystem reads)", Branches = branches,
            Folders = branches * 2L, Files = branches, IngestSeconds = ingestSeconds,
            FinalizeSeconds = timer.Elapsed.TotalSeconds, Stages = stages, Runtime = Environment.Version.ToString(),
            OS = Environment.OSVersion.ToString(), CpuCount = Environment.ProcessorCount,
            PeakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64, DatabaseBytes = new FileInfo(db).Length };
    }
}
