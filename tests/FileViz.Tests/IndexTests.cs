using System.Text.Json;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using Microsoft.Data.Sqlite;
using Xunit;
namespace FileViz.Tests;

public class IndexTests
{
    private static async Task<long> Scan(IndexStore store, params string[] roots)
    {
        var id = store.CreateSnapshot(roots);
        foreach (var root in roots)
            await foreach (var batch in new DirectoryScanEngine().ScanAsync(new(root, [])))
                store.AddBatch(id, batch);
        store.Finish(id, "Complete");
        return id;
    }
    [Fact]
    public async Task DriveScopeFiltersTotalsEntriesAndExtensions()
    {
        using var fixture = new Fixture();
        var first = System.IO.Path.Combine(fixture.Root, "one");
        var second = System.IO.Path.Combine(fixture.Root, "two");
        fixture.Write("one/a.bin", "123");
        fixture.Write("two/b.txt", "1234567");
        using var store = new IndexStore(System.IO.Path.Combine(fixture.Root, "index.db"));
        var id = await Scan(store, first, second);
        Assert.Contains(store.PrimaryQueryPlan(id, first), x => x.Contains($"entry_{id}_size", StringComparison.Ordinal));
        Assert.Equal(3, store.GetSummary(id, first).Logical);
        Assert.Equal(7, store.GetSummary(id, second).Logical);
        Assert.Equal("a.bin", Assert.Single(store.Query(id, new(Root: first))).Name);
        Assert.Equal(".txt", Assert.Single(store.Extensions(id, second)).Name);
    }
    [Fact]
    public async Task DuplicateStagingSeparatesSamplesFromVerifiedContentAndOverlappingHistory()
    {
        using var fixture = new Fixture();
        var data = System.IO.Path.Combine(fixture.Root, "data");
        fixture.Write("data/one/a.bin", "abc");
        fixture.Write("data/two/A.BIN", "abc");
        using var store = new IndexStore(System.IO.Path.Combine(fixture.Root, "index.db"));
        var first = await Scan(store, data);
        var second = await Scan(store, data);
        store.FindNameDuplicates(second, [first, second], [data]);
        Assert.Equal(2, store.Duplicates(second).Count);
        Assert.Equal(0, store.DuplicatePotential(second));
        Assert.Equal(new DuplicateRun("Name", 0, 0, 0, 1, 0, 2, 0, ""), store.LastDuplicateRun(second)! with { Finished = "" });
        store.PrepareContentWork([first, second], [data]);
        var candidates = store.WorkCandidates(false).ToArray();
        Assert.Equal(2, candidates.Length);
        foreach (var entry in candidates)
            store.SaveWorkHash(new(entry.Path, "SAMPLE", entry.Identity, entry.Length, entry.ModifiedTicks, entry.ChangeTicks, null), false);
        store.FinishContentWork(second, "SHA-256", "");
        Assert.Empty(store.Duplicates(second));
        Assert.Equal(2, store.WorkCandidates(true).Count());
        foreach (var entry in candidates)
            store.SaveWorkHash(new(entry.Path, "FULL", entry.Identity, entry.Length, entry.ModifiedTicks, entry.ChangeTicks, null), true);
        store.FinishContentWork(second, "SHA-256", candidates[0].Parent);
        var duplicates = store.Duplicates(second);
        Assert.Equal(2, duplicates.Count);
        Assert.Single(duplicates, x => x.SuggestedKeeper);
        Assert.Equal(3, store.DuplicatePotential(second));
        Assert.Equal(new DuplicateRun("SHA-256", 2, 2, 2, 1, 3, 2, 0, ""), store.LastDuplicateRun(second)! with { Finished = "" });
    }
    [Fact]
    public async Task CompareAndExportDoNotLoseOrOverwriteData()
    {
        using var fixture = new Fixture();
        var data = System.IO.Path.Combine(fixture.Root, "data");
        fixture.Write("data/grown.txt", "a");
        var removed = fixture.Write("data/removed.txt", "x");
        using var store = new IndexStore(System.IO.Path.Combine(fixture.Root, "index.db"));
        var before = await Scan(store, data);
        File.AppendAllText(System.IO.Path.Combine(data, "grown.txt"), "bc");
        File.Delete(removed);
        fixture.Write("data/new.txt", "n");
        var after = await Scan(store, data);
        var differences = store.Compare(before, after);
        Assert.Contains(differences, x => x.Change == "Added");
        Assert.Contains(differences, x => x.Change == "Removed");
        Assert.Contains(differences, x => x.Change == "Grown");
        var report = System.IO.Path.Combine(fixture.Root, "report.json");
        store.Export(after, report, true);
        using var json = JsonDocument.Parse(File.ReadAllText(report));
        Assert.Equal(2, json.RootElement.GetArrayLength());
        Assert.Throws<IOException>(() => store.Export(after, report, true));
    }
    [Fact]
    public void OpeningAnotherConnectionDoesNotInterruptActiveSnapshot()
    {
        using var fixture = new Fixture();
        var db = System.IO.Path.Combine(fixture.Root, "index.db");
        using var writer = new IndexStore(db);
        var id = writer.CreateSnapshot([fixture.Root]);
        using (var reader = new IndexStore(db))
            Assert.Equal("Scanning", Assert.Single(reader.Snapshots()).State);
        writer.RecoverInterrupted();
        Assert.Equal("Interrupted", Assert.Single(writer.Snapshots()).State);
        writer.SetState(id, "Cancelled");
    }
    [Fact]
    public void HardLinkRefreshRequiresTheObservedIdentityAndCanonicalizesAllAliases()
    {
        using var fixture = new Fixture();
        using var store = new IndexStore(Path.Combine(fixture.Root, "aliases.db"));
        var id = store.CreateSnapshot([fixture.Root]);
        var a = new FileEntry(Path.Combine(fixture.Root, "a"), fixture.Root, "a", "file-id", false, 3, 8, 100, 100, 32);
        var b = a with
        {
            Path = Path.Combine(fixture.Root, "b"),
            Name = "b",
            ModifiedTicks = 50,
            ChangeTicks = 50
        };
        store.AddBatch(id, new(fixture.Root, "Fixture", [a, b], []));
        Assert.Single(store.AliasPaths(id));
        Assert.False(store.RefreshAlias(id, a with
        {
            Identity = "replaced-id"
        }));
        Assert.True(store.RefreshAlias(id, a with
        {
            ModifiedTicks = 200,
            ChangeTicks = 200
        }));
        Assert.All(store.Query(id, new()), entry => Assert.Equal(200, entry.ChangeTicks));
    }
    [Fact]
    public void DirectoryRestartDiscardsFailedRawEntriesAndDiagnosticsForTheRoot()
    {
        using var fixture = new Fixture();
        using var store = new IndexStore(Path.Combine(fixture.Root, "restart.db"));
        var id = store.CreateSnapshot([fixture.Root]);
        var raw = new FileEntry(Path.Combine(fixture.Root, "raw-only"), fixture.Root, "raw-only", "bad", false, 999, 1000, 0, 0, 32);
        store.AddBatch(id, new(fixture.Root, "Raw", [raw], [new(raw.Path, "raw diagnostic")]));
        store.AddBatch(id, new(fixture.Root, "Directory fallback", [], [new(fixture.Root, "Structural inconsistency")], true));
        store.AddBatch(id, new(fixture.Root, "Directory", [raw with { Path = Path.Combine(fixture.Root, "valid"), Name = "valid", Length = 3 }], []));
        store.Finish(id, "Complete");
        Assert.Equal("valid", Assert.Single(store.Query(id, new())).Name);
        Assert.Single(store.Errors(id));
        Assert.Equal("Partial", Assert.Single(store.Snapshots()).State);
    }

