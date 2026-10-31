using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FileViz.Tests;

public class PhotoCoverageTests
{
    [Fact]
    public void BulkSelectionReadsAcrossPagesButStaysBoundedAndScoped()
    {
        using var fixture = new Fixture();
        using var store = new IndexStore(Path.Combine(fixture.Root, "selection.db"));
        var root = Path.Combine(fixture.Root, "photos");
        var other = Path.Combine(fixture.Root, "other");
        var snapshot = store.CreateSnapshot([root, other]);
        FileEntry Entry(string folder, string name) => new(Path.Combine(folder, name), folder, name, name, false, 14, 14, 1, 2, 0);
        var entries = Enumerable.Range(0, 5002).Select(i => Entry(root, $"match-{i:D5}.png")).ToArray();
        var neighbor = Entry(other, "match-other.png");
        var unreadable = Entry(root, "unreadable.png");
        store.AddBatch(snapshot, new(root, "Fixture", [.. entries, unreadable], []));
        store.AddBatch(snapshot, new(other, "Fixture", [neighbor], []));
        store.SavePhotos(snapshot, entries.Select(e => new PhotoInfo(e, 200, 200)).Concat([new(neighbor, 200, 200), new(unreadable, 0, 0, Error: "Bad header")]));
        Assert.Equal(100, store.QueryPhotos(snapshot, root, new()).Count);
        var candidates = store.PhotoSelectionCandidates(snapshot, root, new());
        Assert.Equal(5001, candidates.Count);
        Assert.All(candidates, photo => Assert.Equal(root, photo.Entry.Parent));
        Assert.All(candidates, photo => Assert.Null(photo.Error));
        Assert.Single(store.PhotoSelectionCandidates(snapshot, root, new(Search: "match-00000.png")));
        Assert.Single(store.PhotoSelectionCandidates(snapshot, other, new()));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => store.PhotoSelectionCandidates(snapshot, root, new(), cancellation.Token));
    }

    [Fact]
    public void EmptyAndPartialAnalysisNeverImplyThatUnattemptedImagesPassed()
    {
        Assert.Contains("Not analyzed yet", new PhotoCoverage(12, 0, 0, 0).Description);
        Assert.Contains("image count unavailable", new PhotoCoverage(null, 0, 0, 0).Description);
        Assert.Contains("No supported image filenames", new PhotoCoverage(0, 0, 0, 0).Description);
        var partial = new PhotoCoverage(12, 5, 1, 0);
        Assert.Equal(7, partial.Unanalyzed);
        Assert.Contains("Incomplete analysis", partial.Description);
        Assert.Contains("7 images not analyzed", partial.Description);
        Assert.Contains("All saved candidates attempted", new PhotoCoverage(12, 12, 1, 3).Description);
        Assert.Contains("Coverage is unknown", new PhotoCoverage(null, 2, 0, 1).Description);
    }

    [Fact]
    public async Task SmallSquareImageAppearsAfterActualWorkerAnalysisRatherThanMetadataScan()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "missed-thumbnail.png");
        PhotoTests.WritePng(path, 200, 200);
        var database = Path.Combine(fixture.Root, "review.db");
        using var store = new IndexStore(database);
        var snapshot = store.CreateSnapshot([fixture.Root]);
        await foreach (var batch in new DirectoryScanEngine().ScanAsync(new(fixture.Root, []))) store.AddBatch(snapshot, batch);
        Assert.Null(store.SavedPhotoCandidateCount(snapshot, fixture.Root));
        store.Finish(snapshot, "Complete");
        Assert.Equal(1, store.SavedPhotoCandidateCount(snapshot, fixture.Root));
        Assert.Empty(store.QueryPhotos(snapshot, fixture.Root, new()));
        store.PreparePhotos(snapshot);
        var request = Assert.Single(store.PhotoCandidates(snapshot, fixture.Root));
        Assert.Null(request.Cached);
        await using var worker = await WorkerSession.StartAsync(false);
        PhotoInfo? result = null;
        await worker.ExecuteAsync(new("photo", Photos: [request]), message => { result = message.Photo; return Task.CompletedTask; });
        Assert.NotNull(result); Assert.Null(result.Error);
        Assert.Equal(200, result.Width); Assert.Equal(200, result.Height);
        Assert.True(new PhotoFilter().Matches(result));
        store.SavePhotos(snapshot, [result]);
        var match = Assert.Single(store.QueryPhotos(snapshot, fixture.Root, new(Search: "missed-thumbnail")));
        Assert.Equal(path, match.Entry.Path);
        Assert.Equal((1L, 0L, 1L), store.PhotoSummary(snapshot, fixture.Root, new()));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type='index' AND name GLOB 'entry_*_photos';";
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void CandidateRangeKeepsNestedUnicodePathsAndExcludesNeighborScopes()
    {
        using var fixture = new Fixture();
        using var store = new IndexStore(Path.Combine(fixture.Root, "index.db"));
        var root = Path.Combine(fixture.Root, "photos[1]");
        var neighbor = root + "-other";
        var snapshot = store.CreateSnapshot([root, neighbor]);
        FileEntry Entry(string folder, string name) => new(Path.Combine(folder, name), folder, name, null, false, 14, 14, 1, 2, 0);
        var entries = new[] { Entry(root, "200.png"), Entry(Path.Combine(root, "nested"), "portrait-😀.PNG") };
        store.AddBatch(snapshot, new(root, "Fixture", entries, []));
        store.AddBatch(snapshot, new(neighbor, "Fixture", [Entry(neighbor, "other.png")], []));
        Assert.Equal(2, store.PhotoCandidateCount(snapshot, root));
        var first = Assert.Single(store.PhotoCandidates(snapshot, root, limit: 1));
        var second = Assert.Single(store.PhotoCandidates(snapshot, root, first.Entry.Path, 1));
        Assert.NotEqual(first.Entry.Path, second.Entry.Path);
        Assert.Empty(store.PhotoCandidates(snapshot, root, second.Entry.Path));
        Assert.Single(store.PhotoCandidates(snapshot, neighbor));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => store.PreparePhotos(snapshot, cancellation.Token));
    }
}
