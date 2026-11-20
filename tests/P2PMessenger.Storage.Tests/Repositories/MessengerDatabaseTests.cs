using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Tests.Repositories;

public sealed class MessengerDatabaseTests
{
    [Fact]
    public async Task Open_NewFile_CreatesTheLatestSchemaWithoutADataKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var file = new TemporaryDatabase();

        var opened = await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken);
        await using (opened.Database)
        {
            Assert.Equal(DatabaseOpenOutcome.Opened, opened.Outcome);
            Assert.True(opened.IsOpened);
            Assert.Null(await opened.Database.ReadDataKeyAsync(cancellationToken));
            Assert.False(await opened.Database.HoldsDataAsync(cancellationToken));
        }

        await using var connection = await file.OpenAsync(cancellationToken);
        Assert.Equal(MigrationCatalog.Load().Count, await MigrationRunner.ReadVersionAsync(connection, cancellationToken));
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData(DataKeyProtection.WindowsDpapi)]
    [InlineData(DataKeyProtection.AndroidKeystore)]
    [InlineData(DataKeyProtection.LinuxSecretService)]
    public async Task DataKey_SavedThenRead_KeepsItsProtectionAndBytes(DataKeyProtection protection)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var file = new TemporaryDatabase();
        byte[] wrapped = [1, 2, 3, 4, 5, 6, 7];

        await using (var database = (await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken)).Database!)
        {
            await database.SaveDataKeyAsync(new WrappedDataKey(protection, wrapped), cancellationToken);
            await Assert.ThrowsAnyAsync<Exception>(() => database.SaveDataKeyAsync(new WrappedDataKey(protection, wrapped), cancellationToken));
        }

        await using var reopened = (await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken)).Database!;
        var read = await reopened.ReadDataKeyAsync(cancellationToken);
        Assert.NotNull(read);
        Assert.Equal(protection, read.Protection);
        Assert.Equal(wrapped, read.WrappedKey.ToArray());
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task DataKey_WrappedByAPassphrase_IsNotSavedWithoutItsParameters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var file = new TemporaryDatabase();
        await using var database = (await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken)).Database!;

        await Assert.ThrowsAsync<ArgumentException>(() => database.SaveDataKeyAsync(new WrappedDataKey(DataKeyProtection.Passphrase, [1]), cancellationToken));
        Assert.Null(await database.ReadDataKeyAsync(cancellationToken));
    }

    [Fact]
    public async Task Open_DatabaseOfANewerApp_IsReportedAndLeftUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var file = new TemporaryDatabase();
        await using (var connection = await file.OpenAsync(cancellationToken))
        {
            await Sql.ExecuteAsync(connection, "PRAGMA user_version = 1000;", [], cancellationToken);
        }

        var opened = await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken);

        Assert.Equal(DatabaseOpenOutcome.NewerThanApp, opened.Outcome);
        Assert.Null(opened.Database);
        await using var check = await file.OpenAsync(cancellationToken);
        Assert.Equal(1000, await MigrationRunner.ReadVersionAsync(check, cancellationToken));
        Assert.Equal(0, await Sql.ScalarAsync(check, "SELECT count(*) FROM sqlite_schema WHERE type = 'table';", cancellationToken));
    }

    [Fact]
    public async Task Open_FileThatIsNotADatabase_IsReported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var file = new TemporaryDatabase();
        await File.WriteAllTextAsync(file.FilePath, string.Concat(Enumerable.Repeat("This is not an SQLite database. ", 200)), cancellationToken);

        var opened = await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken);

        Assert.Equal(DatabaseOpenOutcome.NotADatabase, opened.Outcome);
        Assert.Null(opened.Database);
    }

    [Fact]
    public async Task HoldsData_AfterAPersonIsStored_IsTrue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        user.Dispose();
        bob.Dispose();
        carol.Dispose();
        await store.CloseAsync();

        await using var database = (await MessengerDatabase.OpenAsync(store.FilePath, cancellationToken)).Database!;
        Assert.True(await database.HoldsDataAsync(cancellationToken));
    }
}
