using System.Globalization;
using FileViz.Core;
namespace FileViz.Data;

/// <summary>Per-folder logical bytes by file category and age bucket, materialized when a snapshot finishes.</summary>
public sealed partial class IndexStore
{
    private static readonly string[] CompositionColumns = [.. Enumerable.Range(0, FileCategories.Count).Select(i => "c" + i), .. Enumerable.Range(0, AgeBuckets.Count).Select(i => "a" + i)];
    private static readonly string CompositionList = string.Join(",", CompositionColumns);
    /// <summary>Adds the composition columns to databases created before they existed. Older snapshots keep composition=0.</summary>
    private void EnsureCompositionSchema()
    {
        var folders = Columns("folders");
        foreach (var column in CompositionColumns)
            if (!folders.Contains(column))
                Execute($"ALTER TABLE folders ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0;");
        if (!Columns("snapshots").Contains("composition"))
            Execute("ALTER TABLE snapshots ADD COLUMN composition INTEGER NOT NULL DEFAULT 0;");
        if (!Columns("errors").Contains("kind"))
            Execute("ALTER TABLE errors ADD COLUMN kind TEXT;");
        Execute("CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);");
    }
    private HashSet<string> Columns(string table)
    {
        using var command = Command($"PRAGMA table_info({table});");
        using var reader = command.ExecuteReader();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            result.Add(reader.GetString(1));
        return result;
    }
    /// <summary>Snapshot start time in UTC ticks: the reference point for age buckets.</summary>
    public long StartedTicks(long snapshot) => Scalar("SELECT started FROM snapshots WHERE id=$s;", ("$s", snapshot)) is string started ? DateTime.Parse(started, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime().Ticks : DateTime.UtcNow.Ticks;
    public bool HasComposition(long snapshot) => Convert.ToInt64(Scalar("SELECT composition FROM snapshots WHERE id=$s;", ("$s", snapshot)) ?? 0L, CultureInfo.InvariantCulture) == 1;
    /// <summary>Runs after <see cref="RebuildFolders"/>: direct totals per parent, then a depth-ordered rollup like the size totals.</summary>
    private void BuildComposition(long snapshot)
    {
        var category = "CASE extension " + string.Join(" ", FileCategories.Extensions.Select(x => $"WHEN '{x.Key}' THEN {(int)x.Value}")) + $" ELSE {(int)FileCategory.Other} END";
        var age = $"CASE WHEN modified<=0 THEN {AgeBuckets.Count - 1} " + string.Join(" ", AgeBuckets.Limits.Select((limit, bucket) => $"WHEN $ref-modified<{limit.Ticks} THEN {bucket}")) + $" ELSE {AgeBuckets.Count - 1} END";
        var sums = string.Join(",", Enumerable.Range(0, FileCategories.Count).Select(i => $"SUM(CASE WHEN cat={i} THEN length ELSE 0 END) c{i}")
            .Concat(Enumerable.Range(0, AgeBuckets.Count).Select(i => $"SUM(CASE WHEN age={i} THEN length ELSE 0 END) a{i}")));
        Execute($"""
        DROP TABLE IF EXISTS temp.direct;
        CREATE TEMP TABLE direct AS SELECT parent,{sums} FROM (SELECT parent,length,{category} cat,{age} age FROM entries WHERE snapshot=$s AND isdir=0) GROUP BY parent;
        CREATE INDEX temp.direct_parent ON direct(parent);
        UPDATE folders SET ({CompositionList})=(SELECT {CompositionList} FROM direct WHERE direct.parent=folders.path) WHERE snapshot=$s AND path IN (SELECT parent FROM direct);
        DROP TABLE temp.direct;
        """, ("$s", snapshot), ("$ref", StartedTicks(snapshot)));
        var rollup = string.Join(",", CompositionColumns.Select(c => $"SUM({c}) {c}"));
        var add = string.Join(",", CompositionColumns.Select(c => $"folders.{c}+r.{c}"));
        Execute($"DROP TABLE IF EXISTS temp.composition_rollup; CREATE TEMP TABLE composition_rollup(parent TEXT PRIMARY KEY,{string.Join(',', CompositionColumns.Select(c => c + " INTEGER NOT NULL"))}) WITHOUT ROWID;");
        var maximum = Convert.ToInt32(Scalar("SELECT COALESCE(MAX(depth),0) FROM folders WHERE snapshot=$s;", ("$s", snapshot)), CultureInfo.InvariantCulture);
        for (var depth = maximum; depth >= 0; depth--)
            Execute($"""
            DELETE FROM composition_rollup;
            INSERT INTO composition_rollup SELECT parent,{rollup} FROM folders WHERE snapshot=$s AND depth=$d AND path<>parent GROUP BY parent;
            UPDATE folders SET ({CompositionList})=(SELECT {add} FROM composition_rollup r WHERE r.parent=folders.path) WHERE snapshot=$s AND path IN (SELECT parent FROM composition_rollup);
            """, ("$s", snapshot), ("$d", depth));
        Execute("DROP TABLE composition_rollup;");
        Execute("UPDATE snapshots SET composition=1 WHERE id=$s;", ("$s", snapshot));
    }
    /// <summary>Category and age totals for a folder, or null when the snapshot predates composition or the folder is unknown.</summary>
    public Composition? FolderComposition(long snapshot, string path)
    {
        if (!HasComposition(snapshot))
            return null;
        using var command = Command($"SELECT {CompositionList} FROM folders WHERE snapshot=$s AND path=$p;", ("$s", snapshot), ("$p", path));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadComposition(reader, 0) : null;
    }
    private static Composition ReadComposition(Microsoft.Data.Sqlite.SqliteDataReader reader, int start)
    {
        var result = Composition.Empty;
        for (var i = 0; i < FileCategories.Count; i++)
            result.Categories[i] = reader.GetInt64(start + i);
        for (var i = 0; i < AgeBuckets.Count; i++)
            result.Ages[i] = reader.GetInt64(start + FileCategories.Count + i);
        return result;
    }
    /// <summary>One entry by path, or null.</summary>
    public FileEntry? Entry(long snapshot, string path)
    {
        using var command = Command($"SELECT {EntryColumns} FROM entries e WHERE e.snapshot=$s AND e.path=$p;", ("$s", snapshot), ("$p", path));
        using var reader = command.ExecuteReader();
        return reader.Read() ? Entry(reader) : null;
    }
    /// <summary>
    /// Two-level space map for a folder: its largest child folders and files, and for the largest folders, their own
    /// largest children. Bounded by the limits so the map never loads a whole subtree.
    /// </summary>
    public List<MapNode> SpaceMap(long snapshot, string parent, bool allocated, int folderLimit = 24, int fileLimit = 40, int nestedFolders = 12, int nestedLimit = 8)
    {
        var reference = StartedTicks(snapshot);
        var composition = HasComposition(snapshot);
        List<MapNode> Level(string folder, int folders, int files)
        {
            var result = new List<MapNode>();
            using (var command = Command($"SELECT path,{(allocated ? "allocated" : "logical")},files,{CompositionList} FROM folders WHERE snapshot=$s AND parent=$p AND path<>parent ORDER BY {(allocated ? "allocated" : "logical")} DESC LIMIT $n;", ("$s", snapshot), ("$p", folder), ("$n", folders)))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    result.Add(new(reader.GetString(0), Name(reader.GetString(0)), true, reader.GetInt64(1), reader.GetInt64(2), composition ? ReadComposition(reader, 3) : Composition.Empty, []));
            foreach (var entry in Query(snapshot, new(Parent: folder, Allocated: allocated), 0, files, filesOnly: true))
                result.Add(new(entry.Path, entry.Name, false, allocated ? entry.Allocated ?? 0 : entry.Length, 1, Composition.ForFile(entry, reference), []));
            return result.OrderByDescending(x => x.Bytes).ToList();
        }
        var top = Level(parent, folderLimit, fileLimit);
        var expanded = 0;
        for (var index = 0; index < top.Count && expanded < nestedFolders; index++)
        {
            if (!top[index].IsDirectory)
                continue;
            top[index] = top[index] with { Children = Level(top[index].Path, nestedLimit, nestedLimit) };
            expanded++;
        }
        return top;
    }
    private static string Name(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
        return name.Length == 0 ? path : name;
    }
}
