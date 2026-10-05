using FileViz.Core;
using FileViz.Data;
using Microsoft.Data.Sqlite;
using Xunit;
namespace FileViz.Tests;

public class RecoveryTests
{
    [Fact]
    public void FailedViewRebuildRetainsInventoryAndCanBeRetried()
    {
        using var fixture = new Fixture();
        var database = Path.Combine(fixture.Root, "recovery.db");
        using var store = new IndexStore(database);
        var id = store.CreateSnapshot([fixture.Root]);
        store.AddBatch(id, new(fixture.Root, "Fixture", [new(Path.Combine(fixture.Root, "file.bin"), fixture.Root,
            "file.bin", "identity", false, 17, 4096, 1, 1, 32)], []));
        store.SetState(id, "Interrupted");
        using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER fail_folder BEFORE INSERT ON folders BEGIN SELECT RAISE(ABORT,'disposable view failure'); END;";
        command.ExecuteNonQuery();
        Assert.Throws<SqliteException>(() => store.RebuildViews(id));
        Assert.Equal("Failed", Assert.Single(store.Snapshots()).State);
        Assert.Equal(17, Assert.Single(store.Query(id, new(Root: fixture.Root))).Length);
        command.CommandText = "DROP TRIGGER fail_folder;";
        command.ExecuteNonQuery();
        store.RebuildViews(id);
        Assert.Equal("Failed", Assert.Single(store.Snapshots()).State);
        Assert.Equal(17, store.GetSummary(id).Logical);
        Assert.Equal(17, store.FolderComposition(id, fixture.Root)!.Categories.Sum());
    }
}
