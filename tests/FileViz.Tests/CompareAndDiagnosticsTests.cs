using System.ComponentModel;
using System.Text.Json;
using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;
using Microsoft.Data.Sqlite;
using Xunit;
namespace FileViz.Tests;

public class CompareAndDiagnosticsTests
{
    private static async Task<long> Scan(IndexStore store, string root)
    {
        var id = store.CreateSnapshot([root]);
        await foreach (var batch in new DirectoryScanEngine().ScanAsync(new(root, [])))
            store.AddBatch(id, batch);
        store.Finish(id, "Complete");
        return id;
    }
    [Fact]
    public async Task CompareTotalsAndFolderRollupAgreeWithTheFileDiff()
    {
        using var fixture = new Fixture();
        using var data = new Fixture();
        fixture.Write("media/grows.bin", new string('g', 100));
        fixture.Write("media/deep/shrinks.bin", new string('s', 300));
        var removed = fixture.Write("old/removed.bin", new string('r', 50));
        using var store = new IndexStore(Path.Combine(data.Root, "index.db"));
        var root = Paths.Normalize(fixture.Root);
        var before = await Scan(store, root);
        File.AppendAllText(Path.Combine(root, "media", "grows.bin"), new string('g', 400));
        File.WriteAllText(Path.Combine(root, "media", "deep", "shrinks.bin"), new string('s', 100));
        File.Delete(removed);
        fixture.Write("new/added.bin", new string('a', 70));
        var after = await Scan(store, root);
        var totals = store.CompareTotals(before, after);
        Assert.Equal(new CompareSummary(1, 70, 1, 50, 1, 400, 1, 200), totals);
        Assert.Equal(70 + 400 - 50 - 200, totals.NetBytes);
        var folders = store.FolderChanges(before, after, root);
        Assert.Equal(200, folders.Single(x => x.Path.EndsWith("media", StringComparison.Ordinal)).Delta);
        Assert.Equal(-200, folders.Single(x => x.Path.EndsWith("deep", StringComparison.Ordinal)).Delta);
        Assert.Equal(-50, folders.Single(x => x.Path.EndsWith("old", StringComparison.Ordinal)).Delta);
        Assert.Equal(70, folders.Single(x => x.Path.EndsWith("new", StringComparison.Ordinal)).Delta);
        // Folder totals and the file-level diff describe the same change.
        Assert.Equal(totals.NetBytes, folders.Where(x => Path.GetDirectoryName(x.Path) == root).Sum(x => x.Delta));
        var removedOnly = store.Compare(before, after, change: "Removed");
        Assert.Equal("Removed", Assert.Single(removedOnly).Change);
        Assert.Equal(400, store.Compare(before, after)[0].Delta);
        SqliteConnection.ClearAllPools();
    }
    [Fact]
    public void DiagnosticKindsComeFromTheSourceOrTheMessage()
    {
        Assert.Equal(DiagnosticKinds.AccessDenied, DiagnosticKinds.FromException(new UnauthorizedAccessException()));
        Assert.Equal(DiagnosticKinds.Unavailable, DiagnosticKinds.FromException(new Win32Exception(53)));
        Assert.Equal(DiagnosticKinds.Interrupted, DiagnosticKinds.FromException(new Win32Exception(64)));
        Assert.Equal(DiagnosticKinds.Other, DiagnosticKinds.FromException(new IOException("disk")));
        Assert.Equal(DiagnosticKinds.AccessDenied, DiagnosticKinds.Classify(null, "Access is denied."));
        Assert.Equal(DiagnosticKinds.NotTraversed, DiagnosticKinds.Classify(null, "Directory depth exceeds the handle budget."));
        Assert.Equal(DiagnosticKinds.EngineFallback, DiagnosticKinds.Classify(DiagnosticKinds.EngineFallback, "anything"));
        Assert.Equal(DiagnosticSeverity.Gap, DiagnosticKinds.Severity(DiagnosticKinds.Unavailable));
        Assert.Equal(DiagnosticSeverity.Info, DiagnosticKinds.Severity(DiagnosticKinds.NotTraversed));
        // Workers that predate kinds still send parsable errors.
        var legacy = JsonSerializer.Deserialize<ScanError>("""{"Path":"C:\\x","Message":"m"}""")!;
        Assert.Null(legacy.Kind);
    }
    [Fact]
    public void ErrorKindsPersistAndLegacyRowsAreClassified()
    {
        using var fixture = new Fixture();
        var database = Path.Combine(fixture.Root, "legacy.db");
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
            CREATE TABLE snapshots(id INTEGER PRIMARY KEY,roots TEXT NOT NULL,started TEXT NOT NULL,state TEXT NOT NULL,files INTEGER NOT NULL DEFAULT 0,logical INTEGER NOT NULL DEFAULT 0,allocated INTEGER,errors INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE errors(snapshot INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,path TEXT NOT NULL,message TEXT NOT NULL);
            INSERT INTO snapshots(id,roots,started,state) VALUES(1,'["C:\\"]','2026-01-01T00:00:00.0000000Z','Partial');
            INSERT INTO errors VALUES(1,'C:\x','Access is denied.');
            """;
            command.ExecuteNonQuery();
        }
        using var store = new IndexStore(database);
        store.AddError(1, @"C:\y", "Raw scanner failed.", DiagnosticKinds.EngineFallback);
        var errors = store.Errors(1);
        Assert.Equal(DiagnosticKinds.AccessDenied, errors.Single(x => x.Path == @"C:\x").Kind);
        Assert.Equal(DiagnosticKinds.EngineFallback, errors.Single(x => x.Path == @"C:\y").Kind);
        store.SaveSetting("theme", "Dark");
        Assert.Equal("Dark", store.Setting("theme"));
        Assert.Null(store.Setting("missing"));
        store.SaveProfile(new("Daily", [@"C:\Users"], ["*.tmp"], true));
        Assert.Single(store.Profiles());
        store.DeleteProfile("Daily");
        Assert.Empty(store.Profiles());
        SqliteConnection.ClearAllPools();
    }
}
