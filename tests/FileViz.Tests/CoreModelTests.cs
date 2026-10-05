using System.Text.Json;
using FileViz.Core;
using Xunit;
namespace FileViz.Tests;

public class CoreModelTests
{
    [Theory]
    [InlineData(59, "0:59")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(4865, "1:21:05")]
    [InlineData(90061, "25:01:01")]
    public void ElapsedTimeDoesNotWrap(int seconds, string expected) =>
        Assert.Equal(expected, Format.Elapsed(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void FallbackReplacesOnlyItsRootsCountersAndFilesExcludeDirectories()
    {
        var tally = new ScanTally();
        var file = new FileEntry(@"C:\a", @"C:\", "a", "id", false, 100, 128, 0, 0, 32);
        tally.Add(new(@"C:\", "Raw MFT", [file, file with { IsDirectory = true }], []));
        tally.Add(new(@"D:\", "Directory", [file with { Length = 7 }], []));
        Assert.Equal((3L, 2L, 107L), (tally.Entries, tally.Files, tally.Bytes));
        tally.Add(new(@"c:\", "Directory fallback", [], [], Restart: true));
        Assert.Equal((1L, 1L, 7L), (tally.Entries, tally.Files, tally.Bytes));
        tally.Add(new(@"C:\", "Directory", [file with { Length = 3 }], []));
        Assert.Equal((2L, 2L, 10L), (tally.Entries, tally.Files, tally.Bytes));
    }

    [Fact]
    public void ScanProgressIsOptionalOnTheWire()
    {
        var withProgress = new ScanBatch(@"C:\", "Raw MFT", [], [], Progress: new(10, 40));
        var roundTrip = JsonSerializer.Deserialize<ScanBatch>(JsonSerializer.Serialize(withProgress))!;
        Assert.Equal(new ScanProgress(10, 40), roundTrip.Progress);
        // A message from a worker that predates progress reporting still parses.
        var legacy = JsonSerializer.Deserialize<ScanBatch>("""{"Root":"C:\\","Engine":"Directory","Entries":[],"Errors":[],"Restart":false}""")!;
        Assert.Null(legacy.Progress);
    }
}
