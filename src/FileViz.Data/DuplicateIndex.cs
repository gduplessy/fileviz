using FileViz.Core;
namespace FileViz.Data;

public sealed partial class IndexStore
{
    public void PrepareContentWork(long[] snapshots, string[]? roots=null)
    {
        Execute("""
        CREATE TABLE IF NOT EXISTS duplicate_work(path TEXT PRIMARY KEY,parent TEXT NOT NULL,name TEXT NOT NULL,identity TEXT,isdir INTEGER NOT NULL,length INTEGER NOT NULL,allocated INTEGER,modified INTEGER NOT NULL,changed INTEGER NOT NULL,attrs INTEGER NOT NULL,source INTEGER NOT NULL,sample TEXT,hash TEXT);
        CREATE INDEX IF NOT EXISTS work_sample ON duplicate_work(length,sample);
        CREATE INDEX IF NOT EXISTS work_hash ON duplicate_work(length,hash);
        DELETE FROM duplicate_work;
        """);
        var ids=string.Join(',',snapshots);if(ids.Length==0)return;
        var rootSql=roots is { Length:>0 }?" AND root IN("+string.Join(',',roots.Select((_,i)=>"$root"+i))+")":"";
        var parameters=roots?.Select((value,i)=>("$root"+i,(object?)value)).ToArray()??[];
        Execute($"""
        INSERT INTO duplicate_work(path,parent,name,identity,isdir,length,allocated,modified,changed,attrs,source)
        SELECT e.path,e.parent,e.name,e.identity,e.isdir,e.length,e.allocated,e.modified,e.changed,e.attrs,e.snapshot FROM entries e
        WHERE e.snapshot IN({ids}) AND e.isdir=0 AND e.length>0 {rootSql} AND e.snapshot=(SELECT MAX(s.snapshot) FROM entries s WHERE s.path=e.path AND s.snapshot IN({ids}) {rootSql})
        GROUP BY COALESCE(e.identity,e.path);
        """,parameters);
    }
    public IEnumerable<FileEntry> WorkCandidates(bool fullHash)
    {
        using var command=Command($"""
        SELECT {EntryColumns} FROM duplicate_work e WHERE {(fullHash ? "e.sample IS NOT NULL AND (e.length,e.sample) IN(SELECT length,sample FROM duplicate_work WHERE sample IS NOT NULL GROUP BY length,sample HAVING COUNT(*)>1)" : "e.length IN(SELECT length FROM duplicate_work GROUP BY length HAVING COUNT(*)>1)")} ORDER BY e.length,e.path;
        """);
        using var reader=command.ExecuteReader();while(reader.Read())yield return Entry(reader);
    }
    public void SaveWorkHash(HashResult result,bool fullHash)
    { Execute($"UPDATE duplicate_work SET {(fullHash?"hash":"sample")}=$hash WHERE path=$path;",("$hash",result.Hash),("$path",result.Path)); }
    public void FinishContentWork(long destination,string algorithm,string preferredFolder)
    {
        ClearDuplicates(destination);
        var prefix=preferredFolder.Length==0?"":EscapeLike(Paths.Normalize(preferredFolder)+System.IO.Path.DirectorySeparatorChar)+"%";
        Execute("""
        INSERT OR REPLACE INTO duplicates
        SELECT $s,DENSE_RANK() OVER(ORDER BY length,hash),path,$e,
        CASE WHEN ROW_NUMBER() OVER(PARTITION BY length,hash ORDER BY CASE WHEN $pref<>'' AND path LIKE $pref ESCAPE '!' THEN 0 ELSE 1 END,modified DESC,path)=1 THEN 1 ELSE 0 END,source
        FROM duplicate_work WHERE hash IS NOT NULL AND (length,hash) IN(SELECT length,hash FROM duplicate_work WHERE hash IS NOT NULL GROUP BY length,hash HAVING COUNT(*)>1);
        """,("$s",destination),("$e",algorithm+" content"),("$pref",prefix));
    }
    public long DuplicatePotential(long snapshot) => Convert.ToInt64(Scalar("SELECT COALESCE(SUM(e.length),0) FROM duplicates d JOIN entries e ON e.snapshot=d.source AND e.path=d.path WHERE d.snapshot=$s AND d.keeper=0 AND d.evidence<>'Name candidate';",("$s",snapshot)));
    public bool IsDirectory(long snapshot,string path) => Convert.ToInt64(Scalar("SELECT COALESCE(isdir,0) FROM entries WHERE snapshot=$s AND path=$p;",("$s",snapshot),("$p",path))??0)!=0;
    public List<string> ParityDifferences(long directory,long mft)
    {
        using var command=Command("""
        SELECT d.path||': missing or different raw metadata' FROM entries d LEFT JOIN entries m ON m.snapshot=$m AND m.path=d.path WHERE d.snapshot=$d AND (m.path IS NULL OR d.isdir<>m.isdir OR d.length<>m.length OR d.allocated<>m.allocated OR d.identity<>m.identity OR d.modified<>m.modified OR d.changed<>m.changed OR d.attrs<>m.attrs)
        UNION ALL SELECT m.path||': missing from directory enumeration' FROM entries m LEFT JOIN entries d ON d.snapshot=$d AND d.path=m.path WHERE m.snapshot=$m AND d.path IS NULL LIMIT 100;
        """,("$d",directory),("$m",mft));using var reader=command.ExecuteReader();var result=new List<string>();while(reader.Read())result.Add(reader.GetString(0));return result;
    }
    public void AddError(long snapshot,string path,string message) => Execute("INSERT INTO errors VALUES($s,$p,$m);",("$s",snapshot),("$p",path),("$m",message));
}