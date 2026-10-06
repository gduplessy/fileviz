using System.Diagnostics;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using Microsoft.Data.Sqlite;
using Xunit;
namespace FileViz.Tests;

public class DuplicateActivityTests
{
    [Theory]
    [InlineData(false, 8388608)]
    [InlineData(true, 196608)]
    public async Task WorkerReportsActualReadBytesBeforePublishingHash(bool sample, long total)
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "hash.bin");
        using (var file = File.Create(path)) file.SetLength(8388608);
        var entry = Native.ReadEntry(path);
        var updates = new List<HashProgress>();
        HashResult? result = null;
        await using var worker = await WorkerSession.StartAsync(false);
        await worker.ExecuteAsync(new("hash", Hashes: [new(entry, "SHA-256", sample)]), message =>
        {
            if (message.HashProgress is { } progress) { Assert.Null(result); updates.Add(progress); }
            if (message.Hash != null) result = message.Hash;
            return Task.CompletedTask;
        });
        Assert.NotNull(result?.Hash);
        Assert.Equal(0, updates.First().BytesRead);
        Assert.Equal(total, updates.Last().BytesRead);
        Assert.All(updates, update => { Assert.Equal(path, update.Path); Assert.Equal(total, update.TotalBytes); });
        Assert.True(updates.Zip(updates.Skip(1), (first, next) => first.BytesRead <= next.BytesRead).All(x => x));
        Assert.Equal((await Hashing.HashAsync(new(entry, "SHA-256", sample))).Hash, result!.Hash);
    }

    [Fact]
    public void DatabaseExecutionReportsActivityAndCancelsInsideAnExecutingStatement()
    {
        using var fixture = new Fixture();
        var database = Path.Combine(fixture.Root, "progress.db");
        using var store = new IndexStore(database);
        var id = store.CreateSnapshot([fixture.Root]);
        store.AddBatch(id, new(fixture.Root, "Fixture", Enumerable.Range(0, 100).Select(i => new FileEntry(
            Path.Combine(fixture.Root, i + ".bin"), fixture.Root, i + ".bin", i.ToString(), false, 17, 4096, 1, 1, 32)).ToArray(), []));
        store.PrepareContentWork([id]);
        using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER expensive_fixture BEFORE INSERT ON duplicate_work BEGIN SELECT COUNT(*) FROM entries a,entries b,entries c,entries d; END;";
        command.ExecuteNonQuery();
        using var cancellation = new CancellationTokenSource();
        var selectingUpdates = 0;
        var timer = Stopwatch.StartNew();
        using (store.ObserveDuplicateWork(phase =>
        {
            if (phase == "Selecting files and collapsing hard-link aliases" && ++selectingUpdates == 2) cancellation.Cancel();
        }, cancellation.Token))
        {
            var error = Assert.Throws<SqliteException>(() => store.PrepareContentWork([id]));
            Assert.Equal(9, error.SqliteErrorCode);
        }
        Assert.Equal(2, selectingUpdates);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3), $"SQL cancellation took {timer.Elapsed}");
        Assert.Equal(100, store.Query(id, new()).Count);
    }

    [Fact]
    public void CancelledResultPublicationRetainsPreviouslyCompletedGroups()
    {
        using var fixture = new Fixture();
        using var store = new IndexStore(Path.Combine(fixture.Root, "atomic.db"));
        var id = store.CreateSnapshot([fixture.Root]);
        store.AddBatch(id, new(fixture.Root, "Fixture", [
            new(Path.Combine(fixture.Root, "a"), fixture.Root, "a", "one", false, 17, 4096, 1, 1, 32),
            new(Path.Combine(fixture.Root, "b"), fixture.Root, "b", "two", false, 17, 4096, 1, 1, 32)], []));
        store.PrepareContentWork([id]);
        foreach (var file in store.WorkCandidates(false).ToArray())
            store.SaveWorkHash(new(file.Path, "same", file.Identity, file.Length, 1, 1, null), true);
        store.FinishContentWork(id, "SHA-256", "");
        var before = store.LastDuplicateRun(id);
        Assert.Equal(2, store.WorkCandidateCount(false));
        Assert.Equal(0, store.WorkCandidateCount(true));
        using var cancellation = new CancellationTokenSource();
        using (store.ObserveDuplicateWork(phase => { if (phase == "Saving duplicate results and summary") cancellation.Cancel(); }, cancellation.Token))
            Assert.Throws<OperationCanceledException>(() => store.FinishContentWork(id, "MD5", ""));
        Assert.Equal(before, store.LastDuplicateRun(id));
        Assert.All(store.Duplicates(id), row => Assert.Equal("SHA-256 content", row.Evidence));
    }
}