    [Fact]
    public void UnchangedAliasRefreshDoesNotWriteAndUnknownAllocationRemainsUnknown()
    {
        using var fixture = new Fixture();
        var db = Path.Combine(fixture.Root, "no-op.db");
        using var store = new IndexStore(db);
        var id = store.CreateSnapshot([fixture.Root]);
        var a = new FileEntry(Path.Combine(fixture.Root, "a"), fixture.Root, "a", "id", false, 3, null, 100, 100, 32);
        var b = a with { Path = Path.Combine(fixture.Root, "b"), Name = "b" };
        store.AddBatch(id, new(fixture.Root, "Fixture", [a, b], []));
        Assert.Single(store.AliasPaths(id));
        using var audit = new SqliteConnection($"Data Source={db};Pooling=False");
        audit.Open();
        using var command = audit.CreateCommand();
        command.CommandText = "CREATE TABLE writes(n INTEGER); INSERT INTO writes VALUES(0); CREATE TRIGGER audit_updates AFTER UPDATE ON entries BEGIN UPDATE writes SET n=n+1; END;";
        command.ExecuteNonQuery();
        Assert.Empty(store.RefreshAliases(id, [a]));
        command.CommandText = "SELECT n FROM writes;";
        Assert.Equal(0L, command.ExecuteScalar());
        Assert.Empty(store.RefreshAliases(id, [a with { Length = 7, Allocated = 8 }]));
        Assert.Equal(2L, command.ExecuteScalar());
        Assert.Empty(store.RefreshAliases(id, [a with { Length = 7 }]));
        Assert.Equal(4L, command.ExecuteScalar());
        Assert.All(store.Query(id, new()), entry => { Assert.Equal(7, entry.Length); Assert.Null(entry.Allocated); });
        Assert.Single(store.RefreshAliases(id, [a with { Identity = "replaced" }]));
        Assert.Equal(4L, command.ExecuteScalar());
    }

