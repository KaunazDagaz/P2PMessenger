using Microsoft.Data.Sqlite;

namespace P2PMessenger.Storage.Tests;

public sealed class MigrationRunnerTests
{
    private static readonly Migration[] FailingSecondScript =
    [
        new(1, "0001_first", "CREATE TABLE kept_table (id INTEGER PRIMARY KEY) STRICT;"),
        new(2, "0002_broken", "CREATE TABLE rolled_back_table (id INTEGER PRIMARY KEY) STRICT; INSERT INTO missing_table (id) VALUES (1);"),
    ];

    [Fact]
    public async Task MigrateAsync_NewDatabase_AppliesEveryScriptAndRecordsTheVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var database = new TemporaryDatabase();
        await using var connection = await database.OpenAsync(cancellationToken);

        var version = await MigrationRunner.MigrateAsync(connection, cancellationToken);

        Assert.Equal(MigrationCatalog.Load().Count, version);
        Assert.Equal(version, await MigrationRunner.ReadVersionAsync(connection, cancellationToken));
    }

    [Fact]
    public async Task MigrateAsync_CurrentDatabase_ChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var database = new TemporaryDatabase();
        await using var connection = await database.OpenMigratedAsync(cancellationToken);
        var schemaBefore = await Sql.StringsAsync(connection, "SELECT sql FROM sqlite_master WHERE sql IS NOT NULL ORDER BY name;", cancellationToken);

        var version = await MigrationRunner.MigrateAsync(connection, cancellationToken);

        Assert.Equal(MigrationCatalog.Load().Count, version);
        Assert.Equal(schemaBefore, await Sql.StringsAsync(connection, "SELECT sql FROM sqlite_master WHERE sql IS NOT NULL ORDER BY name;", cancellationToken));
    }

    [Fact]
    public async Task MigrateAsync_FailingScript_RollsBackThatScriptAndKeepsTheEarlierOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var database = new TemporaryDatabase();
        await using var connection = await database.OpenAsync(cancellationToken);

        await Assert.ThrowsAsync<SqliteException>(() => MigrationRunner.MigrateAsync(connection, FailingSecondScript, cancellationToken));

        Assert.Equal(1, await MigrationRunner.ReadVersionAsync(connection, cancellationToken));
        Assert.Equal("kept_table", Assert.Single(await Sql.StringsAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;", cancellationToken)));
    }

    [Fact]
    public async Task MigrateAsync_DatabaseNewerThanTheApp_IsRefusedAndLeftAsItIs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var database = new TemporaryDatabase();
        await using var connection = await database.OpenAsync(cancellationToken);
        await Sql.ExecuteAsync(connection, "PRAGMA user_version = 1000;", [], cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => MigrationRunner.MigrateAsync(connection, cancellationToken));

        Assert.Equal(1000, await MigrationRunner.ReadVersionAsync(connection, cancellationToken));
        Assert.Empty(await Sql.StringsAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table';", cancellationToken));
    }
}
