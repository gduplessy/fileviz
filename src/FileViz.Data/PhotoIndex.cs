using FileViz.Core;
using Microsoft.Data.Sqlite;

namespace FileViz.Data;

public sealed partial class IndexStore
{
    private static string ImageExtensions => string.Join(",", PhotoFormats.Extensions.Select(x => "'" + x + "'"));
    private void EnsurePhotoSchema() => Execute("""
        CREATE TABLE IF NOT EXISTS photos(snapshot INTEGER NOT NULL,path TEXT NOT NULL,identity TEXT,length INTEGER NOT NULL,modified INTEGER NOT NULL,changed INTEGER NOT NULL,width INTEGER NOT NULL,height INTEGER NOT NULL,frames INTEGER NOT NULL,error TEXT,
            PRIMARY KEY(snapshot,path),FOREIGN KEY(snapshot,path) REFERENCES entries(snapshot,path) ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS photo_resolution ON photos(snapshot,width,height,path);
        """);
    public void PreparePhotos(long snapshot, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (snapshot <= 0) throw new ArgumentOutOfRangeException(nameof(snapshot));
        // The existing (snapshot,path) primary key supports bounded root-range
        // reads. A new partial index would scan every historical entry first.
    }
    private static (string Start, string End) PhotoRange(string root)
    {
        var start = root.TrimEnd('\\') + "\\";
        return (start, start[..^1] + "]"); // '\\' < every child suffix < ']' under BINARY ordering.
    }
    public long? SavedPhotoCandidateCount(long snapshot, string root)
    {
        if (Scalar("SELECT 1 FROM root_totals WHERE snapshot=$s AND root=$r;", ("$s", snapshot), ("$r", root)) == null) return null;
        return Convert.ToInt64(Scalar($"SELECT COALESCE(SUM(files),0) FROM extension_totals WHERE snapshot=$s AND root=$r AND extension IN ({ImageExtensions});", ("$s", snapshot), ("$r", root)));
    }
    public long PhotoCandidateCount(long snapshot, string root)
    {
        if (SavedPhotoCandidateCount(snapshot, root) is long count) return count;
        var range = PhotoRange(root);
        return Convert.ToInt64(Scalar($"SELECT COUNT(*) FROM entries WHERE snapshot=$s AND root=$r AND path>=$start AND path<$end AND isdir=0 AND extension IN ({ImageExtensions});", ("$s", snapshot), ("$r", root), ("$start", range.Start), ("$end", range.End)));
    }
    /// <summary>Keyset pagination keeps inventory memory bounded and avoids repeated OFFSET scans.</summary>
    public List<PhotoRequest> PhotoCandidates(long snapshot, string root, string after = "", int limit = 64)
    {
        var range = PhotoRange(root);
        using var command = Command($"""
            SELECT {EntryColumns},p.identity,p.length,p.modified,p.changed,p.width,p.height,p.frames,p.error
            FROM entries e LEFT JOIN photos p ON p.snapshot=e.snapshot AND p.path=e.path
            WHERE e.snapshot=$s AND e.root=$r AND e.path>=$start AND e.path<$end AND e.isdir=0 AND e.extension IN ({ImageExtensions}) AND e.path>$after
            ORDER BY e.path LIMIT $limit;
            """, ("$s", snapshot), ("$r", root), ("$start", range.Start), ("$end", range.End), ("$after", after), ("$limit", Math.Clamp(limit, 1, 128)));
        using var reader = command.ExecuteReader();
        var result = new List<PhotoRequest>();
        while (reader.Read())
        {
            var entry = Entry(reader);
            var cached = reader.IsDBNull(11) ? null : Photo(reader, entry);
            result.Add(new(entry, cached));
        }
        return result;
    }
    private static PhotoInfo Photo(SqliteDataReader reader, FileEntry entry)
    {
        var saved = entry with { Identity = reader.IsDBNull(10) ? null : reader.GetString(10), Length = reader.GetInt64(11), ModifiedTicks = reader.GetInt64(12), ChangeTicks = reader.GetInt64(13) };
        return new(saved, reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16), reader.IsDBNull(17) ? null : reader.GetString(17));
    }
    public void SavePhotos(long snapshot, IEnumerable<PhotoInfo> photos)
    {
        using var transaction = connection.BeginTransaction();
        foreach (var photo in photos)
        {
            var e = photo.Entry;
            Execute("""
                INSERT OR REPLACE INTO photos(snapshot,path,identity,length,modified,changed,width,height,frames,error)
                VALUES($s,$p,$id,$l,$m,$c,$w,$h,$f,$error);
                """, ("$s", snapshot), ("$p", e.Path), ("$id", e.Identity), ("$l", e.Length), ("$m", e.ModifiedTicks), ("$c", e.ChangeTicks),
                ("$w", photo.Width), ("$h", photo.Height), ("$f", photo.Frames), ("$error", photo.Error));
        }
        transaction.Commit();
    }
    private static string PhotoWhere(PhotoFilter filter) => filter.ErrorsOnly ? "p.error IS NOT NULL" : "p.error IS NULL AND (($edge>0 AND MIN(p.width,p.height)<$edge) OR ($pixels>0 AND CAST(p.width AS INTEGER)*p.height<$pixels))";
    public List<PhotoInfo> QueryPhotos(long snapshot, string root, PhotoFilter filter, int page = 0)
        => ReadPhotos(snapshot, root, filter, 100, (long)Math.Max(0, page) * 100);
    /// <summary>One extra row detects an oversized selection before any choices are changed.</summary>
    public List<PhotoInfo> PhotoSelectionCandidates(long snapshot, string root, PhotoFilter filter, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        using var cancellation = token.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle!));
        try { return ReadPhotos(snapshot, root, filter, 5001, 0); }
        catch (SqliteException e) when (e.SqliteErrorCode == 9 && token.IsCancellationRequested) { throw new OperationCanceledException("Photo selection cancelled.", e, token); }
    }
    private List<PhotoInfo> ReadPhotos(long snapshot, string root, PhotoFilter filter, int limit, long offset)
    {
        filter.Validate();
        using var command = Command($"""
            SELECT {EntryColumns},p.identity,p.length,p.modified,p.changed,p.width,p.height,p.frames,p.error
            FROM photos p JOIN entries e ON e.snapshot=p.snapshot AND e.path=p.path
            WHERE p.snapshot=$s AND e.root=$r AND {PhotoWhere(filter)} AND e.path LIKE $search ESCAPE '!'
            ORDER BY CAST(p.width AS INTEGER)*p.height,p.path LIMIT $limit OFFSET $offset;
            """, ("$s", snapshot), ("$r", root), ("$edge", filter.MinimumShortEdge), ("$pixels", filter.MinimumMegapixels * 1_000_000),
            ("$search", "%" + EscapeLike(filter.Search) + "%"), ("$limit", limit), ("$offset", offset));
        using var reader = command.ExecuteReader();
        var result = new List<PhotoInfo>();
        while (reader.Read()) result.Add(Photo(reader, Entry(reader)));
        return result;
    }
    public (long Inspected, long Errors, long Matches) PhotoSummary(long snapshot, string root, PhotoFilter filter)
    {
        filter.Validate();
        using var command = Command($"""
            SELECT COUNT(*),COALESCE(SUM(p.error IS NOT NULL),0),COALESCE(SUM(CASE WHEN {PhotoWhere(filter)} AND e.path LIKE $search ESCAPE '!' THEN 1 ELSE 0 END),0)
            FROM photos p JOIN entries e ON e.snapshot=p.snapshot AND e.path=p.path WHERE p.snapshot=$s AND e.root=$r;
            """, ("$s", snapshot), ("$r", root), ("$edge", filter.MinimumShortEdge), ("$pixels", filter.MinimumMegapixels * 1_000_000), ("$search", "%" + EscapeLike(filter.Search) + "%"));
        using var reader = command.ExecuteReader(); reader.Read();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }
}
