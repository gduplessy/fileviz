using FileViz.Core;
using Microsoft.Data.Sqlite;
namespace FileViz.Data;

public sealed partial class IndexStore
{
    /// <summary>Enumerates only known hard-linked identities, using a disk index rather than a memory set.</summary>
    public IEnumerable<string> AliasPaths(long snapshot)
    {
        Execute($"CREATE INDEX IF NOT EXISTS entry_{snapshot}_identity ON entries(identity,isdir,path,allocated) WHERE snapshot={snapshot};");
        Execute("CREATE TEMP TABLE IF NOT EXISTS alias_paths(path TEXT PRIMARY KEY); DELETE FROM alias_paths;");
        Execute($"INSERT INTO alias_paths SELECT MIN(path) FROM entries INDEXED BY entry_{snapshot}_identity WHERE snapshot=$s AND isdir=0 AND identity IS NOT NULL GROUP BY identity HAVING COUNT(*)>1;", ("$s", snapshot));
        using var command = Command("SELECT path FROM alias_paths ORDER BY path;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
            yield return reader.GetString(0);
    }
    /// <summary>Directory index timestamps can lag after writes through another link. Canonicalize only after an identity-checked metadata read.</summary>
    public bool RefreshAlias(long snapshot, FileEntry current) => RefreshAlias(snapshot, current, null);

    /// <summary>Commit one bounded metadata batch; unchanged aliases cause no database writes.</summary>
    public IReadOnlyList<FileEntry> RefreshAliases(long snapshot, IReadOnlyList<FileEntry> entries, CancellationToken token = default)
    {
        if (entries.Count > 64)
            throw new ArgumentOutOfRangeException(nameof(entries), "Metadata batches cannot exceed 64 entries.");
        token.ThrowIfCancellationRequested();
        using var transaction = connection.BeginTransaction();
        var rejected = new List<FileEntry>();
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            if (!RefreshAlias(snapshot, entry, transaction))
                rejected.Add(entry);
        }
        token.ThrowIfCancellationRequested();
        transaction.Commit();
        return rejected;
    }

    private bool RefreshAlias(long snapshot, FileEntry current, SqliteTransaction? transaction)
    {
        if (current.Identity == null || current.IsDirectory)
            return false;
        using var command = Command($"UPDATE entries INDEXED BY entry_{snapshot}_identity SET length=$l,allocated=$a,modified=$m,changed=$c,attrs=$attrs WHERE snapshot=$s AND identity=$id AND isdir=0 AND (length IS NOT $l OR allocated IS NOT $a OR modified IS NOT $m OR changed IS NOT $c OR attrs IS NOT $attrs) AND EXISTS(SELECT 1 FROM entries observed WHERE observed.snapshot=$s AND observed.path=$p AND observed.identity=$id AND observed.isdir=0);", ("$s", snapshot), ("$id", current.Identity), ("$p", current.Path), ("$l", current.Length), ("$a", current.Allocated), ("$m", current.ModifiedTicks), ("$c", current.ChangeTicks), ("$attrs", current.Attributes));
        command.Transaction = transaction;
        if (command.ExecuteNonQuery() > 0)
            return true;
        // No changed rows can mean a verified no-op, rather than a replaced or missing file.
        using var observed = Command("SELECT 1 FROM entries WHERE snapshot=$s AND path=$p AND identity=$id AND isdir=0;", ("$s", snapshot), ("$p", current.Path), ("$id", current.Identity));
        observed.Transaction = transaction;
        return observed.ExecuteScalar() != null;
    }
}
