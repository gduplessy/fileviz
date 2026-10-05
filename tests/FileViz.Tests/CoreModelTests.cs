using System.Text.Json;
using FileViz.Core;
using Xunit;
namespace FileViz.Tests;

public class CoreModelTests
{
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
