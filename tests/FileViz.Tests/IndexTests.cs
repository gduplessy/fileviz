using System.Text.Json;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using Xunit;
namespace FileViz.Tests;

public class IndexTests
{
    private static async Task<long> Scan(IndexStore store,params string[] roots)
    {
        var id=store.CreateSnapshot(roots);foreach(var root in roots)await foreach(var batch in new DirectoryScanEngine().ScanAsync(new(root,[])))store.AddBatch(id,batch);store.Finish(id,"Complete");return id;
    }
    [Fact] public async Task DriveScopeFiltersTotalsEntriesAndExtensions()
    {
        using var fixture=new Fixture();var first=System.IO.Path.Combine(fixture.Root,"one");var second=System.IO.Path.Combine(fixture.Root,"two");fixture.Write("one/a.bin","123");fixture.Write("two/b.txt","1234567");
        using var store=new IndexStore(System.IO.Path.Combine(fixture.Root,"index.db"));var id=await Scan(store,first,second);
        Assert.Contains(store.PrimaryQueryPlan(id,first),x=>x.Contains($"entry_{id}_size",StringComparison.Ordinal));
        Assert.Equal(3,store.GetSummary(id,first).Logical);Assert.Equal(7,store.GetSummary(id,second).Logical);
        Assert.Equal("a.bin",Assert.Single(store.Query(id,new(Root:first))).Name);Assert.Equal(".txt",Assert.Single(store.Extensions(id,second)).Name);
    }
    [Fact] public async Task DuplicateStagingSeparatesSamplesFromVerifiedContentAndOverlappingHistory()
    {
        using var fixture=new Fixture();var data=System.IO.Path.Combine(fixture.Root,"data");fixture.Write("data/one/a.bin","abc");fixture.Write("data/two/A.BIN","abc");
        using var store=new IndexStore(System.IO.Path.Combine(fixture.Root,"index.db"));var first=await Scan(store,data);var second=await Scan(store,data);
        store.FindNameDuplicates(second,[first,second],[data]);Assert.Equal(2,store.Duplicates(second).Count);Assert.Equal(0,store.DuplicatePotential(second));
        store.PrepareContentWork([first,second],[data]);var candidates=store.WorkCandidates(false).ToArray();Assert.Equal(2,candidates.Length);
        foreach(var entry in candidates)store.SaveWorkHash(new(entry.Path,"SAMPLE",entry.Identity,entry.Length,entry.ModifiedTicks,entry.ChangeTicks,null),false);
        store.FinishContentWork(second,"SHA-256","");Assert.Empty(store.Duplicates(second));
        Assert.Equal(2,store.WorkCandidates(true).Count());
        foreach(var entry in candidates)store.SaveWorkHash(new(entry.Path,"FULL",entry.Identity,entry.Length,entry.ModifiedTicks,entry.ChangeTicks,null),true);
        store.FinishContentWork(second,"SHA-256",candidates[0].Parent);var duplicates=store.Duplicates(second);Assert.Equal(2,duplicates.Count);Assert.Single(duplicates,x=>x.SuggestedKeeper);Assert.Equal(3,store.DuplicatePotential(second));
    }
    [Fact] public async Task CompareAndExportDoNotLoseOrOverwriteData()
    {
        using var fixture=new Fixture();var data=System.IO.Path.Combine(fixture.Root,"data");fixture.Write("data/grown.txt","a");var removed=fixture.Write("data/removed.txt","x");
        using var store=new IndexStore(System.IO.Path.Combine(fixture.Root,"index.db"));var before=await Scan(store,data);File.AppendAllText(System.IO.Path.Combine(data,"grown.txt"),"bc");File.Delete(removed);fixture.Write("data/new.txt","n");var after=await Scan(store,data);
        var differences=store.Compare(before,after);Assert.Contains(differences,x=>x.Change=="Added");Assert.Contains(differences,x=>x.Change=="Removed");Assert.Contains(differences,x=>x.Change=="Grown");
        var report=System.IO.Path.Combine(fixture.Root,"report.json");store.Export(after,report,true);using var json=JsonDocument.Parse(File.ReadAllText(report));Assert.Equal(2,json.RootElement.GetArrayLength());Assert.Throws<IOException>(()=>store.Export(after,report,true));
    }
    [Fact] public void OpeningAnotherConnectionDoesNotInterruptActiveSnapshot()
    {
        using var fixture=new Fixture();var db=System.IO.Path.Combine(fixture.Root,"index.db");using var writer=new IndexStore(db);var id=writer.CreateSnapshot([fixture.Root]);using(var reader=new IndexStore(db))Assert.Equal("Scanning",Assert.Single(reader.Snapshots()).State);
        writer.RecoverInterrupted();Assert.Equal("Interrupted",Assert.Single(writer.Snapshots()).State);writer.SetState(id,"Cancelled");
    }
}