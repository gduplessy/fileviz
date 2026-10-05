using FileViz.Core;
using FileViz.Windows;
using Xunit;
namespace FileViz.Tests;

public class PrecheckTests
{
    [Fact]
    public async Task MatchingCopiesPassEveryCheckAndStayInPlace()
    {
        using var fixture = new Fixture();
        var target = fixture.Write("target.txt", "same content");
        var keeper = fixture.Write("keeper.txt", "same content");
        var check = await CleanupService.PrecheckAsync(new(Native.ReadEntry(target), Native.ReadEntry(keeper)));
        Assert.True(check.Ready, check.Error);
        Assert.Equal([CheckState.Passed, CheckState.Passed, CheckState.Passed, CheckState.Passed], new[] { check.Identity, check.Metadata, check.Bytes, check.Streams });
        Assert.True(File.Exists(target));
        // A passing precheck agrees with the move, which revalidates under its own locks.
        var moved = await new CleanupService(_ => { }).QuarantineAsync(check.Selection);
        Assert.Equal("Quarantined", moved.State);
    }
    [Fact]
    public async Task DifferentBytesFailOnlyTheByteCheck()
    {
        using var fixture = new Fixture();
        var target = fixture.Write("a.txt", "abc");
        var keeper = fixture.Write("b.txt", "xyz");
        var check = await CleanupService.PrecheckAsync(new(Native.ReadEntry(target), Native.ReadEntry(keeper)));
        Assert.False(check.Ready);
        Assert.Equal(CheckState.Passed, check.Identity);
        Assert.Equal(CheckState.Passed, check.Metadata);
        Assert.Equal(CheckState.Failed, check.Bytes);
        Assert.Equal(CheckState.NotChecked, check.Streams);
        Assert.Equal("Failed", (await new CleanupService(_ => { }).QuarantineAsync(check.Selection)).State);
    }
    [Fact]
    public async Task ChangedFilesFailMetadataAndManualSelectionsSkipContent()
    {
        using var fixture = new Fixture();
        var target = fixture.Write("a.txt", "abc");
        var scanned = Native.ReadEntry(target);
        var manual = await CleanupService.PrecheckAsync(new(scanned));
        Assert.True(manual.Ready, manual.Error);
        Assert.Equal(CheckState.NotChecked, manual.Bytes);
        File.AppendAllText(target, "changed");
        var changed = await CleanupService.PrecheckAsync(new(scanned));
        Assert.Equal(CheckState.Failed, changed.Metadata);
        Assert.Contains("rescan", changed.Error, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task NamedStreamDifferencesFailTheStreamCheck()
    {
        using var fixture = new Fixture();
        var target = fixture.Write("a.txt", "same");
        var keeper = fixture.Write("b.txt", "same");
        File.WriteAllText(target + ":note", "one");
        var check = await CleanupService.PrecheckAsync(new(Native.ReadEntry(target), Native.ReadEntry(keeper)));
        Assert.Equal(CheckState.Failed, check.Streams);
        Assert.False(check.Ready);
    }
}
