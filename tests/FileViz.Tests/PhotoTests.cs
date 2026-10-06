using System.Buffers.Binary;
using System.IO.Compression;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using Xunit;

namespace FileViz.Tests;

public class PhotoTests
{
    [Fact]
    public void ResolutionFiltersAreOrientationIndependentAndNeverMatchErrors()
    {
        var entry = new FileEntry(@"C:\fixture\a.png", @"C:\fixture", "a.png", "id", false, 10, 10, 1, 2, 0);
        var landscape = new PhotoInfo(entry, 1200, 600);
        var portrait = new PhotoInfo(entry, 600, 1200);
        Assert.True(new PhotoFilter().Matches(landscape));
        Assert.True(new PhotoFilter().Matches(portrait));
        Assert.False(new PhotoFilter(600).Matches(portrait));
        Assert.True(new PhotoFilter(0, 1).Matches(landscape));
        Assert.False(new PhotoFilter(0, 0).Matches(landscape));
        Assert.False(new PhotoFilter().Matches(landscape with { Error = "Unreadable" }));
        Assert.True(new PhotoFilter(ErrorsOnly: true).Matches(landscape with { Error = "Unreadable" }));
        Assert.Throws<ArgumentException>(() => new PhotoFilter(MinimumMegapixels: double.NaN).Validate());
        Assert.Throws<ArgumentException>(() => new PhotoFilter(-1).Validate());
        Assert.False(landscape.CacheMatches(entry with { ChangeTicks = 0 }));
        Assert.False(landscape.CacheMatches(entry with { Identity = "other" }));
        Assert.True(landscape.CacheMatches(entry));
    }
    [Fact]
    public async Task CodecWorkerReadsHeadersPreviewsAndRejectsChangedOrUnreadableImages()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "photo.png");
        WritePng(path, 320, 200);
        var entry = Native.ReadEntry(path);
        await using var worker = await WorkerSession.StartAsync(false);
        async Task<PhotoInfo> Read(PhotoRequest request, bool preview = false)
        {
            PhotoInfo? result = null;
            await worker.ExecuteAsync(new(preview ? "photo-preview" : "photo", Photos: [request]), message => { result = message.Photo; return Task.CompletedTask; });
            return Assert.IsType<PhotoInfo>(result);
        }
        var photo = await Read(new(entry));
        Assert.Null(photo.Error); Assert.Equal(320, photo.Width); Assert.Equal(200, photo.Height); Assert.Null(photo.Preview);
        var cached = await Read(new(entry, photo));
        Assert.Equal(photo, cached);
        var preview = await Read(new(entry), true);
        Assert.Null(preview.Error); Assert.NotEmpty(preview.Preview!); Assert.True(preview.Preview!.Length < 1024 * 1024);
        WritePng(path, 800, 1200);
        var changed = await Read(new(entry, photo));
        Assert.Contains("changed", changed.Error, StringComparison.OrdinalIgnoreCase);
        var fresh = await Read(new(Native.ReadEntry(path), photo));
        Assert.Null(fresh.Error); Assert.Equal(800, fresh.Width); Assert.Equal(1200, fresh.Height);
        File.WriteAllText(path, "this is not an image");
        Assert.NotNull((await Read(new(Native.ReadEntry(path)))).Error);
        Assert.NotNull((await Read(new(entry with { Attributes = 0x1000 }))).Error);
        Assert.NotNull((await Read(new(entry with { Path = @"\\.\C:" }))).Error);
    }
    [Fact]
    public async Task PhotoIndexPagesByRootPersistsMetadataAndSeparatesErrors()
    {
        using var fixture = new Fixture();
        using var store = new IndexStore(Path.Combine(fixture.Root, "index.db"));
        var first = Path.Combine(fixture.Root, "first"); var second = Path.Combine(fixture.Root, "second");
        Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        var snapshot = store.CreateSnapshot([first, second]);
        var entries = Enumerable.Range(0, 205).Select(i => new FileEntry(Path.Combine(first, $"{i:000}.png"), first, $"{i:000}.png", $"id-{i}", false, 10, 4096, 1, 2, 0)).ToArray();
        store.AddBatch(snapshot, new(first, "fixture", entries, []));
        var other = entries[0] with { Path = Path.Combine(second, "other.png"), Parent = second, Name = "other.png", Identity = "other" };
        store.AddBatch(snapshot, new(second, "fixture", [other], []));
        store.PreparePhotos(snapshot);
        Assert.Equal(205, store.PhotoCandidateCount(snapshot, first));
        var batch = store.PhotoCandidates(snapshot, first);
        Assert.Equal(64, batch.Count);
        Assert.Equal(entries[64].Path, store.PhotoCandidates(snapshot, first, batch[^1].Entry.Path)[0].Entry.Path);
        store.SavePhotos(snapshot, entries.Select(e => new PhotoInfo(e, 320, 200)));
        store.SavePhotos(snapshot, [new(other, 160, 100)]);
        store.SavePhotos(snapshot, [new(entries[204], 0, 0, Error: "Bad header")]);
        Assert.Equal((205L, 1L, 204L), store.PhotoSummary(snapshot, first, new()));
        Assert.Equal(100, store.QueryPhotos(snapshot, first, new()).Count);
        Assert.Equal(4, store.QueryPhotos(snapshot, first, new(), 2).Count);
        Assert.Single(store.QueryPhotos(snapshot, second, new()));
        Assert.Single(store.QueryPhotos(snapshot, first, new(ErrorsOnly: true)));
        Assert.NotNull(store.PhotoCandidates(snapshot, first)[0].Cached);
        Assert.Empty(store.QueryPhotos(snapshot, first, new(200)));
        Assert.Single(store.QueryPhotos(snapshot, first, new(Search: "000.png")));
    }

    [Fact]
    public async Task PhotoCleanupUsesManualReviewAndRejectsChangedInventory()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "small.png"); WritePng(path, 320, 200);
        var entry = Native.ReadEntry(path);
        var original = File.ReadAllBytes(path);
        var service = new CleanupService(_ => { });
        var check = await CleanupService.PrecheckAsync(new(entry));
        Assert.True(check.Ready); Assert.Equal(CheckState.NotChecked, check.Bytes);
        var quarantined = await service.QuarantineAsync(new(entry));
        Assert.Equal("Quarantined", quarantined.State); Assert.False(File.Exists(path));
        Assert.Equal(original, File.ReadAllBytes(quarantined.Destination));
        Assert.Equal("Restored", service.Restore(quarantined).State); Assert.Equal(original, File.ReadAllBytes(path));
        WritePng(path, 1200, 800);
        Assert.False((await CleanupService.PrecheckAsync(new(entry))).Ready);
        var rejected = await service.QuarantineAsync(new(entry));
        Assert.Equal("Failed", rejected.State); Assert.True(File.Exists(path));
    }

    // Valid PNG fixtures without image-library or package dependencies.
    private static void WritePng(string path, int width, int height)
    {
        using var file = File.Create(path);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height); header[8] = 8; header[9] = 2;
        Chunk(file, "IHDR", header);
        using var data = new MemoryStream();
        using (var zlib = new ZLibStream(data, CompressionLevel.Fastest, true))
        {
            var row = new byte[1 + width * 3];
            for (var i = 1; i < row.Length; i += 3) { row[i] = 25; row[i + 1] = 160; row[i + 2] = 180; }
            for (var y = 0; y < height; y++) zlib.Write(row);
        }
        Chunk(file, "IDAT", data.ToArray()); Chunk(file, "IEND", []);
    }
    private static void Chunk(Stream file, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, data.Length); file.Write(number);
        var name = System.Text.Encoding.ASCII.GetBytes(type); file.Write(name); file.Write(data);
        var crc = uint.MaxValue;
        foreach (var b in name.Concat(data))
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); file.Write(number);
    }
}
