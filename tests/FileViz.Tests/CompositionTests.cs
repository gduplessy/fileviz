using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using Microsoft.Data.Sqlite;
using Xunit;
namespace FileViz.Tests;

public class CompositionTests
{
    [Theory]
    [InlineData(".MP4", FileCategory.Video)]
    [InlineData(".vhdx", FileCategory.DiskImage)]
    [InlineData(".msi", FileCategory.Archive)]
    [InlineData(".exe", FileCategory.Other)]
    [InlineData("", FileCategory.Other)]
    public void ExtensionsMapToCategories(string extension, FileCategory expected) => Assert.Equal(expected, FileCategories.Of(extension));
    [Fact]
    public void AgeBucketsAreRelativeToTheReferenceTime()
    {
        var reference = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        Assert.Equal(0, AgeBuckets.Of(reference - TimeSpan.FromDays(1).Ticks, reference));
        Assert.Equal(1, AgeBuckets.Of(reference - TimeSpan.FromDays(60).Ticks, reference));
        Assert.Equal(2, AgeBuckets.Of(reference - TimeSpan.FromDays(200).Ticks, reference));
        Assert.Equal(3, AgeBuckets.Of(reference - TimeSpan.FromDays(400).Ticks, reference));
        Assert.Equal(4, AgeBuckets.Of(reference - TimeSpan.FromDays(4000).Ticks, reference));
        Assert.Equal(4, AgeBuckets.Of(0, reference));
        Assert.Equal(-1, AgeBuckets.Weighted([0, 0, 0, 0, 0]));
        Assert.Equal(1, AgeBuckets.Weighted([10, 0, 10, 0, 0]));
        Assert.Equal(4, AgeBuckets.Weighted([1, 0, 0, 0, 99]));
    }
    [Fact]
    public void SquarifiedLayoutIsProportionalAndCoversTheBounds()
    {
        double[] values = [500, 250, 120, 80, 30, 15, 5, 0];
        var bounds = new TreemapRect(10, 20, 640, 360);
        var rects = TreemapLayout.Squarify(values, bounds);
        Assert.Equal(values.Length, rects.Length);
        Assert.Equal(default, rects[^1]);
        var total = values.Sum();
        for (var i = 0; i < values.Length - 1; i++)
        {
            var r = rects[i];
            Assert.Equal(values[i] / total * bounds.Width * bounds.Height, r.Width * r.Height, 3);
            Assert.True(r.X >= bounds.X - 1e-6 && r.Y >= bounds.Y - 1e-6 && r.X + r.Width <= bounds.X + bounds.Width + 1e-6 && r.Y + r.Height <= bounds.Y + bounds.Height + 1e-6);
            for (var j = i + 1; j < values.Length - 1; j++)
            {
                var o = rects[j];
                var overlapX = Math.Min(r.X + r.Width, o.X + o.Width) - Math.Max(r.X, o.X);
                var overlapY = Math.Min(r.Y + r.Height, o.Y + o.Height) - Math.Max(r.Y, o.Y);
                Assert.False(overlapX > 1e-6 && overlapY > 1e-6, $"Tiles {i} and {j} overlap");
            }
        }
        Assert.All(TreemapLayout.Squarify([], bounds), _ => Assert.Fail("Empty input yields no tiles."));
    }
    [Fact]
    public async Task FolderCompositionEqualsTheSumOfItsFiles()
    {
        using var fixture = new Fixture();
        var video = fixture.Write("media/clip.mp4", new string('v', 400));
        var old = fixture.Write("media/archive/old.zip", new string('z', 300));
        fixture.Write("docs/readme.txt", new string('d', 100));
        fixture.Write("docs/tool.exe", new string('e', 50));
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddYears(-5));
        File.SetLastWriteTimeUtc(video, DateTime.UtcNow.AddDays(-2));
        using var data = new Fixture();
        using var store = new IndexStore(Path.Combine(data.Root, "index.db"));
        var id = store.CreateSnapshot([fixture.Root]);
        await foreach (var batch in new DirectoryScanEngine().ScanAsync(new(fixture.Root, [])))
            store.AddBatch(id, batch);
        store.Finish(id, "Complete");
        Assert.True(store.HasComposition(id));
        var root = store.FolderComposition(id, Paths.Normalize(fixture.Root))!;
        Assert.Equal(400, root.Categories[(int)FileCategory.Video]);
        Assert.Equal(300, root.Categories[(int)FileCategory.Archive]);
        Assert.Equal(100, root.Categories[(int)FileCategory.Document]);
        Assert.Equal(50, root.Categories[(int)FileCategory.Other]);
        Assert.Equal(850, root.Categories.Sum());
        Assert.Equal(850, root.Ages.Sum());
        Assert.Equal(300, root.Ages[4]);
        Assert.Equal(550, root.Ages[0]);
        var media = store.FolderComposition(id, Path.Combine(Paths.Normalize(fixture.Root), "media"))!;
        Assert.Equal(700, media.Categories.Sum());
        Assert.Equal(FileCategory.Video, media.Dominant);
        var map = store.SpaceMap(id, Paths.Normalize(fixture.Root), false);
        Assert.Equal(["media", "docs"], map.Select(x => x.Name));
        Assert.Contains(map[0].Children, x => x.Name == "archive" && x.IsDirectory && x.Bytes == 300);
        Assert.Contains(map[0].Children, x => x.Name == "clip.mp4" && !x.IsDirectory && x.Composition.Dominant == FileCategory.Video && x.Composition.AgeBucket == 0);
        SqliteConnection.ClearAllPools();
    }
    [Fact]
    public void OlderDatabasesGainCompositionColumnsWithoutCompositionData()
    {
        using var fixture = new Fixture();
        var database = Path.Combine(fixture.Root, "legacy.db");
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
            CREATE TABLE snapshots(id INTEGER PRIMARY KEY,roots TEXT NOT NULL,started TEXT NOT NULL,state TEXT NOT NULL,files INTEGER NOT NULL DEFAULT 0,logical INTEGER NOT NULL DEFAULT 0,allocated INTEGER,errors INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE folders(snapshot INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,path TEXT NOT NULL,parent TEXT NOT NULL,depth INTEGER NOT NULL,root TEXT NOT NULL,logical INTEGER NOT NULL DEFAULT 0,allocated INTEGER NOT NULL DEFAULT 0,files INTEGER NOT NULL DEFAULT 0,PRIMARY KEY(snapshot,path));
            INSERT INTO snapshots(id,roots,started,state) VALUES(1,'["C:\\"]','2026-01-01T00:00:00.0000000Z','Complete');
            INSERT INTO folders VALUES(1,'C:\','C:\',1,'C:\',100,100,1);
            """;
            command.ExecuteNonQuery();
        }
        using var store = new IndexStore(database);
        Assert.False(store.HasComposition(1));
        Assert.Null(store.FolderComposition(1, @"C:\"));
        Assert.Empty(store.SpaceMap(1, @"C:\", false));
        SqliteConnection.ClearAllPools();
    }
}
