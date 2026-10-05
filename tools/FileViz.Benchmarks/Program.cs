using System.Diagnostics;
using System.Text.Json;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using FileViz.Windows.Ntfs;

if (args.Length < 3 || args[0] is not ("--index" or "--scan" or "--parity"))
{
    Console.Error.WriteLine("Usage: --index COUNT OUTPUT | --scan ROOT OUTPUT [mft|directory] | --parity ROOT OUTPUT");
    return 2;
}
var output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
var timer = Stopwatch.StartNew();
var process = Process.GetCurrentProcess();
long peak = 0;
using var sample = new Timer(_ => { try { process.Refresh(); Interlocked.Exchange(ref peak, Math.Max(Interlocked.Read(ref peak), process.WorkingSet64)); } catch (InvalidOperationException) { } }, null, 0, 50);
object result;
try
{
    if (args[0] == "--index")
    {
        var clustered = args.Contains("--clustered");
        var count = int.Parse(args[1]);
        if (count < 1 || count > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(count));
        var db = Path.Combine(output, $"index-{count}.db");
        if (File.Exists(db))
            throw new IOException("Use an empty output directory to avoid reusing old benchmark data.");
        using var store = new IndexStore(db);
        var root = @"C:\FileViz-Synthetic";
        var id = store.CreateSnapshot([root]);
        var entries = new List<FileEntry>(256);
        var timestamp = DateTime.UtcNow.Ticks;
        for (var i = 0; i < 1000; i++)
        {
            var name = "folder-" + i;
            entries.Add(new(Path.Combine(root, name), root, name, "dir-" + i, true, 0, 0, timestamp, timestamp, 16));
            if (entries.Count == 256)
            {
                store.AddBatch(id, new(root, "Synthetic", entries.ToArray(), []));
                entries.Clear();
            }
        }
        for (var i = 0; i < count; i++)
        {
            var parent = Path.Combine(root, "folder-" + (clustered ? (long)i * 1000 / count : i % 1000));
            var name = $"file-{i:D9}.bin";
            var length = 1 + (i * 104729L) % 10000000;
            entries.Add(new(Path.Combine(parent, name), parent, name, "synthetic-" + i, false, length, (length + 4095) / 4096 * 4096, timestamp, timestamp, 32));
            if (entries.Count == 256)
            {
                store.AddBatch(id, new(root, "Synthetic", entries.ToArray(), []));
                entries.Clear();
            }
            if (i % 100000 == 0)
                Console.WriteLine($"Indexed {i:N0} / {count:N0}");
        }
        if (entries.Count > 0)
            store.AddBatch(id, new(root, "Synthetic", entries.ToArray(), []));
        var ingestSeconds = timer.Elapsed.TotalSeconds;
        store.Finish(id, "Complete");
        var finalizeSeconds = timer.Elapsed.TotalSeconds - ingestSeconds;
        var query = new Stopwatch();
        var timings = new List<double>();
        for (var i = 0; i < 10; i++)
        {
            query.Restart();
            var rows = store.Query(id, new(Root: root));
            if (rows.Count != Math.Min(count, 250))
                throw new InvalidDataException("Incorrect benchmark query results.");
            timings.Add(query.Elapsed.TotalMilliseconds);
        }
        var totals = store.GetSummary(id);
        if (totals.Files != count)
            throw new InvalidDataException("Incorrect entry count.");
        result = new
        {
            Kind = "Synthetic SQLite index (not live filesystem throughput)",
            Count = count,
            IngestionOrder = clustered ? "Directory-clustered" : "Interleaved across 1000 directories",
            IngestSeconds = ingestSeconds,
            FinalizeSeconds = finalizeSeconds,
            PeakWorkingSet = peak,
            QueryMedianMs = timings.Order().ElementAt(5),
            QueryMaximumMs = timings.Max(),
            MemoryTargetBytes = count <= 1000000 ? 512L * 1024 * 1024 : 1024L * 1024 * 1024,
            MemoryTargetMet = peak < (count <= 1000000 ? 512L * 1024 * 1024 : 1024L * 1024 * 1024),
            QueryTargetMet = timings.Max() < 500,
            DatabaseBytes = new FileInfo(db).Length,
            OS = Environment.OSVersion.ToString(),
            CpuCount = Environment.ProcessorCount,
            Runtime = Environment.Version.ToString(),
            ElapsedSeconds = timer.Elapsed.TotalSeconds
        };
    }
    else if (args[0] == "--scan")
    {
        var root = Paths.Normalize(args[1]);
        IScanEngine engine = args.Length > 3 && args[3] == "mft" ? new MftScanEngine() : new DirectoryScanEngine();
        long entries = 0, errors = 0;
        await foreach (var batch in engine.ScanAsync(new(root, [], true)))
        {
            entries += batch.Entries.Length;
            errors += batch.Errors.Length;
        }
        result = new
        {
            Kind = "Live metadata scan",
            Root = root,
            Engine = engine.GetType().Name,
            Entries = entries,
            Errors = errors,
            PeakWorkingSet = peak,
            ElapsedSeconds = timer.Elapsed.TotalSeconds,
            Runtime = Environment.Version.ToString()
        };
    }
    else
    {
        var root = Paths.Normalize(args[1]);
        if (!Native.IsElevated || new DriveInfo(root).VolumeLabel != "FileVizTest")
            throw new IOException("Parity mode requires an elevated disposable NTFS volume labelled FileVizTest.");
        var dataRoot = Path.Combine(root, "data");
        using var store = new IndexStore(Path.Combine(output, "parity.db"));
        var ids = new List<long>();
        var durations = new List<double>();
        foreach (IScanEngine engine in new IScanEngine[] { new DirectoryScanEngine(), new MftScanEngine() })
        {
            var id = store.CreateSnapshot([root]);
            ids.Add(id);
            var start = timer.Elapsed.TotalSeconds;
            await foreach (var batch in engine.ScanAsync(new(root, [], true)))
                store.AddBatch(id, batch with
                {
                    Entries = batch.Entries.Where(x => Paths.Within(x.Path, dataRoot)).ToArray(),
                    Errors = batch.Errors.Where(x => Paths.Within(x.Path, dataRoot)).ToArray()
                });
            durations.Add(timer.Elapsed.TotalSeconds - start);
            foreach (var alias in store.AliasPaths(id))
                if (!store.RefreshAlias(id, Native.ReadEntry(alias)))
                    throw new InvalidDataException("Alias identity changed.");
            store.Finish(id, "Complete");
        }
        var differences = store.ParityDifferences(ids[0], ids[1]);
        var totals = ids.Select(id => store.GetSummary(id)).ToArray();
        result = new
        {
            Kind = "Disposable NTFS engine parity",
            DirectoryEntries = totals[0].Files + totals[0].Folders,
            MftEntries = totals[1].Files + totals[1].Folders,
            DirectorySeconds = durations[0],
            MftSeconds = durations[1],
            SpeedRatio = durations[0] / durations[1],
            ParityPassed = differences.Count == 0,
            Differences = differences,
            PeakWorkingSet = peak,
            OS = Environment.OSVersion.ToString(),
            Runtime = Environment.Version.ToString()
        };
        if (differences.Count > 0)
        {
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }
    File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(result));
    return 0;
}
catch (Exception e) { File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { Error = e.ToString(), ElapsedSeconds = timer.Elapsed.TotalSeconds, PeakWorkingSet = peak }, new JsonSerializerOptions { WriteIndented = true })); Console.Error.WriteLine(e); return 1; }
