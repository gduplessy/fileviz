namespace FileViz.Core;

/// <summary>How a diagnostic affects a snapshot.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Contents were not indexed: totals, duplicates, and comparisons are incomplete.</summary>
    Gap,
    /// <summary>Indexing recovered or changed engine; results are complete but worth knowing about.</summary>
    Warning,
    /// <summary>Listed by design but not followed, such as junctions and placeholders.</summary>
    Info
}

/// <summary>Diagnostic kinds. Set where the error is raised; older rows without a kind are classified from the message.</summary>
public static class DiagnosticKinds
{
    public const string AccessDenied = "AccessDenied", Unavailable = "Unavailable", Interrupted = "Interrupted", NotTraversed = "NotTraversed", EngineFallback = "EngineFallback", Other = "Other";
    public static string FromException(Exception error) => error switch
    {
        UnauthorizedAccessException => AccessDenied,
        DirectoryNotFoundException or FileNotFoundException or DriveNotFoundException => Unavailable,
        System.ComponentModel.Win32Exception { NativeErrorCode: 5 } => AccessDenied,
        System.ComponentModel.Win32Exception { NativeErrorCode: 2 or 3 or 53 or 67 or 1231 or 1232 } => Unavailable,
        System.ComponentModel.Win32Exception { NativeErrorCode: 64 or 121 or 1236 } => Interrupted,
        _ => Other
    };
    public static string Classify(string? kind, string message)
    {
        if (!string.IsNullOrEmpty(kind))
            return kind;
        bool Has(string text) => message.Contains(text, StringComparison.OrdinalIgnoreCase);
        if (Has("denied"))
            return AccessDenied;
        if (Has("network name") || Has("cannot find") || Has("not found") || Has("unavailable"))
            return Unavailable;
        if (Has("reset") || Has("interrupted") || Has("timeout") || Has("semaphore"))
            return Interrupted;
        if (Has("depth exceeds") || Has("reparse") || Has("placeholder") || Has("not traversed"))
            return NotTraversed;
        if (Has("raw") || Has("mft") || Has("ntfs") || Has("directory enumeration"))
            return EngineFallback;
        return Other;
    }
    public static DiagnosticSeverity Severity(string kind) => kind switch
    {
        AccessDenied or Unavailable => DiagnosticSeverity.Gap,
        NotTraversed => DiagnosticSeverity.Info,
        _ => DiagnosticSeverity.Warning
    };
    public static string Label(string kind) => kind switch
    {
        AccessDenied => "Access denied",
        Unavailable => "Path unavailable",
        Interrupted => "Interrupted",
        NotTraversed => "Not traversed",
        EngineFallback => "Engine fallback",
        _ => "Other"
    };
}

/// <summary>Totals of a snapshot comparison, beyond the first page of file changes.</summary>
public sealed record CompareSummary(long AddedFiles, long AddedBytes, long RemovedFiles, long RemovedBytes, long GrownFiles, long GrownBytes, long ShrunkFiles, long ShrunkBytes)
{
    public long NetBytes => AddedBytes + GrownBytes - RemovedBytes - ShrunkBytes;
}

/// <summary>Logical size of a folder in two snapshots.</summary>
public sealed record FolderChange(string Path, long Before, long After)
{
    public long Delta => After - Before;
}
