using FileViz.Core;
using FileViz.Windows;
using Xunit;
namespace FileViz.Tests;

public class CleanupTests
{
    [Fact] public async Task QuarantineAndRestorePreserveContentAndNeverOverwrite()
    {
        using var fixture=new Fixture();var target=fixture.Write("target.txt","same content");var keeper=fixture.Write("keeper.txt","same content");
        var journal=new List<CleanupRecord>();var service=new CleanupService(journal.Add);
        var result=await service.QuarantineAsync(new(Native.ReadEntry(target),Native.ReadEntry(keeper)));
        Assert.True(result.State=="Quarantined",result.Error);Assert.False(File.Exists(target));Assert.True(File.Exists(keeper));Assert.Equal("same content",File.ReadAllText(result.Destination));
        File.WriteAllText(target,"collision");var failed=service.Restore(result);Assert.NotNull(failed.Error);Assert.Equal("collision",File.ReadAllText(target));
        File.Delete(target);var restored=service.Restore(result);Assert.Equal("Restored",restored.State);Assert.Equal("same content",File.ReadAllText(target));
        Assert.Contains(journal,x=>x.State=="Pending");
    }
    [Fact] public async Task UnequalBytesAndChangedMetadataBlockCleanup()
    {
        using var fixture=new Fixture();var target=fixture.Write("a.txt","abc");var keeper=fixture.Write("b.txt","xyz");var service=new CleanupService(_=>{});
        Assert.Equal("Failed",(await service.QuarantineAsync(new(Native.ReadEntry(target),Native.ReadEntry(keeper)))).State);Assert.True(File.Exists(target));
        var scanned=Native.ReadEntry(target);File.AppendAllText(target,"changed");Assert.Equal("Failed",(await service.QuarantineAsync(new(scanned))).State);
    }
    [Fact] public async Task AlternateStreamsMustMatch()
    {
        using var fixture=new Fixture();var target=fixture.Write("a.txt","abc");var keeper=fixture.Write("b.txt","abc");File.WriteAllText(target+":test","secret");
        var result=await new CleanupService(_=>{}).QuarantineAsync(new(Native.ReadEntry(target),Native.ReadEntry(keeper)));
        Assert.Equal("Failed",result.State);Assert.True(File.Exists(target));
    }
    [Fact] public void SystemPathsAreProtected()
    { Assert.True(CleanupService.Protected(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"example.txt"))); }
    [Fact] public async Task NativeEnumerationAndHandleIdentitiesAgree()
    {
        using var fixture=new Fixture();var path=fixture.Write("a.txt","abc");FileEntry? scanned=null;
        await foreach(var batch in new DirectoryScanEngine().ScanAsync(new(fixture.Root,[])))scanned=batch.Entries.Single();
        Assert.NotNull(scanned);Assert.Equal(Native.ReadEntry(path).Identity,scanned.Identity);
    }
}