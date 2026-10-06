using FileViz.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FileViz.Tests;

public class StartupTests
{
    [Fact]
    public void SchemaUpgradeDoesNotCheckpointPendingInventoryBeforeOpening()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "index.db");
        long snapshot;
        using (var seed = new IndexStore(path))
        {
            snapshot = seed.CreateSnapshot([fixture.Root]);
        }
        // Keep a connection open so the disposable WAL survives. This models an
        // earlier run with pending pages, without relying on crashes or wall-clock timing.
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        writer.Open();
        using var command = writer.CreateCommand();
        command.CommandText = "PRAGMA wal_autocheckpoint=0; DROP TABLE photos; CREATE TABLE pending(payload BLOB); WITH RECURSIVE rows(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM rows WHERE n<11000) INSERT INTO pending SELECT zeroblob(4096) FROM rows;";
        command.ExecuteNonQuery();
        var databaseBytes = new FileInfo(path).Length;
        Assert.True(new FileInfo(path + "-wal").Length > 10000L * 4096);
        using var reopened = new IndexStore(path);
        reopened.RecoverInterrupted();
        Assert.Equal(databaseBytes, new FileInfo(path).Length);
        var saved = Assert.Single(reopened.Snapshots());
        Assert.Equal(snapshot, saved.Id);
        Assert.Equal("Interrupted", saved.State);
        command.CommandText = "SELECT COUNT(*) FROM pending;";
        Assert.Equal(11000L, (long)command.ExecuteScalar()!);
        command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name='photos';";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
        // A later ordinary write resumes the normal checkpoint policy.
        reopened.CreateSnapshot([fixture.Root]);
        Assert.True(new FileInfo(path).Length > databaseBytes);
    }
}
