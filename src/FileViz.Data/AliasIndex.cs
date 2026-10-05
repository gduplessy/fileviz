using FileViz.Core;
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
    public bool RefreshAlias(long snapshot, FileEntry current)
    {
        if (current.Identity == null || current.IsDirectory)
            return false;
        using var command = Command($"UPDATE entries INDEXED BY entry_{snapshot}_identity SET length=$l,allocated=$a,modified=$m,changed=$c,attrs=$attrs WHERE snapshot=$s AND identity=$id AND isdir=0 AND EXISTS(SELECT 1 FROM entries observed WHERE observed.snapshot=$s AND observed.path=$p AND observed.identity=$id);", ("$s", snapshot), ("$id", current.Identity), ("$p", current.Path), ("$l", current.Length), ("$a", current.Allocated), ("$m", current.ModifiedTicks), ("$c", current.ChangeTicks), ("$attrs", current.Attributes));
        return command.ExecuteNonQuery() > 0;
    }
}
