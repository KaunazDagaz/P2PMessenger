using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Data.Sqlite;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Tests.LocalData;

public sealed class DatabaseLocalDataTests
{
    private const string ContactName = "Yorick Marlowe";
    private const string MessageText = "The keyword is periwinkle-avalanche-7341";

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Open_NoDatabase_CreatesOneWhoseKeyTheKeyStoreWraps()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);

        var result = await localData.OpenAsync(cancellationToken);

        Assert.True(result.IsOpen);
        Assert.Equal(LocalDataProblem.None, result.Problem);
        Assert.Equal(LocalDataMode.Saved, result.Stores.Mode);
        Assert.Equal(1, protector.ProtectCalls);
        Assert.True(File.Exists(folder.DatabasePath));
        Assert.Null(await result.Stores.Identity.LoadAsync(cancellationToken));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(folder.DatabasePath)!));
        }
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Open_Twice_GivesTheSameStores()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);

        var first = await localData.OpenAsync(cancellationToken);
        var second = await localData.OpenAsync(cancellationToken);

        Assert.Same(first.Stores, second.Stores);
        Assert.Equal(1, protector.ProtectCalls);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Open_NoKeyStoreAndNoDatabase_KeepsTheDataInMemoryAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector { Protection = KeyProtection.LinuxSecretService, Outcome = KeyStoreOutcome.Unavailable };
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);

        var result = await localData.OpenAsync(cancellationToken);

        Assert.True(result.IsOpen);
        Assert.Equal(LocalDataMode.MemoryOnlyNoKeyStore, result.Stores.Mode);
        Assert.IsType<InMemoryIdentityStore>(result.Stores.Identity);
        Assert.Empty(folder.Files());
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData(KeyStoreOutcome.Refused, LocalDataProblem.KeyStoreRefused)]
    [InlineData(KeyStoreOutcome.KeyNotFound, LocalDataProblem.KeyNotFound)]
    public async Task Open_KeyStoreFailsForANewDatabase_ReportsItAndWritesNothing(KeyStoreOutcome outcome, LocalDataProblem problem)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector { Outcome = outcome };
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);

        var result = await localData.OpenAsync(cancellationToken);

        Assert.False(result.IsOpen);
        Assert.Equal(problem, result.Problem);
        Assert.Empty(folder.Files());
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData(KeyStoreOutcome.Unavailable, LocalDataProblem.KeyStoreUnavailable)]
    [InlineData(KeyStoreOutcome.Refused, LocalDataProblem.KeyStoreRefused)]
    [InlineData(KeyStoreOutcome.KeyNotFound, LocalDataProblem.KeyNotFound)]
    public async Task Open_KeyStoreFailsForAStoredDatabase_ReportsItAndKeepsTheData(KeyStoreOutcome outcome, LocalDataProblem problem)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await FillAsync(folder.DatabasePath, protector, cancellationToken);
        protector.Outcome = outcome;

        await using (var failing = new DatabaseLocalData(folder.DatabasePath, protector))
        {
            var result = await failing.OpenAsync(cancellationToken);
            Assert.False(result.IsOpen);
            Assert.Equal(problem, result.Problem);
        }

        protector.Outcome = KeyStoreOutcome.Succeeded;
        await using var working = new DatabaseLocalData(folder.DatabasePath, protector);
        var reopened = await working.OpenAsync(cancellationToken);
        Assert.True(reopened.IsOpen);
        Assert.Single(await reopened.Stores.Contacts.LoadAllAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Open_AfterAProblem_CanBeTriedAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await FillAsync(folder.DatabasePath, protector, cancellationToken);
        protector.Outcome = KeyStoreOutcome.Refused;
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);

        Assert.Equal(LocalDataProblem.KeyStoreRefused, (await localData.OpenAsync(cancellationToken)).Problem);
        protector.Outcome = KeyStoreOutcome.Succeeded;
        var result = await localData.OpenAsync(cancellationToken);

        Assert.True(result.IsOpen);
        Assert.NotNull(await result.Stores.Identity.LoadAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("Story", "US-15")]
    [Trait("NFR", "NFR-06")]
    public async Task Open_AfterARestart_GivesTheSameIdentityContactsMessagesAndSeenIds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        var stored = await FillAsync(folder.DatabasePath, protector, cancellationToken);

        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);
        var result = await localData.OpenAsync(cancellationToken);

        Assert.True(result.IsOpen);
        using var identity = await result.Stores.Identity.LoadAsync(cancellationToken);
        Assert.NotNull(identity);
        Assert.Equal(stored.IdentityKey, identity.Keys.IdentityKey);
        Assert.Equal("Alice", identity.Name.Value);
        var contact = Assert.Single(await result.Stores.Contacts.LoadAllAsync(cancellationToken));
        Assert.Equal(stored.Contact.Code, contact.Code);
        Assert.Equal(ContactState.Added, contact.State);
        Assert.Equal(KeyComparison.CheckedByYou, contact.KeyComparison);
        var messages = await result.Stores.Chats.LoadAllAsync(cancellationToken);
        Assert.Equal(stored.Messages.Select(Shape), messages.Select(Shape));
        Assert.True(await result.Stores.SeenMessageIds.ContainsAsync(stored.Messages[1].Id, cancellationToken));
        Assert.False(await result.Stores.SeenMessageIds.ContainsAsync(MessageId.Create(), cancellationToken));
        Assert.Equal(1, protector.UnprotectCalls);
        using var mallory = TestKeys.NewIdentity("Mallory");
        Assert.False(await result.Stores.Identity.TrySaveAsync(mallory, cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public async Task Open_FilesCopiedToAnotherDevice_GivesNothingAndSaysTheKeyIsNotThere()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        using var otherDevice = new LocalDataFolder();
        await FillAsync(folder.DatabasePath, new TestKeyProtector(), cancellationToken);
        var copied = folder.CopyTo(otherDevice);

        await using var sameKind = new DatabaseLocalData(copied, new TestKeyProtector());
        var result = await sameKind.OpenAsync(cancellationToken);
        Assert.False(result.IsOpen);
        Assert.Equal(LocalDataProblem.KeyNotFound, result.Problem);

        await using var otherKind = new DatabaseLocalData(copied, new TestKeyProtector { Protection = KeyProtection.LinuxSecretService });
        Assert.Equal(LocalDataProblem.KeyNotFound, (await otherKind.OpenAsync(cancellationToken)).Problem);
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public async Task Files_AfterUse_HoldNoNameOrTextInPlain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        await FillAsync(folder.DatabasePath, new TestKeyProtector(), cancellationToken);

        var files = folder.Files();

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var content = await File.ReadAllBytesAsync(file, cancellationToken);
            Assert.Equal(-1, content.AsSpan().IndexOf(Encoding.UTF8.GetBytes(ContactName)));
            Assert.Equal(-1, content.AsSpan().IndexOf(Encoding.UTF8.GetBytes(MessageText)));
            Assert.Equal(-1, content.AsSpan().IndexOf("Alice"u8));
        }
    }

    [Fact]
    public async Task Open_DatabaseOfANewerApp_SaysSo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await FillAsync(folder.DatabasePath, protector, cancellationToken);
        await ExecuteAsync(folder.DatabasePath, "PRAGMA user_version = 99;", cancellationToken);

        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);

        Assert.Equal(LocalDataProblem.NewerVersion, (await localData.OpenAsync(cancellationToken)).Problem);
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData("UPDATE person SET display_name_enc = (SELECT display_name_enc FROM person WHERE id = 2) WHERE id = 1;")]
    [InlineData("UPDATE message SET text_enc = (SELECT text_enc FROM message WHERE id = 1) WHERE id = 2;")]
    [InlineData("DELETE FROM data_key;")]
    [InlineData("UPDATE data_key SET protection = 3;")]
    public async Task Open_ChangedDatabase_SaysTheDataIsDamagedOrTheKeyIsNotThere(string change)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await FillAsync(folder.DatabasePath, protector, cancellationToken);
        await ExecuteAsync(folder.DatabasePath, change, cancellationToken);

        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);
        var result = await localData.OpenAsync(cancellationToken);

        Assert.False(result.IsOpen);
        Assert.Equal(change.Contains("protection", StringComparison.Ordinal) ? LocalDataProblem.KeyNotFound : LocalDataProblem.Damaged, result.Problem);
    }

    [Fact]
    public async Task Open_FileThatIsNotADatabase_SaysTheDataIsDamaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        Directory.CreateDirectory(Path.GetDirectoryName(folder.DatabasePath)!);
        await File.WriteAllTextAsync(folder.DatabasePath, string.Concat(Enumerable.Repeat("Not a database. ", 300)), cancellationToken);

        await using var localData = new DatabaseLocalData(folder.DatabasePath, new TestKeyProtector());

        Assert.Equal(LocalDataProblem.Damaged, (await localData.OpenAsync(cancellationToken)).Problem);
    }

    [Fact]
    public async Task Open_EmptyDatabaseWithoutAKey_GetsANewKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        Directory.CreateDirectory(Path.GetDirectoryName(folder.DatabasePath)!);
        await ExecuteAsync(folder.DatabasePath, "CREATE TABLE placeholder (id INTEGER PRIMARY KEY); DROP TABLE placeholder;", cancellationToken);
        var protector = new TestKeyProtector();

        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);
        var result = await localData.OpenAsync(cancellationToken);

        Assert.True(result.IsOpen);
        Assert.Equal(LocalDataMode.Saved, result.Stores.Mode);
        Assert.Equal(1, protector.ProtectCalls);
    }

    [Fact]
    public void InMemory_IsNeverSaved()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryLocalData(LocalDataMode.Saved));
    }

    [Theory]
    [InlineData(LocalDataMode.MemoryOnly)]
    [InlineData(LocalDataMode.MemoryOnlyNoKeyStore)]
    public async Task InMemory_Open_GivesEmptyStoresInItsMode(LocalDataMode mode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var localData = new InMemoryLocalData(mode);

        var result = await localData.OpenAsync(cancellationToken);

        Assert.True(result.IsOpen);
        Assert.Equal(mode, result.Stores.Mode);
        Assert.Null(await result.Stores.Identity.LoadAsync(cancellationToken));
        Assert.Same(result.Stores, (await localData.OpenAsync(cancellationToken)).Stores);
    }

    private static (MessageId, MessageDirection, long, DateTimeOffset, string, DateTimeOffset) Shape(ChatMessage message) =>
        (message.Id, message.Direction, message.Sequence, message.WrittenAt, message.Text, message.RecordedAt);

    private static async Task<StoredSample> FillAsync(string databasePath, TestKeyProtector protector, CancellationToken cancellationToken)
    {
        await using var localData = new DatabaseLocalData(databasePath, protector);
        var result = await localData.OpenAsync(cancellationToken);
        Assert.True(result.IsOpen);

        var identities = new IdentityService(result.Stores.Identity);
        var created = await identities.CreateAsync("Alice", cancellationToken);
        Assert.True(created.IsCreated);
        using var identity = created.Identity;

        using var bobKeys = KeySet.Create();
        var contact = new Contact(TestKeys.Code(bobKeys, ContactName), ContactState.Added, KeyComparison.CheckedByYou);
        await result.Stores.Contacts.SaveAsync(contact, cancellationToken);

        var recorded = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
        ChatMessage[] messages =
        [
            new(bobKeys.IdentityKey, MessageDirection.Outgoing, MessageId.Create(), 1, recorded, "Hello", recorded.AddTicks(1)),
            new(bobKeys.IdentityKey, MessageDirection.Incoming, MessageId.Create(), 1, recorded.AddSeconds(3), MessageText, recorded.AddSeconds(4)),
        ];

        foreach (var message in messages)
        {
            await result.Stores.Chats.AddAsync(message, cancellationToken);
            await result.Stores.SeenMessageIds.AddAsync(message.Id, cancellationToken);
        }

        return new StoredSample(identity.Keys.IdentityKey, contact, messages);
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

    private sealed record StoredSample(Ed25519PublicKey IdentityKey, Contact Contact, IReadOnlyList<ChatMessage> Messages);
}
