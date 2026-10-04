using System.Globalization;
namespace FileViz.Core;

public enum ScanEngineKind { Directory, Mft }
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
public record ScanError(string Path, string Message);
public record ScanBatch(string Root, string Engine, FileEntry[] Entries, ScanError[] Errors, bool Restart = false);
public record Snapshot(long Id, string Roots, string Started, string State, long Files, long Logical, long? Allocated, long Errors);
public record Summary(long Files, long Folders, long Logical, long Allocated, long UnknownAllocations, long Errors);
public record Breakdown(string Name, long Bytes, long Count);
public record ScanProfile(string Name, string[] Roots, string[] Exclusions, bool PreferMft);
public record DuplicateRow(long GroupId, FileEntry Entry, string Evidence, bool SuggestedKeeper);
public record HashRequest(FileEntry Entry, string Algorithm, bool Sample = false);
public record HashResult(string Path, string? Hash, string? Identity, long Length, long ModifiedTicks, long ChangeTicks, string? Error);
public record WorkerRequest(string Operation, ScanScope[]? Scopes = null, HashRequest[]? Hashes = null);
public record WorkerMessage(string Kind, ScanBatch? Batch = null, HashResult? Hash = null, string? Text = null);
public record QueryFilter(string Search = "", string Extension = "", long MinimumSize = 0, long? ModifiedAfter = null, uint RequiredAttributes = 0, string? Parent = null, bool Allocated = false, string? Root = null);
public record Difference(string Path, string Change, long Before, long After) { public long Delta => After - Before; }
public record CleanupRecord(string Id, string Original, string Destination, string Identity, string State, string Time, string? Error = null);

public interface IScanEngine
{
    IAsyncEnumerable<ScanBatch> ScanAsync(ScanScope scope, CancellationToken cancellationToken = default);
}
public static class Format
{
    public static string Bytes(long value)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
        var size = (double)value; var index = 0;
        while (Math.Abs(size) >= 1024 && index < units.Length - 1) { size /= 1024; index++; }
        return $"{size:0.##} {units[index]}";
    }
}
public static class Paths
{
    public static string Normalize(string path) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    public static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(System.IO.Path.TrimEndingDirectorySeparator(root) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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