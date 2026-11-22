using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Application.Tests.LocalData;

public sealed class DeleteAllTests
{
    private const string Passphrase = "lantern orchard pebble";

    private static readonly PassphraseKdf Cheap = new(1024, 1);

    [Fact]
    [Trait("Story", "US-21")]
    public async Task DeleteAll_DataInTheKeyStore_RemovesEveryFileAndForgetsTheKeyStoreItem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);
        var created = await localData.CreateAsync(null, cancellationToken);
        await FillAsync(created.Stores!, cancellationToken);
        var keyStoreCopy = (byte[])(await ScalarAsync(folder.DatabasePath, "SELECT wrapped_key FROM data_key;", cancellationToken))!;

        Assert.True(await localData.DeleteAllAsync(cancellationToken));

        Assert.Empty(folder.Files());
        Assert.Equal(keyStoreCopy, Assert.Single(protector.Forgotten));
        Assert.Equal(LocalDataProtection.None, localData.Protection);
        Assert.Equal(LocalDataStatus.NotCreated, (await localData.OpenAsync(cancellationToken)).Status);
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-01")]
    public async Task DeleteAll_ThenCreate_StartsWithoutTheOldIdentityContactsOrChats()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);
        await FillAsync((await localData.CreateAsync(null, cancellationToken)).Stores!, cancellationToken);
        Assert.True(await localData.DeleteAllAsync(cancellationToken));

        var again = await localData.CreateAsync(null, cancellationToken);

        Assert.True(again.IsOpen);
        Assert.Null(await again.Stores.Identity.LoadAsync(cancellationToken));
        Assert.Empty(await again.Stores.Contacts.LoadAllAsync(cancellationToken));
        Assert.Empty(await again.Stores.Chats.LoadAllAsync(cancellationToken));
        Assert.Empty(await again.Stores.Chats.LoadSentSequencesAsync(cancellationToken));
        Assert.Equal(LocalDataProtection.KeyStore, localData.Protection);
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-20")]
    public async Task DeleteAll_PassphraseOverTheKeyStore_ForgetsTheKeyStoresShare()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector { Protection = KeyProtection.LinuxSecretService };
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);
        await FillAsync((await localData.CreateAsync(Passphrase, cancellationToken)).Stores!, cancellationToken);

        Assert.True(await localData.DeleteAllAsync(cancellationToken));

        Assert.Empty(folder.Files());
        Assert.Single(protector.Forgotten);
        Assert.Equal(LocalDataStatus.NotCreated, (await localData.OpenAsync(cancellationToken)).Status);
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-20")]
    public async Task DeleteAll_StillLockedAtStart_DeletesWithoutThePassphrase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await using (var first = new DatabaseLocalData(folder.DatabasePath, protector, Cheap))
        {
            await FillAsync((await first.CreateAsync(Passphrase, cancellationToken)).Stores!, cancellationToken);
        }

        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);
        Assert.Equal(LocalDataStatus.PassphraseNeeded, (await localData.OpenAsync(cancellationToken)).Status);

        Assert.True(await localData.DeleteAllAsync(cancellationToken));

        Assert.Empty(folder.Files());
        Assert.Single(protector.Forgotten);
        Assert.Equal(LocalDataStatus.NotCreated, (await localData.OpenAsync(cancellationToken)).Status);
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-20")]
    public async Task DeleteAll_PassphraseAloneWithoutAKeyStore_RemovesTheFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        await using var localData = new DatabaseLocalData(folder.DatabasePath, null, Cheap);
        await FillAsync((await localData.CreateAsync(Passphrase, cancellationToken)).Stores!, cancellationToken);

        Assert.True(await localData.DeleteAllAsync(cancellationToken));

        Assert.Empty(folder.Files());
        Assert.Equal(LocalDataStatus.NotCreated, (await localData.OpenAsync(cancellationToken)).Status);
    }

    [Theory]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-20")]
    [InlineData("not a database")]
    [InlineData("newer version")]
    public async Task DeleteAll_DataThatCannotBeOpened_IsDeleted(string kind)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        Directory.CreateDirectory(Path.GetDirectoryName(folder.DatabasePath)!);
        if (kind == "not a database")
        {
            await File.WriteAllTextAsync(folder.DatabasePath, string.Concat(Enumerable.Repeat("This is not an SQLite database. ", 200)), cancellationToken);
        }
        else
        {
            await ExecuteAsync(folder.DatabasePath, "CREATE TABLE later (id INTEGER); PRAGMA user_version = 1000;", cancellationToken);
        }

        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);
        Assert.Equal(LocalDataStatus.Failed, (await localData.OpenAsync(cancellationToken)).Status);

        Assert.True(await localData.DeleteAllAsync(cancellationToken));

        Assert.Empty(folder.Files());
        Assert.Empty(protector.Forgotten);
        Assert.Equal(LocalDataStatus.NotCreated, (await localData.OpenAsync(cancellationToken)).Status);
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-11")]
    public async Task DeleteAll_CopyWhoseKeyIsNotOnThisDevice_IsDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        using var otherDevice = new LocalDataFolder();
        await using (var original = new DatabaseLocalData(folder.DatabasePath, new TestKeyProtector(), Cheap))
        {
            await FillAsync((await original.CreateAsync(null, cancellationToken)).Stores!, cancellationToken);
        }

        await using var localData = new DatabaseLocalData(folder.CopyTo(otherDevice), new TestKeyProtector(), Cheap);
        Assert.Equal(LocalDataProblem.KeyNotFound, (await localData.OpenAsync(cancellationToken)).Problem);

        Assert.True(await localData.DeleteAllAsync(cancellationToken));

        Assert.Empty(otherDevice.Files());
        Assert.NotEmpty(folder.Files());
    }

    [Fact]
    [Trait("Story", "US-21")]
    public async Task DeleteAll_DataKeptInMemoryOrNeverCreated_WritesNothingAndSucceeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        await using var neverCreated = new DatabaseLocalData(folder.DatabasePath, new TestKeyProtector(), Cheap);
        await using var inMemory = new DatabaseLocalData(folder.DatabasePath, null, Cheap);
        var memory = await inMemory.CreateAsync(null, cancellationToken);
        Assert.Equal(LocalDataMode.MemoryOnlyNoKeyStore, memory.Stores!.Mode);

        Assert.True(await neverCreated.DeleteAllAsync(cancellationToken));
        Assert.True(await inMemory.DeleteAllAsync(cancellationToken));

        Assert.Empty(folder.Files());
    }

    [Fact]
    [Trait("Story", "US-21")]
    public async Task InMemory_DeleteAll_GivesNewEmptyStores()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var localData = new InMemoryLocalData(LocalDataMode.MemoryOnlyNoKeyStore);
        var before = (await localData.OpenAsync(cancellationToken)).Stores!;
        await FillAsync(before, cancellationToken);

        Assert.True(await localData.DeleteAllAsync(cancellationToken));
        var after = (await localData.OpenAsync(cancellationToken)).Stores!;

        Assert.NotSame(before, after);
        Assert.Equal(LocalDataMode.MemoryOnlyNoKeyStore, after.Mode);
        Assert.Null(await after.Identity.LoadAsync(cancellationToken));
        Assert.Empty(await after.Contacts.LoadAllAsync(cancellationToken));
        Assert.Empty(await after.Chats.LoadAllAsync(cancellationToken));
    }

    private static async Task FillAsync(LocalDataStores stores, CancellationToken cancellationToken)
    {
        var created = await new IdentityService(stores.Identity).CreateAsync("Alice", cancellationToken);
        Assert.True(created.IsCreated);
        using var identity = created.Identity;
        using var bob = KeySet.Create();
        await stores.Contacts.SaveAsync(new Contact(TestKeys.Code(bob, "Bob"), ContactState.Added, KeyComparison.NotCompared), cancellationToken);
        var recorded = new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
        await stores.Chats.AddAsync(new ChatMessage(bob.IdentityKey, MessageDirection.Outgoing, MessageId.Create(), 1, recorded, "Hello", recorded), cancellationToken);
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The tests pass constant SQL.")]
    private static async Task<object?> ScalarAsync(string databasePath, string statement, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false };
        await using var connection = new SqliteConnection(connectionString.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = statement;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The tests pass constant SQL.")]
    private static async Task ExecuteAsync(string databasePath, string statement, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false };
        await using var connection = new SqliteConnection(connectionString.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = statement;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
