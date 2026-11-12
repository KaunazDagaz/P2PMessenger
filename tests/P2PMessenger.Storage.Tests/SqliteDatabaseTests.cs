using Microsoft.Data.Sqlite;

namespace P2PMessenger.Storage.Tests;

public sealed class SqliteDatabaseTests
{
    [Fact]
    public async Task OpenAsync_NewFile_TurnsOnWalSecureDeleteAndForeignKeys()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var database = new TemporaryDatabase();
        await using var connection = await database.OpenAsync(cancellationToken);

        Assert.Equal("wal", await Sql.TextAsync(connection, "PRAGMA journal_mode;", cancellationToken));
        Assert.Equal(1, await Sql.ScalarAsync(connection, "PRAGMA secure_delete;", cancellationToken));
        Assert.Equal(1, await Sql.ScalarAsync(connection, "PRAGMA foreign_keys;", cancellationToken));
    }

    [Fact]
    public void ConnectionStringFor_Path_CreatesTheFileAndTurnsOnForeignKeys()
    {
        var connectionString = SqliteDatabase.ConnectionStringFor("messenger.db");

        Assert.Equal("messenger.db", connectionString.DataSource);
        Assert.Equal(SqliteOpenMode.ReadWriteCreate, connectionString.Mode);
        Assert.True(connectionString.ForeignKeys);
    }
}
