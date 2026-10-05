using System.Text.Json;
using FileViz.Core;
using Microsoft.Data.Sqlite;
namespace FileViz.Data;

public sealed partial class IndexStore : IDisposable
{
    private readonly SqliteConnection connection;
    private CancellationToken commandCancellation;
    public string DatabasePath
    {
        get;
    }
    static IndexStore()
    {
        SQLitePCL.Batteries_V2.Init();
    }
    public IndexStore(string path)
    {
        DatabasePath = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DatabasePath)!);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        connection.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-32768; PRAGMA temp_store=FILE; PRAGMA busy_timeout=5000; PRAGMA wal_autocheckpoint=10000; PRAGMA foreign_keys=ON;");
        Execute("""
        CREATE TABLE IF NOT EXISTS snapshots(id INTEGER PRIMARY KEY,roots TEXT NOT NULL,started TEXT NOT NULL,state TEXT NOT NULL,files INTEGER NOT NULL DEFAULT 0,logical INTEGER NOT NULL DEFAULT 0,allocated INTEGER,errors INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS entries(snapshot INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,path TEXT NOT NULL,parent TEXT NOT NULL,name TEXT NOT NULL,name_key TEXT NOT NULL,identity TEXT,isdir INTEGER NOT NULL,length INTEGER NOT NULL,allocated INTEGER,modified INTEGER NOT NULL,changed INTEGER NOT NULL,attrs INTEGER NOT NULL,extension TEXT NOT NULL,root TEXT NOT NULL,PRIMARY KEY(snapshot,path));
        CREATE TABLE IF NOT EXISTS errors(snapshot INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,path TEXT NOT NULL,message TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS folders(snapshot INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,path TEXT NOT NULL,parent TEXT NOT NULL,depth INTEGER NOT NULL,root TEXT NOT NULL,logical INTEGER NOT NULL DEFAULT 0,allocated INTEGER NOT NULL DEFAULT 0,files INTEGER NOT NULL DEFAULT 0,PRIMARY KEY(snapshot,path));
        CREATE TABLE IF NOT EXISTS root_totals(snapshot INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,root TEXT NOT NULL,files INTEGER NOT NULL,folders INTEGER NOT NULL,logical INTEGER NOT NULL,allocated INTEGER NOT NULL,unknown INTEGER NOT NULL,PRIMARY KEY(snapshot,root));
        CREATE TABLE IF NOT EXISTS extension_totals(snapshot INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,root TEXT NOT NULL,extension TEXT NOT NULL,logical INTEGER NOT NULL,files INTEGER NOT NULL,PRIMARY KEY(snapshot,root,extension));
        CREATE INDEX IF NOT EXISTS extension_rank ON extension_totals(snapshot,root,logical DESC);
        CREATE INDEX IF NOT EXISTS errors_snapshot ON errors(snapshot);
        CREATE INDEX IF NOT EXISTS folders_rank ON folders(snapshot,root,logical DESC);
        CREATE INDEX IF NOT EXISTS folders_depth ON folders(snapshot,depth,parent);
        CREATE TABLE IF NOT EXISTS hashes(path TEXT NOT NULL,algorithm TEXT NOT NULL,sample INTEGER NOT NULL,identity TEXT NOT NULL,length INTEGER NOT NULL,modified INTEGER NOT NULL,changed INTEGER NOT NULL,hash TEXT NOT NULL,PRIMARY KEY(path,algorithm,sample));
        CREATE TABLE IF NOT EXISTS duplicates(snapshot INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,groupid INTEGER NOT NULL,path TEXT NOT NULL,evidence TEXT NOT NULL,keeper INTEGER NOT NULL DEFAULT 0,source INTEGER NOT NULL,PRIMARY KEY(snapshot,path));
        CREATE TABLE IF NOT EXISTS profiles(name TEXT PRIMARY KEY,json TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS duplicate_runs(snapshot INTEGER PRIMARY KEY REFERENCES snapshots(id) ON DELETE CASCADE,algorithm TEXT NOT NULL,size_candidates INTEGER NOT NULL,sample_matches INTEGER NOT NULL,verified INTEGER NOT NULL,groups INTEGER NOT NULL,reclaimable INTEGER NOT NULL,name_matches INTEGER NOT NULL,aliases INTEGER NOT NULL,finished TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS cleanup(id TEXT PRIMARY KEY,original TEXT NOT NULL,destination TEXT NOT NULL,identity TEXT NOT NULL,state TEXT NOT NULL,time TEXT NOT NULL,error TEXT);

        """);
        EnsureCompositionSchema();
    }
    public void RecoverInterrupted() => Execute("UPDATE snapshots SET state='Interrupted' WHERE state='Scanning';");
    public long CreateSnapshot(string[] roots)
    {
        Execute("INSERT INTO snapshots(roots,started,state) VALUES($roots,$started,'Scanning');", ("$roots", JsonSerializer.Serialize(roots)), ("$started", DateTime.UtcNow.ToString("O")));
        return Convert.ToInt64(Scalar("SELECT last_insert_rowid();"));
    }
    public void AddBatch(long snapshot, ScanBatch batch)
    {
        using var transaction = connection.BeginTransaction();
        if (batch.Restart)
        {
            Execute("DELETE FROM entries WHERE snapshot=$s AND root=$r; DELETE FROM errors WHERE snapshot=$s AND (path=$r OR path LIKE $prefix ESCAPE '!');", ("$s", snapshot), ("$r", batch.Root), ("$prefix", EscapeLike(batch.Root.TrimEnd('\\') + "\\") + "%"));
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR REPLACE INTO entries VALUES($s,$p,$parent,$name,$key,$id,$dir,$length,$allocated,$modified,$changed,$attrs,$ext,$root);";
        foreach (var name in new[] { "$s", "$p", "$parent", "$name", "$key", "$id", "$dir", "$length", "$allocated", "$modified", "$changed", "$attrs", "$ext", "$root" })
            command.Parameters.Add(new SqliteParameter(name, null));
        command.Prepare();
        foreach (var item in batch.Entries)
        {
            object?[] values = [snapshot, item.Path, item.Parent, item.Name, item.Name.ToUpperInvariant(), item.Identity, item.IsDirectory ? 1 : 0, item.Length, item.Allocated, item.ModifiedTicks, item.ChangeTicks, item.Attributes, item.Extension, batch.Root];
            for (var i = 0; i < values.Length; i++)
                command.Parameters[i].Value = values[i] ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
        foreach (var error in batch.Errors)
            Execute("INSERT INTO errors(snapshot,path,message,kind) VALUES($s,$p,$m,$k);", ("$s", snapshot), ("$p", error.Path), ("$m", error.Message), ("$k", error.Kind));
        transaction.Commit();
    }
    public void Finish(long snapshot, string state, Action<string>? progress = null) => Finish(snapshot, state, progress, CancellationToken.None);

    /// <summary>Builds views from persisted metadata; cancellation interrupts an executing SQLite statement.</summary>
    public void Finish(long snapshot, string state, Action<string>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var cancellation = token.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle!));
        commandCancellation = token;
        try
        {
            Execute("UPDATE snapshots SET state='Scanning',composition=0 WHERE id=$s; DELETE FROM root_totals WHERE snapshot=$s; DELETE FROM extension_totals WHERE snapshot=$s;", ("$s", snapshot));
            FinishViews(snapshot, state, progress);
            token.ThrowIfCancellationRequested();
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 9 && token.IsCancellationRequested)
        {
            throw new OperationCanceledException("Snapshot finalization cancelled.", e, token);
        }
        finally { commandCancellation = default; }
    }

    /// <summary>Rebuilds saved views without scanning roots or claiming that interrupted coverage is complete.</summary>
    public void RebuildViews(long snapshot, Action<string>? progress = null, CancellationToken token = default)
    {
        var state = Scalar("SELECT state FROM snapshots WHERE id=$s;", ("$s", snapshot)) as string;
        if (state is null || !(state.StartsWith("Interrupted", StringComparison.Ordinal) || state.StartsWith("Cancelled", StringComparison.Ordinal) || state.StartsWith("Failed", StringComparison.Ordinal)))
            throw new InvalidOperationException("Only interrupted, cancelled, or failed snapshots can be rebuilt.");
        try { Finish(snapshot, state, progress, token); }
        catch (OperationCanceledException) { SetState(snapshot, "Cancelled"); throw; }
    }

    private void FinishViews(long snapshot, string state, Action<string>? progress)
    {
        CreateQueryIndexes(snapshot, progress);
        progress?.Invoke("Calculating folder sizes");
        RebuildFolders(snapshot, progress);
        progress?.Invoke("Calculating file types and ages");
        BuildComposition(snapshot);
        progress?.Invoke("Summarizing drive usage");
        var summary = GetSummaryRaw(snapshot);
        CacheSummaries(snapshot, summary);
        progress?.Invoke("Saving snapshot");
        Execute("UPDATE snapshots SET state=$state,files=$files,logical=$logical,allocated=$allocated,errors=$errors WHERE id=$s;",
            ("$state", summary.Errors > 0 && state == "Complete" ? "Partial" : state), ("$files", summary.Files), ("$logical", summary.Logical), ("$allocated", summary.Allocated), ("$errors", summary.Errors), ("$s", snapshot));
        Execute("PRAGMA wal_checkpoint(TRUNCATE);");
    }
    public void SetState(long snapshot, string state) => Execute("UPDATE snapshots SET state=$state WHERE id=$s;", ("$state", state), ("$s", snapshot));
    public Summary GetSummary(long snapshot, string? root = null)
    {
        using var command = Command("SELECT files,folders,logical,allocated,unknown,(SELECT COUNT(*) FROM errors WHERE snapshot=$s) FROM root_totals WHERE snapshot=$s AND root=$root;", ("$s", snapshot), ("$root", root ?? ""));
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
                return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
        }
        return GetSummaryRaw(snapshot, root);
    }
    private void CacheSummaries(long snapshot, Summary summary)
    {
        Execute("DELETE FROM root_totals WHERE snapshot=$s; DELETE FROM extension_totals WHERE snapshot=$s;", ("$s", snapshot));
        Execute("INSERT INTO extension_totals SELECT $s,root,extension,SUM(length),COUNT(*) FROM entries WHERE snapshot=$s AND isdir=0 GROUP BY root,extension; INSERT INTO extension_totals SELECT $s,'',extension,SUM(logical),SUM(files) FROM extension_totals WHERE snapshot=$s GROUP BY extension;", ("$s", snapshot));
        Execute("""
        WITH counts AS(SELECT root,SUM(CASE WHEN isdir=0 THEN 1 ELSE 0 END) f,SUM(isdir) d,SUM(CASE WHEN isdir=0 THEN length ELSE 0 END) l,SUM(CASE WHEN isdir=0 AND allocated IS NULL THEN 1 ELSE 0 END) u FROM entries WHERE snapshot=$s GROUP BY root),
        allocation AS(SELECT root,SUM(a) a FROM(SELECT root,MAX(allocated) a FROM entries WHERE snapshot=$s AND isdir=0 GROUP BY root,COALESCE(identity,path))GROUP BY root)
        INSERT INTO root_totals SELECT $s,counts.root,f,d,l,COALESCE(allocation.a,0),u FROM counts LEFT JOIN allocation USING(root);
        """, ("$s", snapshot));
        Execute("INSERT INTO root_totals VALUES($s,'',$f,$d,$l,$a,$u);", ("$s", snapshot), ("$f", summary.Files), ("$d", summary.Folders), ("$l", summary.Logical), ("$a", summary.Allocated), ("$u", summary.UnknownAllocations));
    }
    public void MarkStale(string original)
    {
        Execute("UPDATE snapshots SET state=state||' · Stale' WHERE id IN(SELECT snapshot FROM entries WHERE path=$p) AND INSTR(state,'Stale')=0;", ("$p", original));
    }
    private Summary GetSummaryRaw(long snapshot, string? root = null)
    {
        using var command = Command("""
        SELECT SUM(CASE WHEN isdir=0 THEN 1 ELSE 0 END),SUM(isdir),SUM(CASE WHEN isdir=0 THEN length ELSE 0 END),
        (SELECT COALESCE(SUM(a),0) FROM (SELECT MAX(allocated) a FROM entries WHERE snapshot=$s AND ($root IS NULL OR root=$root) AND isdir=0 GROUP BY COALESCE(identity,path))),
        SUM(CASE WHEN isdir=0 AND allocated IS NULL THEN 1 ELSE 0 END),(SELECT COUNT(*) FROM errors WHERE snapshot=$s)
        FROM entries WHERE snapshot=$s AND ($root IS NULL OR root=$root);
        """, ("$s", snapshot), ("$root", root));
        using var reader = command.ExecuteReader();
        reader.Read();
        long Value(int index) => reader.IsDBNull(index) ? 0 : reader.GetInt64(index);
        return new(Value(0), Value(1), Value(2), Value(3), Value(4), Value(5));
    }
    private void CreateQueryIndexes(long snapshot, Action<string>? progress)
    {
        // Existing snapshots keep their indexes. New rows do not satisfy old index predicates,
        // so scanning does not update six large B-trees for every incoming file.
        var definitions = new Dictionary<string, string>
        {
            ["parent"] = "parent,length DESC,path",
            ["parent_allocated"] = "parent,allocated DESC,path",
            ["size"] = "root,isdir,length DESC,path",
            ["allocated"] = "root,isdir,allocated DESC,path",
            ["identity"] = "identity,isdir,path,allocated",
            ["name"] = "name_key,path",
            ["extension"] = "root,extension,length DESC"
        };
        var index = 0;
        foreach (var item in definitions)
        {
            progress?.Invoke($"Preparing file views ({++index}/{definitions.Count}): {item.Key.Replace('_', ' ')}");
            Execute($"CREATE INDEX IF NOT EXISTS entry_{snapshot}_{item.Key} ON entries({item.Value}) WHERE snapshot={snapshot};");
        }
        progress?.Invoke("Optimizing file queries");
        Execute("PRAGMA analysis_limit=1000; ANALYZE entries;");
    }
    public List<string> PrimaryQueryPlan(long snapshot, string root)
    {
        using var command = Command($"EXPLAIN QUERY PLAN SELECT path FROM entries WHERE snapshot={snapshot} AND root=$root AND isdir=0 ORDER BY length DESC,path LIMIT 250;", ("$root", root));
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
            result.Add(reader.GetString(3));
        return result;
    }
    private void RebuildFolders(long snapshot, Action<string>? progress = null)
    {
        progress?.Invoke("Initializing folder views");
        Execute("DELETE FROM folders WHERE snapshot=$s;", ("$s", snapshot));
        var roots = JsonSerializer.Deserialize<string[]>((string)Scalar("SELECT roots FROM snapshots WHERE id=$s;", ("$s", snapshot))!) ?? [];
        foreach (var root in roots)
        {
            // Snapshot roots are already persisted. Do not read and sort the complete
            // inventory to rediscover them, or visit file rows to select directories.
            Execute("""INSERT OR IGNORE INTO folders(snapshot,path,parent,depth,root) VALUES($s,$r,$r,LENGTH($r)-LENGTH(REPLACE($r,'\','')),$r);""", ("$s", snapshot), ("$r", root));
            Execute($"""
            INSERT OR IGNORE INTO folders(snapshot,path,parent,depth,root)
            SELECT $s,path,parent,LENGTH(path)-LENGTH(REPLACE(path,'\','')),$r
            FROM entries INDEXED BY entry_{snapshot}_size WHERE snapshot=$s AND root=$r AND isdir=1;
            """, ("$s", snapshot), ("$r", root));
        }
        progress?.Invoke("Locating hard-link allocation owners");
        // Only shared identities need an allocation owner. Most files are unique; do not
        // perform a second entries lookup for every file or scan entries once per folder.
        Execute($"""
        DROP TABLE IF EXISTS temp.folder_alias_owners;
        DROP TABLE IF EXISTS temp.folder_direct;
        DROP TABLE IF EXISTS temp.folder_rollup;
        CREATE TEMP TABLE folder_alias_owners(identity TEXT PRIMARY KEY,path TEXT NOT NULL) WITHOUT ROWID;
        INSERT INTO folder_alias_owners
        SELECT identity,MIN(path) FROM entries INDEXED BY entry_{snapshot}_identity
        WHERE snapshot=$s AND isdir=0 AND identity IS NOT NULL GROUP BY identity HAVING COUNT(*)>1;
        CREATE TEMP TABLE folder_direct(parent TEXT PRIMARY KEY,logical INTEGER NOT NULL,allocated INTEGER NOT NULL,files INTEGER NOT NULL) WITHOUT ROWID;
        """, ("$s", snapshot));
        progress?.Invoke("Summing direct folder files");
        Execute($"""
        INSERT INTO folder_direct
        SELECT e.parent,SUM(e.length),COALESCE(SUM(CASE WHEN owner.identity IS NULL OR e.path=owner.path THEN e.allocated ELSE 0 END),0),COUNT(*)
        FROM entries e INDEXED BY entry_{snapshot}_parent LEFT JOIN folder_alias_owners owner ON owner.identity=e.identity
        WHERE e.snapshot=$s AND e.isdir=0 GROUP BY e.parent;
        UPDATE folders SET (logical,allocated,files)=(SELECT logical,allocated,files FROM folder_direct WHERE folder_direct.parent=folders.path)
        WHERE snapshot=$s AND path IN(SELECT parent FROM folder_direct);
        DROP TABLE folder_direct;
        DROP TABLE folder_alias_owners;
        CREATE TEMP TABLE folder_rollup(parent TEXT PRIMARY KEY,logical INTEGER NOT NULL,allocated INTEGER NOT NULL,files INTEGER NOT NULL) WITHOUT ROWID;
        """, ("$s", snapshot));
        var maximum = Convert.ToInt32(Scalar("SELECT COALESCE(MAX(depth),0) FROM folders WHERE snapshot=$s;", ("$s", snapshot)));
        for (var depth = maximum; depth >= 0; depth--)
        {
            progress?.Invoke($"Rolling up folder depth {maximum - depth + 1} of {maximum + 1}");
            Execute("""
            DELETE FROM folder_rollup;
            INSERT INTO folder_rollup SELECT parent,SUM(logical),SUM(allocated),SUM(files)
            FROM folders WHERE snapshot=$s AND depth=$d AND path<>parent GROUP BY parent;
            UPDATE folders SET (logical,allocated,files)=(SELECT folders.logical+r.logical,folders.allocated+r.allocated,folders.files+r.files FROM folder_rollup r WHERE r.parent=folders.path)
            WHERE snapshot=$s AND path IN(SELECT parent FROM folder_rollup);
            """, ("$s", snapshot), ("$d", depth));
        }
        Execute("DROP TABLE folder_rollup;");
    }
    public List<Snapshot> Snapshots()
    {
        using var command = Command("SELECT id,roots,started,state,files,logical,allocated,errors FROM snapshots ORDER BY id DESC LIMIT 100;");
        using var reader = command.ExecuteReader();
        var result = new List<Snapshot>();
        while (reader.Read())
            result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetInt64(5), reader.IsDBNull(6) ? null : reader.GetInt64(6), reader.GetInt64(7)));
        return result;
    }
    private static string EntryColumns => "e.path,e.parent,e.name,e.identity,e.isdir,e.length,e.allocated,e.modified,e.changed,e.attrs";
    private static FileEntry Entry(SqliteDataReader reader, int start = 0) => new(reader.GetString(start), reader.GetString(start + 1), reader.GetString(start + 2), reader.IsDBNull(start + 3) ? null : reader.GetString(start + 3), reader.GetInt64(start + 4) != 0, reader.GetInt64(start + 5), reader.IsDBNull(start + 6) ? null : reader.GetInt64(start + 6), reader.GetInt64(start + 7), reader.GetInt64(start + 8), (uint)reader.GetInt64(start + 9));
    public List<FileEntry> Query(long snapshot, QueryFilter filter, int page = 0, int pageSize = 250, bool filesOnly = true)
    {
        var where = $"e.snapshot={snapshot}" + (filesOnly ? " AND e.isdir=0" : "");
        using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("$s", snapshot);
        if (filter.Root != null)
        {
            where += " AND e.root=$root";
            command.Parameters.AddWithValue("$root", filter.Root);
        }
        if (filter.Search.Length > 0)
        {
            where += " AND e.path LIKE $search ESCAPE '!'";
            command.Parameters.AddWithValue("$search", "%" + EscapeLike(filter.Search) + "%");
        }
        if (filter.Extension.Length > 0)
        {
            where += " AND e.extension=$ext";
            command.Parameters.AddWithValue("$ext", filter.Extension.StartsWith('.') ? filter.Extension.ToLowerInvariant() : "." + filter.Extension.ToLowerInvariant());
        }
        if (filter.MinimumSize > 0)
        {
            where += " AND e.length >= $min";
            command.Parameters.AddWithValue("$min", filter.MinimumSize);
        }
        if (filter.ModifiedAfter is long after)
        {
            where += " AND e.modified >= $after";
            command.Parameters.AddWithValue("$after", after);
        }
        if (filter.RequiredAttributes != 0)
        {
            where += " AND (e.attrs & $attrs)=$attrs";
            command.Parameters.AddWithValue("$attrs", filter.RequiredAttributes);
        }
        if (filter.Parent != null)
        {
            where += " AND e.parent=$parent";
            command.Parameters.AddWithValue("$parent", filter.Parent);
        }
        command.CommandText = $"SELECT {EntryColumns} FROM entries e WHERE {where} ORDER BY {(filter.Allocated ? "e.allocated" : "e.length")} DESC,e.path LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(pageSize, 1, 1000));
        command.Parameters.AddWithValue("$offset", Math.Max(0, page) * Math.Clamp(pageSize, 1, 1000));
        using var reader = command.ExecuteReader();
        var result = new List<FileEntry>();
        while (reader.Read())
            result.Add(Entry(reader));
        return result;
    }
    public List<Breakdown> LargestFolders(long snapshot, bool allocated = false, string? parent = null, string? root = null)
    {
        using var command = Command($"SELECT path,{(allocated ? "allocated" : "logical")},files FROM folders WHERE snapshot=$s AND ($root IS NULL OR path=$root OR path LIKE $prefix ESCAPE '!') {(parent == null ? "" : "AND parent=$parent AND path<>parent")} ORDER BY {(allocated ? "allocated" : "logical")} DESC LIMIT 100;", ("$s", snapshot), ("$parent", parent), ("$root", root), ("$prefix", root == null ? null : EscapeLike(root.TrimEnd('\\') + "\\") + "%"));
        using var reader = command.ExecuteReader();
        var result = new List<Breakdown>();
        while (reader.Read())
            result.Add(new(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        return result;
    }
    public List<Breakdown> Extensions(long snapshot, string? root = null)
    {
        using var command = Command("SELECT extension,logical,files FROM extension_totals WHERE snapshot=$s AND root=COALESCE($root,'') ORDER BY logical DESC LIMIT 100;", ("$s", snapshot), ("$root", root));
        using var reader = command.ExecuteReader();
        var result = new List<Breakdown>();
        while (reader.Read())
            result.Add(new(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        return result;
    }
    public List<ScanError> Errors(long snapshot)
    {
        using var command = Command("SELECT path,message,kind FROM errors WHERE snapshot=$s LIMIT 1000;", ("$s", snapshot));
        using var reader = command.ExecuteReader();
        var result = new List<ScanError>();
        while (reader.Read())
            result.Add(new(reader.GetString(0), reader.GetString(1), DiagnosticKinds.Classify(reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(1))));
        return result;
    }
    public IEnumerable<FileEntry> ContentCandidates(long[] snapshots, long minimum = 1)
    {
        var ids = string.Join(',', snapshots.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (ids.Length == 0)
            yield break;
        using var command = Command($"SELECT {EntryColumns} FROM entries e WHERE e.snapshot IN ({ids}) AND e.isdir=0 AND e.length >= $min AND e.length IN (SELECT length FROM entries WHERE snapshot IN ({ids}) AND isdir=0 GROUP BY length HAVING COUNT(DISTINCT COALESCE(identity,path))>1) GROUP BY COALESCE(e.identity,e.path) ORDER BY e.length,e.path;", ("$min", minimum));
        using var reader = command.ExecuteReader();
        while (reader.Read())
            yield return Entry(reader);
    }
    public string? CachedHash(HashRequest request, FileEntry current)
    {
        if (current.Identity == null || current.ChangeTicks == 0)
            return null;
        var value = Scalar("SELECT hash FROM hashes WHERE path=$p AND algorithm=$a AND sample=$sample AND identity=$id AND length=$l AND modified=$m AND changed=$c;",
            ("$p", request.Entry.Path), ("$a", request.Algorithm), ("$sample", request.Sample ? 1 : 0), ("$id", current.Identity), ("$l", current.Length), ("$m", current.ModifiedTicks), ("$c", current.ChangeTicks));
        return value as string;
    }
    public void SaveHash(HashRequest request, HashResult result)
    {
        if (result.Hash == null || result.Identity == null || result.ChangeTicks == 0)
            return;
        Execute("INSERT OR REPLACE INTO hashes VALUES($p,$a,$sample,$id,$l,$m,$c,$h);", ("$p", result.Path), ("$a", request.Algorithm), ("$sample", request.Sample ? 1 : 0), ("$id", result.Identity), ("$l", result.Length), ("$m", result.ModifiedTicks), ("$c", result.ChangeTicks), ("$h", result.Hash));
    }
    public void ClearDuplicates(long snapshot) => Execute("DELETE FROM duplicates WHERE snapshot=$s;", ("$s", snapshot));
    public void AddDuplicate(long snapshot, long group, string path, string evidence, bool keeper) => Execute("INSERT OR REPLACE INTO duplicates VALUES($s,$g,$p,$e,$k,(SELECT MAX(snapshot) FROM entries WHERE path=$p));", ("$s", snapshot), ("$g", group), ("$p", path), ("$e", evidence), ("$k", keeper ? 1 : 0));
    public void FindNameDuplicates(long destination, long[] snapshots, string[]? roots = null)
    {
        ClearDuplicates(destination);
        var ids = string.Join(',', snapshots);
        if (ids.Length == 0)
            return;
        var rootSql = roots is { Length: > 0 } ? " AND root IN(" + string.Join(',', roots.Select((_, i) => "$root" + i)) + ")" : "";
        var parameters = new List<(string Name, object? Value)> { ("$s", destination) };
        if (roots != null)
            parameters.AddRange(roots.Select((value, i) => ("$root" + i, (object?)value)));
        Execute($"""
        WITH chosen AS(SELECT e.* FROM entries e WHERE e.snapshot IN({ids}) AND e.isdir=0 {rootSql}
        AND e.snapshot=(SELECT MAX(snapshot) FROM entries WHERE path=e.path AND snapshot IN({ids}) {rootSql})),
        physical AS(SELECT * FROM chosen GROUP BY COALESCE(identity,path))
        INSERT OR REPLACE INTO duplicates SELECT $s,DENSE_RANK() OVER(ORDER BY name_key),path,'Name candidate',
        CASE WHEN ROW_NUMBER() OVER(PARTITION BY name_key ORDER BY path)=1 THEN 1 ELSE 0 END,snapshot
        FROM physical WHERE name_key IN(SELECT name_key FROM physical GROUP BY name_key HAVING COUNT(*)>1);
        """, parameters.ToArray());
        var names = Convert.ToInt64(Scalar("SELECT COUNT(*) FROM duplicates WHERE snapshot=$s;", ("$s", destination)));
        var groups = Convert.ToInt64(Scalar("SELECT COUNT(DISTINCT groupid) FROM duplicates WHERE snapshot=$s;", ("$s", destination)));
        SaveDuplicateRun(destination, new("Name", 0, 0, 0, groups, 0, names, 0, DateTime.UtcNow.ToString("O")));
    }
    public List<DuplicateRow> Duplicates(long snapshot, int page = 0)
    {
        using var command = Command($"SELECT d.groupid,{EntryColumns},d.evidence,d.keeper FROM duplicates d JOIN entries e ON e.path=d.path AND e.snapshot=d.source WHERE d.snapshot=$s GROUP BY d.path ORDER BY d.groupid,d.keeper DESC,d.path LIMIT 250 OFFSET $offset;", ("$s", snapshot), ("$offset", Math.Max(0, page) * 250));
        using var reader = command.ExecuteReader();
        var result = new List<DuplicateRow>();
        while (reader.Read())
            result.Add(new(reader.GetInt64(0), Entry(reader, 1), reader.GetString(11), reader.GetInt64(12) != 0));
        return result;
    }
    public List<FileEntry> DuplicateGroup(long snapshot, long group)
    {
        using var command = Command($"SELECT {EntryColumns} FROM duplicates d JOIN entries e ON e.path=d.path AND e.snapshot=d.source WHERE d.snapshot=$s AND d.groupid=$g GROUP BY d.path ORDER BY d.keeper DESC,d.path LIMIT 10000;", ("$s", snapshot), ("$g", group));
        using var reader = command.ExecuteReader();
        var result = new List<FileEntry>();
        while (reader.Read())
            result.Add(Entry(reader));
        return result;
    }
    /// <summary>First 250 added, removed, or grown files by size change, optionally only one kind of change.</summary>
    public List<Difference> Compare(long before, long after, int page = 0, string? change = null)
    {
        using var command = Command("""
        SELECT * FROM (
        SELECT a.path p,CASE WHEN b.path IS NULL THEN 'Added' ELSE 'Grown' END c,COALESCE(b.length,0) x,a.length y FROM entries a LEFT JOIN entries b ON b.snapshot=$before AND b.path=a.path WHERE a.snapshot=$after AND a.isdir=0 AND (b.path IS NULL OR a.length>b.length)
        UNION ALL SELECT b.path,'Removed',b.length,0 FROM entries b LEFT JOIN entries a ON a.snapshot=$after AND a.path=b.path WHERE b.snapshot=$before AND b.isdir=0 AND a.path IS NULL)
        WHERE $change IS NULL OR c=$change ORDER BY ABS(y-x) DESC,p LIMIT 250 OFFSET $offset;
        """, ("$before", before), ("$after", after), ("$offset", Math.Max(0, page) * 250), ("$change", change));
        using var reader = command.ExecuteReader();
        var result = new List<Difference>();
        while (reader.Read())
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3)));
        return result;
    }
    /// <summary>Counts and bytes of every change between two snapshots, not just the first page.</summary>
    public CompareSummary CompareTotals(long before, long after)
    {
        using var command = Command("""
        SELECT (SELECT COUNT(*) FROM entries a LEFT JOIN entries b ON b.snapshot=$b AND b.path=a.path WHERE a.snapshot=$a AND a.isdir=0 AND b.path IS NULL),
        (SELECT COALESCE(SUM(a.length),0) FROM entries a LEFT JOIN entries b ON b.snapshot=$b AND b.path=a.path WHERE a.snapshot=$a AND a.isdir=0 AND b.path IS NULL),
        (SELECT COUNT(*) FROM entries b LEFT JOIN entries a ON a.snapshot=$a AND a.path=b.path WHERE b.snapshot=$b AND b.isdir=0 AND a.path IS NULL),
        (SELECT COALESCE(SUM(b.length),0) FROM entries b LEFT JOIN entries a ON a.snapshot=$a AND a.path=b.path WHERE b.snapshot=$b AND b.isdir=0 AND a.path IS NULL),
        COALESCE(SUM(CASE WHEN a.length>b.length THEN 1 ELSE 0 END),0),COALESCE(SUM(CASE WHEN a.length>b.length THEN a.length-b.length ELSE 0 END),0),
        COALESCE(SUM(CASE WHEN a.length<b.length THEN 1 ELSE 0 END),0),COALESCE(SUM(CASE WHEN a.length<b.length THEN b.length-a.length ELSE 0 END),0)
        FROM entries a JOIN entries b ON b.snapshot=$b AND b.path=a.path WHERE a.snapshot=$a AND a.isdir=0 AND b.isdir=0;
        """, ("$a", after), ("$b", before));
        using var reader = command.ExecuteReader();
        reader.Read();
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7));
    }
    /// <summary>
    /// Folders one and two levels below <paramref name="root"/> whose logical size changed most, from the materialized
    /// folder totals of both snapshots (no file-level join).
    /// </summary>
    public List<FolderChange> FolderChanges(long before, long after, string root, int limit = 12)
    {
        var normalized = Paths.Normalize(root);
        var depth = Convert.ToInt64(Scalar("SELECT depth FROM folders WHERE snapshot=$a AND path=$r;", ("$a", after), ("$r", normalized)) ?? Scalar("SELECT depth FROM folders WHERE snapshot=$b AND path=$r;", ("$b", before), ("$r", normalized)) ?? 0L);
        using var command = Command("""
        SELECT * FROM (
        SELECT a.path p,COALESCE(b.logical,0) x,a.logical y FROM folders a LEFT JOIN folders b ON b.snapshot=$b AND b.path=a.path WHERE a.snapshot=$a AND a.root=$r AND a.depth BETWEEN $d+1 AND $d+2
        UNION ALL SELECT b.path,b.logical,0 FROM folders b LEFT JOIN folders a ON a.snapshot=$a AND a.path=b.path WHERE b.snapshot=$b AND b.root=$r AND b.depth BETWEEN $d+1 AND $d+2 AND a.path IS NULL)
        WHERE x<>y ORDER BY ABS(y-x) DESC,p LIMIT $n;
        """, ("$a", after), ("$b", before), ("$r", normalized), ("$d", depth), ("$n", limit));
        using var reader = command.ExecuteReader();
        var result = new List<FolderChange>();
        while (reader.Read())
            result.Add(new(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        return result;
    }
    public string? Setting(string key) => Scalar("SELECT value FROM settings WHERE key=$k;", ("$k", key)) as string;
    public void SaveSetting(string key, string value) => Execute("INSERT OR REPLACE INTO settings(key,value) VALUES($k,$v);", ("$k", key), ("$v", value));
    public void DeleteProfile(string name) => Execute("DELETE FROM profiles WHERE name=$name;", ("$name", name));
    public void SaveProfile(ScanProfile profile) => Execute("INSERT OR REPLACE INTO profiles VALUES($name,$json);", ("$name", profile.Name), ("$json", JsonSerializer.Serialize(profile)));
    public List<ScanProfile> Profiles()
    {
        using var command = Command("SELECT json FROM profiles ORDER BY name;");
        using var reader = command.ExecuteReader();
        var result = new List<ScanProfile>();
        while (reader.Read())
            result.Add(JsonSerializer.Deserialize<ScanProfile>(reader.GetString(0))!);
        return result;
    }
    public void SaveCleanup(CleanupRecord record) => Execute("INSERT OR REPLACE INTO cleanup VALUES($id,$o,$d,$identity,$state,$time,$error);", ("$id", record.Id), ("$o", record.Original), ("$d", record.Destination), ("$identity", record.Identity), ("$state", record.State), ("$time", record.Time), ("$error", record.Error));
    public List<CleanupRecord> CleanupHistory()
    {
        using var command = Command("SELECT id,original,destination,identity,state,time,error FROM cleanup ORDER BY time DESC LIMIT 1000;");
        using var reader = command.ExecuteReader();
        var result = new List<CleanupRecord>();
        while (reader.Read())
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        return result;
    }
    public void Export(long snapshot, string path, bool json)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var command = Command($"SELECT {EntryColumns} FROM entries e WHERE snapshot=$s ORDER BY e.path;", ("$s", snapshot));
        using var reader = command.ExecuteReader();
        if (json)
        {
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartArray();
            while (reader.Read())
                JsonSerializer.Serialize(writer, Entry(reader));
            writer.WriteEndArray();
        }
        else
        {
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(true));
            writer.WriteLine("Path,Directory,LogicalBytes,AllocatedBytes,Identity,ModifiedUtc");
            while (reader.Read())
            {
                var item = Entry(reader);
                writer.WriteLine($"{Csv(item.Path)},{item.IsDirectory},{item.Length},{item.Allocated?.ToString() ?? ""},{Csv(item.Identity ?? "")},{(item.ModifiedTicks > 0 ? new DateTime(item.ModifiedTicks, DateTimeKind.Utc).ToString("O") : "")}");
            }
        }
    }
    private static string Csv(string value) => "\"" + (value.Length > 0 && "=+-@".Contains(value[0]) ? "'" : "") + value.Replace("\"", "\"\"") + "\"";
    private static string EscapeLike(string value) => value.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");
    private SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        commandCancellation.ThrowIfCancellationRequested();
        // A literal snapshot constraint lets SQLite prove the per-snapshot partial-index predicate.
        var snapshot = parameters.FirstOrDefault(x => x.Name == "$s");
        if (snapshot.Value is long or int)
            sql = System.Text.RegularExpressions.Regex.Replace(sql, @"\$s\b", Convert.ToInt64(snapshot.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var item in parameters)
            if (item.Name != "$s" || snapshot.Value is not (long or int))
                command.Parameters.AddWithValue(item.Name, item.Value ?? DBNull.Value);
        return command;
    }
    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        commandCancellation.ThrowIfCancellationRequested();
        using var command = Command(sql, parameters);
        command.ExecuteNonQuery();
    }
    private object? Scalar(string sql, params (string Name, object? Value)[] parameters)
    {
        commandCancellation.ThrowIfCancellationRequested();
        using var command = Command(sql, parameters);
        var result = command.ExecuteScalar();
        return result == DBNull.Value ? null : result;
    }
    public void Dispose() => connection.Dispose();
}