    [Fact]
    public void AliasBatchRollsBackOnFailureAndRejectsUnboundedOrCancelledWork()
    {
        using var fixture = new Fixture();
        var db = Path.Combine(fixture.Root, "atomic.db");
        using var store = new IndexStore(db);
        var id = store.CreateSnapshot([fixture.Root]);
        var a = new FileEntry(Path.Combine(fixture.Root, "a"), fixture.Root, "a", "id", false, 3, 8, 100, 100, 32);
        var b = a with { Path = Path.Combine(fixture.Root, "b"), Name = "b" };
        store.AddBatch(id, new(fixture.Root, "Fixture", [a, b], []));
        Assert.Single(store.AliasPaths(id));
        using var audit = new SqliteConnection($"Data Source={db};Pooling=False");
        audit.Open();
        using var command = audit.CreateCommand();
        command.CommandText = "CREATE TRIGGER reject_test_update BEFORE UPDATE ON entries WHEN NEW.length=99 BEGIN SELECT RAISE(ABORT,'test failure'); END;";
        command.ExecuteNonQuery();
        Assert.Throws<SqliteException>(() => store.RefreshAliases(id, [a with { Length = 7 }, b with { Length = 99 }]));
        Assert.All(store.Query(id, new()), entry => Assert.Equal(3, entry.Length));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.RefreshAliases(id, Enumerable.Repeat(a, 65).ToArray()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => store.RefreshAliases(id, [a with { Length = 7 }], cancellation.Token));
        Assert.All(store.Query(id, new()), entry => Assert.Equal(3, entry.Length));
    }

    [Fact]
    public void FinalizationReportsIndexesAndAggregatesBeforeCompletingTheSnapshot()
    {
        using var fixture = new Fixture();
        using var store = new IndexStore(Path.Combine(fixture.Root, "stages.db"));
        var id = store.CreateSnapshot([fixture.Root]);
        var entry = new FileEntry(Path.Combine(fixture.Root, "a"), fixture.Root, "a", "id", false, 3, 8, 100, 100, 32);
        store.AddBatch(id, new(fixture.Root, "Fixture", [entry], []));
        var stages = new List<string>();
        store.Finish(id, "Complete", stage =>
        {
            Assert.Equal("Scanning", Assert.Single(store.Snapshots()).State);
            stages.Add(stage);
        });
        Assert.Equal(7, stages.Count(x => x.StartsWith("Preparing file views", StringComparison.Ordinal)));
        Assert.Contains("Calculating folder sizes", stages);
        Assert.Contains("Calculating file types and ages", stages);
        Assert.Contains("Summarizing drive usage", stages);
        Assert.Equal("Saving snapshot", stages[^1]);
        Assert.Equal("Complete", Assert.Single(store.Snapshots()).State);
        Assert.Equal(3, store.GetSummary(id).Logical);
    }
}
