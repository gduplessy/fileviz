namespace FileViz.Core;

/// <summary>Running scan totals, retaining only per-root counters so a fallback can discard raw results.</summary>
public sealed class ScanTally
{
    private readonly Dictionary<string, (long Entries, long Files, long Bytes)> roots = new(StringComparer.OrdinalIgnoreCase);
    public long Entries { get; private set; }
    public long Files { get; private set; }
    public long Bytes { get; private set; }

    public void Add(ScanBatch batch)
    {
        roots.TryGetValue(batch.Root, out var totals);
        if (batch.Restart)
        {
            Entries -= totals.Entries;
            Files -= totals.Files;
            Bytes -= totals.Bytes;
            totals = default;
        }
        var files = 0L;
        var bytes = 0L;
        foreach (var entry in batch.Entries)
            if (!entry.IsDirectory)
            {
                files++;
                bytes += entry.Length;
            }
        Entries += batch.Entries.LongLength;
        Files += files;
        Bytes += bytes;
        roots[batch.Root] = (totals.Entries + batch.Entries.LongLength, totals.Files + files, totals.Bytes + bytes);
    }
}
