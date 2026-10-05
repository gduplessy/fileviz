using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using Xunit;
namespace FileViz.Tests;

public sealed class Fixture : IDisposable
{
    public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FileViz-tests-" + Guid.NewGuid().ToString("N"));
    public Fixture() => Directory.CreateDirectory(Root);
    public string Write(string name, string text)
    {
        var path = System.IO.Path.Combine(Root, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
    public void Dispose()
    {
        if (Root.StartsWith(System.IO.Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            Directory.Delete(Root, true);
    }
}
public class ScannerTests
{
    [Fact]
    public async Task DirectoryMetadataAndSqliteRoundTrip()
    {
        using var fixture = new Fixture();
        fixture.Write("a.bin", "12345");
        fixture.Write("child/b.bin", "12345");
        fixture.Write("child/c.txt", "abcdef");
        using var store = new IndexStore(System.IO.Path.Combine(fixture.Root, "index.db"));
        var snapshot = store.CreateSnapshot([fixture.Root]);
        await foreach (var batch in new DirectoryScanEngine().ScanAsync(new(fixture.Root, ["index.db*"])))
            store.AddBatch(snapshot, batch);
        store.Finish(snapshot, "Complete");
        var summary = store.GetSummary(snapshot);
        Assert.Equal(3, summary.Files);
        Assert.Equal(16, summary.Logical);
        Assert.Equal(0, summary.Errors);
        Assert.Equal("c.txt", store.Query(snapshot, new())[0].Name);
        Assert.Equal(2, store.Query(snapshot, new(Extension: "bin")).Count);
        Assert.Equal(16, store.LargestFolders(snapshot)[0].Bytes);
        Assert.Equal(2, store.ContentCandidates([snapshot]).Count());
    }
    [Fact]
    public async Task ExclusionsAndCancellationAreHonored()
    {
        using var fixture = new Fixture();
        fixture.Write("excluded/a.txt", "a");
        fixture.Write("b.txt", "b");
        var values = new List<FileEntry>();
        await foreach (var batch in new DirectoryScanEngine().ScanAsync(new(fixture.Root, ["excluded"])))
            values.AddRange(batch.Entries);
        Assert.Single(values);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await foreach (var batch in new DirectoryScanEngine().ScanAsync(new(fixture.Root, []), cts.Token)) { } });
    }
    [Fact]
    public async Task HashingRejectsChangedScanMetadataAndMatchesIdenticalFiles()
    {
        using var fixture = new Fixture();
        var a = fixture.Write("a.txt", "identical");
        var b = fixture.Write("b.txt", "identical");
        var ea = Native.ReadEntry(a);
        var eb = Native.ReadEntry(b);
        Assert.Equal((await Hashing.HashAsync(new(ea, "SHA-256"))).Hash, (await Hashing.HashAsync(new(eb, "SHA-256"))).Hash);
        Assert.NotNull((await Hashing.HashAsync(new(ea with
        {
            Identity = "replaced-file"
        }, "SHA-256"))).Error);
        File.AppendAllText(a, "changed");
        Assert.NotNull((await Hashing.HashAsync(new(ea, "SHA-256"))).Error);
    }
    [Fact] public void OverlappingRootsAreDeduplicated() => Assert.Single(Paths.DistinctRoots([@"C:\data", @"C:\data\child", @"C:\DATA"]));
    [Fact]
    public async Task IpcRejectsOversizedFrames()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(Wire.MaximumFrame + 1));
        await Assert.ThrowsAsync<InvalidDataException>(() => Wire.ReadAsync<WorkerMessage>(stream));
    }
    [Fact]
    public void WholeDriveRootsContainDescendantsAndCollapseOverlappingFolders()
    {
        Assert.True(Paths.Within(@"C:\data\child", @"C:\"));
        Assert.False(Paths.Within(@"C:\database", @"C:\data"));
        Assert.Single(Paths.DistinctRoots([@"C:\", @"C:\data"]));
    }
}
