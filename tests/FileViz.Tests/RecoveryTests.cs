using FileViz.Core;
using FileViz.Data;
using Microsoft.Data.Sqlite;
using Xunit;
namespace FileViz.Tests;

public class RecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FolderNavigationSeeksTheParentWithoutSortingTheInventory(bool allocated)
    {
        using var fixture = new Fixture();
        var database = Path.Combine(fixture.Root, "navigation.db");
        using var store = new IndexStore(database);
        var id = store.CreateSnapshot([fixture.Root]);
        var child = Path.Combine(fixture.Root, "child");
        store.AddBatch(id, new(fixture.Root, "Fixture", [new(child, fixture.Root, "child", "child", true, 0, 0, 1, 1, 16),
            new(Path.Combine(child, "file.bin"), child, "file.bin", "file", false, 17, 4096, 1, 1, 32)], []));
        store.Finish(id, "Complete");
        var map = store.SpaceMap(id, fixture.Root, allocated);
        Assert.Equal(allocated ? 4096 : 17, Assert.Single(map).Bytes);
        Assert.Equal(2, store.LargestFolders(id, allocated, root: fixture.Root.ToUpperInvariant()).Count);
        using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        var size = allocated ? "allocated" : "logical";
        command.CommandText = $"EXPLAIN QUERY PLAN SELECT path,{size},files FROM folders WHERE snapshot=$s AND parent=$p AND path<>parent ORDER BY {size} DESC LIMIT 24;";
        command.Parameters.AddWithValue("$s", id);
        command.Parameters.AddWithValue("$p", fixture.Root);
        using var reader = command.ExecuteReader();
        var details = new List<string>();
        while (reader.Read()) details.Add(reader.GetString(3));
        Assert.Contains(details, x => x.Contains($"folder_{id}_parent_{size}", StringComparison.Ordinal));
        Assert.DoesNotContain(details, x => x.Contains("TEMP B-TREE", StringComparison.Ordinal));
        reader.Close();
        command.CommandText = $"EXPLAIN QUERY PLAN SELECT path,{size},files FROM folders WHERE snapshot=$s AND root=$p ORDER BY {size} DESC LIMIT 100;";
        using var ranking = command.ExecuteReader();
        details.Clear();
        while (ranking.Read()) details.Add(ranking.GetString(3));
        Assert.Contains(details, x => x.Contains(allocated ? $"folder_{id}_root_allocated" : "folders_rank", StringComparison.Ordinal));
        Assert.DoesNotContain(details, x => x.Contains("TEMP B-TREE", StringComparison.Ordinal));
    }

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
