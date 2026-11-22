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
    [Trait("Story", "US-20")]
    public async Task DataKey_WrappedByAPassphrase_IsSavedAndReadWithItsSaltAndSettings()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var file = new TemporaryDatabase();
        var wrapped = PassphraseKeyWrap.Wrap(new byte[32], "a passphrase of some length", new PassphraseKdf(1024, 2));

        await using (var database = (await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken)).Database!)
        {
            await database.SaveDataKeyAsync(wrapped, cancellationToken);
        }

        await using var reopened = (await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken)).Database!;
        var read = await reopened.ReadDataKeyAsync(cancellationToken);
        Assert.NotNull(read);
        Assert.Equal(DataKeyProtection.Passphrase, read.Protection);
        Assert.Equal(wrapped.WrappedKey.ToArray(), read.WrappedKey.ToArray());
        Assert.Equal(wrapped.Derivation!.Salt.ToArray(), read.Derivation!.Salt.ToArray());
        Assert.Equal(new PassphraseKdf(1024, 2), read.Derivation.Kdf);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task DataKey_WrappedByAPassphraseInsideTheKeyStore_IsSavedAndReadWithItsShare()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var file = new TemporaryDatabase();
        var wrapped = PassphraseKeyWrap.Wrap(new byte[32], "a passphrase of some length", new PassphraseKdf(1024, 2), DataKeyProtection.WindowsDpapi, new byte[32], new byte[170]);

        await using (var database = (await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken)).Database!)
        {
            await database.SaveDataKeyAsync(wrapped, cancellationToken);
        }

        await using var reopened = (await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken)).Database!;
        var read = await reopened.ReadDataKeyAsync(cancellationToken);
        Assert.NotNull(read);
        Assert.Equal(wrapped.WrappedKey.ToArray(), read.WrappedKey.ToArray());
        Assert.True(PassphraseKeyWrap.TryGetKeyStoreShare(read, out var keyStore, out var protectedShare));
        Assert.Equal(DataKeyProtection.WindowsDpapi, keyStore);
        Assert.Equal(170, protectedShare.Length);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task DataKey_Replaced_KeepsOnlyTheNewKeyInTheFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var file = new TemporaryDatabase();
        byte[] old = [.. Enumerable.Range(0, 48).Select(static value => (byte)(0x80 + value))];
        var replacement = PassphraseKeyWrap.Wrap(new byte[32], "a passphrase of some length", new PassphraseKdf(1024, 2));

        await using (var database = (await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken)).Database!)
        {
            await database.SaveDataKeyAsync(new WrappedDataKey(DataKeyProtection.WindowsDpapi, old), cancellationToken);
            await database.ReplaceDataKeyAsync(replacement, cancellationToken);
            var read = await database.ReadDataKeyAsync(cancellationToken);
            Assert.Equal(DataKeyProtection.Passphrase, read!.Protection);
            Assert.Equal(replacement.WrappedKey.ToArray(), read.WrappedKey.ToArray());

            foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(file.FilePath)!, Path.GetFileName(file.FilePath) + "*"))
            {
                var content = await ReadWhileOpenAsync(path, cancellationToken);
                Assert.Equal(-1, content.AsSpan().IndexOf(old));
            }

            await database.ReplaceDataKeyAsync(new WrappedDataKey(DataKeyProtection.LinuxSecretService, old), cancellationToken);
            var back = await database.ReadDataKeyAsync(cancellationToken);
            Assert.Equal(DataKeyProtection.LinuxSecretService, back!.Protection);
            Assert.Null(back.Derivation);
        }
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task DataKey_ReplacedWithoutARow_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var file = new TemporaryDatabase();
        await using var database = (await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken)).Database!;

        await Assert.ThrowsAsync<StoredDataException>(() => database.ReplaceDataKeyAsync(new WrappedDataKey(DataKeyProtection.WindowsDpapi, [1]), cancellationToken));
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData("UPDATE data_key SET kdf_memory_kib = 1048577;")]
    [InlineData("UPDATE data_key SET kdf_memory_kib = 4;")]
    [InlineData("UPDATE data_key SET kdf_passes = 17;")]
    [InlineData("UPDATE data_key SET wrapped_key = substr(wrapped_key, 1, 72);")]
    [InlineData("UPDATE data_key SET wrapped_key = unhex('02' || substr(hex(wrapped_key), 3));")]
    public async Task DataKey_PassphraseRowOutsideTheAcceptedRange_IsDamaged(string change)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var file = new TemporaryDatabase();
        await using (var database = (await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken)).Database!)
        {
            await database.SaveDataKeyAsync(PassphraseKeyWrap.Wrap(new byte[32], "a passphrase of some length", new PassphraseKdf(1024, 2)), cancellationToken);
        }

        await using (var connection = await file.OpenAsync(cancellationToken))
        {
            using var command = connection.CreateCommand();
            command.CommandText = change;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var reopened = (await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken)).Database!;
        await Assert.ThrowsAsync<StoredDataException>(() => reopened.ReadDataKeyAsync(cancellationToken));
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

    private static async Task<byte[]> ReadWhileOpenAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        var content = new byte[stream.Length];
        await stream.ReadExactlyAsync(content, cancellationToken);
        return content;
    }
}
