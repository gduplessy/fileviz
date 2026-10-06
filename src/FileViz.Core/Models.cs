using System.Globalization;
namespace FileViz.Core;

public enum ScanEngineKind
{
    Directory, Mft
}
public record ScanScope(string Root, string[] Exclusions, bool PreferMft = true);
public record FileEntry(string Path, string Parent, string Name, string? Identity, bool IsDirectory,
    long Length, long? Allocated, long ModifiedTicks, long ChangeTicks, uint Attributes)
{
    public string Extension => IsDirectory ? "" : System.IO.Path.GetExtension(Name).ToLowerInvariant();
    public bool IsPlaceholder => (Attributes & (0x1000u | 0x40000u | 0x400000u)) != 0;
    public bool IsReparse => (Attributes & 0x400u) != 0;
    public string SizeText => Format.Bytes(Length);
    public string AllocatedText => Allocated is long size ? Format.Bytes(size) : "Unknown";
    public string ModifiedText => ModifiedTicks > 0 ? new DateTime(ModifiedTicks, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "Unknown";
}
public record ScanError(string Path, string Message, string? Kind = null);
/// <summary>Work done and total for engines that know their extent (raw MFT records). Absent for directory enumeration.</summary>
public record ScanProgress(long Done, long Total);
public record ScanBatch(string Root, string Engine, FileEntry[] Entries, ScanError[] Errors, bool Restart = false, ScanProgress? Progress = null);
public record Snapshot(long Id, string Roots, string Started, string State, long Files, long Logical, long? Allocated, long Errors);
public record Summary(long Files, long Folders, long Logical, long Allocated, long UnknownAllocations, long Errors);
public record Breakdown(string Name, long Bytes, long Count);
public record ScanProfile(string Name, string[] Roots, string[] Exclusions, bool PreferMft);
public record DuplicateRow(long GroupId, FileEntry Entry, string Evidence, bool SuggestedKeeper);
/// <summary>Counts from the last duplicate analysis of a snapshot. Only verified groups carry a savings figure.</summary>
public record DuplicateRun(string Algorithm, long SizeCandidates, long SampleMatches, long VerifiedFiles, long Groups, long Reclaimable, long NameMatches, long Aliases, string Finished);
public record HashRequest(FileEntry Entry, string Algorithm, bool Sample = false);
public record HashResult(string Path, string? Hash, string? Identity, long Length, long ModifiedTicks, long ChangeTicks, string? Error);
/// <summary>Actual bytes read from one file; sampling totals reflect sampled blocks, not logical size.</summary>
public record HashProgress(string Path, long BytesRead, long TotalBytes);
public record DuplicateProgress(string Phase, long Completed = 0, long? Total = null, long BytesRead = 0,
    long Cached = 0, long Errors = 0, HashProgress? CurrentFile = null);
public record WorkerRequest(string Operation, ScanScope[]? Scopes = null, HashRequest[]? Hashes = null, string[]? MetadataPaths = null);
public record WorkerMessage(string Kind, ScanBatch? Batch = null, HashResult? Hash = null, string? Text = null, FileEntry? Entry = null, string? Path = null, HashProgress? HashProgress = null);
public record QueryFilter(string Search = "", string Extension = "", long MinimumSize = 0, long? ModifiedAfter = null, uint RequiredAttributes = 0, string? Parent = null, bool Allocated = false, string? Root = null);
public record Difference(string Path, string Change, long Before, long After)
{
    public long Delta => After - Before;
}
public record CleanupRecord(string Id, string Original, string Destination, string Identity, string State, string Time, string? Error = null);

public interface IScanEngine
{
    IAsyncEnumerable<ScanBatch> ScanAsync(ScanScope scope, CancellationToken cancellationToken = default);
}
public static class Format
{
    /// <summary>Elapsed time without wrapping at an hour or a day.</summary>
    public static string Elapsed(TimeSpan value) => value.TotalHours >= 1
        ? $"{(long)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{(long)value.TotalMinutes}:{value.Seconds:00}";
    /// <summary>"1 file", "2 files".</summary>
    public static string Count(long count, string singular, string? plural = null) => $"{count:N0} {(count == 1 ? singular : plural ?? singular + "s")}";
    public static string Bytes(long value)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
        var size = (double)value;
        var index = 0;
        while (Math.Abs(size) >= 1024 && index < units.Length - 1)
        {
            size /= 1024;
            index++;
        }
        return $"{size:0.##} {units[index]}";
    }
}
public static class Paths
{
    public static string Normalize(string path) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    public static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static string[] DistinctRoots(IEnumerable<string> roots)
    {
        var values = roots.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Length).ToArray();
        return values.Where((root, index) => !values.Take(index).Any(parent => Within(root, parent))).ToArray();
    }
    public static bool Excluded(string path, string[] exclusions) => exclusions.Any(x =>
        System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(x, path, true) ||
        System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(x, System.IO.Path.GetFileName(path), true) ||
        (System.IO.Path.IsPathFullyQualified(x) && Within(path, Normalize(x))));
}
