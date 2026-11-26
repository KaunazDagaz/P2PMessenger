using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Data.Sqlite;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Groups;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Tests.LocalData;

public sealed class DatabaseLocalDataTests
{
    private const string ContactName = "Yorick Marlowe";
    private const string MessageText = "The keyword is periwinkle-avalanche-7341";

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Open_NoDatabase_SaysItIsNotCreatedAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);

        var result = await localData.OpenAsync(cancellationToken);

        Assert.Equal(LocalDataStatus.NotCreated, result.Status);
        Assert.False(result.IsOpen);
        Assert.Equal(LocalDataProblem.None, result.Problem);
        Assert.Equal(0, protector.ProtectCalls);
        Assert.Empty(folder.Files());
        Assert.Equal(LocalDataProtection.None, localData.Protection);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Create_WithoutPassphrase_CreatesADatabaseWhoseKeyTheKeyStoreWraps()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);

        var result = await localData.CreateAsync(null, cancellationToken);

        Assert.True(result.IsOpen);
        Assert.Equal(LocalDataStatus.Opened, result.Status);
        Assert.Equal(LocalDataMode.Saved, result.Stores.Mode);
        Assert.Equal(LocalDataProtection.KeyStore, localData.Protection);
        Assert.Equal(1, protector.ProtectCalls);
        Assert.True(File.Exists(folder.DatabasePath));
        Assert.Equal(2L, await ScalarAsync(folder.DatabasePath, "SELECT protection FROM data_key;", cancellationToken));
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

        var created = await localData.CreateAsync(null, cancellationToken);
        var first = await localData.OpenAsync(cancellationToken);
        var second = await localData.OpenAsync(cancellationToken);
        var again = await localData.CreateAsync(null, cancellationToken);

        Assert.Same(created.Stores, first.Stores);
        Assert.Same(first.Stores, second.Stores);
        Assert.Same(first.Stores, again.Stores);
        Assert.Equal(1, protector.ProtectCalls);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Create_NoPassphraseAndTheKeyStoreDoesNotAnswer_AsksForAPassphraseAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector { Protection = KeyProtection.LinuxSecretService, Outcome = KeyStoreOutcome.Unavailable };
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);

        var result = await localData.CreateAsync(null, cancellationToken);

        Assert.Equal(LocalDataStatus.PassphraseRequired, result.Status);
        Assert.False(result.IsOpen);
        Assert.Empty(folder.Files());
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Create_NoPassphraseAndNoKeyStore_KeepsTheDataInMemoryAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        await using var localData = new DatabaseLocalData(folder.DatabasePath, null);

        var result = await localData.CreateAsync(null, cancellationToken);

        Assert.True(result.IsOpen);
        Assert.Equal(LocalDataMode.MemoryOnlyNoKeyStore, result.Stores.Mode);
        Assert.IsType<InMemoryIdentityStore>(result.Stores.Identity);
        Assert.Equal(LocalDataProtection.None, localData.Protection);
        Assert.False(localData.HasKeyStore);
        Assert.Empty(folder.Files());
        Assert.Same(result.Stores, (await localData.OpenAsync(cancellationToken)).Stores);
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData(KeyStoreOutcome.Refused, LocalDataProblem.KeyStoreRefused)]
    [InlineData(KeyStoreOutcome.KeyNotFound, LocalDataProblem.KeyNotFound)]
    public async Task Create_KeyStoreFailsForANewDatabase_ReportsItAndWritesNothing(KeyStoreOutcome outcome, LocalDataProblem problem)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector { Outcome = outcome };
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);

        var result = await localData.CreateAsync(null, cancellationToken);

        Assert.False(result.IsOpen);
        Assert.Equal(LocalDataStatus.Failed, result.Status);
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
    [Trait("Story", "US-02")]
    [Trait("Story", "US-04")]
    public async Task Open_AfterARestart_KeepsWhetherEachContactWasAddedInPerson()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        using var bobKeys = KeySet.Create();
        using var carolKeys = KeySet.Create();
        await using (var created = new DatabaseLocalData(folder.DatabasePath, protector))
        {
            var result = await created.CreateAsync(null, cancellationToken);
            Assert.True(result.IsOpen);
            var identities = new IdentityService(result.Stores.Identity);
            var identity = await identities.CreateAsync("Alice", cancellationToken);
            Assert.True(identity.IsCreated);
            identity.Identity.Dispose();
            await result.Stores.Contacts.SaveAsync(new Contact(TestKeys.Code(bobKeys, "Bob"), ContactState.WaitingForFirstConnection, KeyComparison.CheckedByYou, CodeExchange.InPerson), cancellationToken);
            await result.Stores.Contacts.SaveAsync(new Contact(TestKeys.Code(carolKeys, "Carol"), ContactState.Added, KeyComparison.NotCompared, CodeExchange.OtherChannel), cancellationToken);
        }

        Assert.Equal(1L, await ScalarAsync(folder.DatabasePath, "SELECT count(*) FROM contact WHERE added_via = 1;", cancellationToken));
        Assert.Equal(1L, await ScalarAsync(folder.DatabasePath, "SELECT count(*) FROM contact WHERE added_via = 2;", cancellationToken));

        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);
        var opened = await localData.OpenAsync(cancellationToken);
        Assert.True(opened.IsOpen);
        var contacts = await opened.Stores.Contacts.LoadAllAsync(cancellationToken);
        var bob = Assert.Single(contacts, contact => contact.IdentityKey.Equals(bobKeys.IdentityKey));
        var carol = Assert.Single(contacts, contact => contact.IdentityKey.Equals(carolKeys.IdentityKey));
        Assert.Equal(CodeExchange.InPerson, bob.Exchange);
        Assert.Equal(KeyComparison.CheckedByYou, bob.KeyComparison);
        Assert.Equal(CodeExchange.OtherChannel, carol.Exchange);
        Assert.Equal(KeyComparison.NotCompared, carol.KeyComparison);
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

    [Theory]
    [Trait("Story", "US-20")]
    [Trait("Story", "US-12")]
    [InlineData("UPDATE group_key SET record_enc = randomblob(300);")]
    [InlineData("UPDATE message SET group_envelope_enc = randomblob(200) WHERE group_envelope_enc IS NOT NULL;")]
    [InlineData("UPDATE conversation SET deleted_ranges_enc = randomblob(100) WHERE group_id IS NOT NULL;")]
    public async Task Open_ChangedGroupData_SaysTheDataIsDamaged(string change)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await FillWithGroupAsync(folder.DatabasePath, protector, cancellationToken);
        await using (var intact = new DatabaseLocalData(folder.DatabasePath, protector))
        {
            Assert.True((await intact.OpenAsync(cancellationToken)).IsOpen);
        }

        await ExecuteAsync(folder.DatabasePath, change, cancellationToken);

        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);
        var result = await localData.OpenAsync(cancellationToken);

        Assert.False(result.IsOpen);
        Assert.Equal(LocalDataProblem.Damaged, result.Problem);
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
    public async Task Open_EmptyDatabaseWithoutAKey_SaysItIsNotCreatedAndCreateGivesItAKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        Directory.CreateDirectory(Path.GetDirectoryName(folder.DatabasePath)!);
        await ExecuteAsync(folder.DatabasePath, "CREATE TABLE placeholder (id INTEGER PRIMARY KEY); DROP TABLE placeholder;", cancellationToken);
        var protector = new TestKeyProtector();

        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector);
        Assert.Equal(LocalDataStatus.NotCreated, (await localData.OpenAsync(cancellationToken)).Status);
        var result = await localData.CreateAsync(null, cancellationToken);

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
        var result = await localData.CreateAsync(null, cancellationToken);
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

    private static async Task FillWithGroupAsync(string databasePath, TestKeyProtector protector, CancellationToken cancellationToken)
    {
        await using var localData = new DatabaseLocalData(databasePath, protector);
        var result = await localData.CreateAsync(null, cancellationToken);
        Assert.True(result.IsOpen);
        var created = await new IdentityService(result.Stores.Identity).CreateAsync("Alice", cancellationToken);
        Assert.True(created.IsCreated);
        using var identity = created.Identity;
        using var bobKeys = KeySet.Create();
        await result.Stores.Contacts.SaveAsync(new Contact(TestKeys.Code(bobKeys, "Bob"), ContactState.Added, KeyComparison.CheckedByYou), cancellationToken);

        using var key = GroupKey.Create();
        GroupMember[] members = [new(identity.Keys.IdentityKey, identity.Name), new(bobKeys.IdentityKey, TestKeys.Name("Bob"))];
        var record = GroupRecord.Create(GroupId.Create(), 1, TestKeys.Name("Team"), key, members, bobKeys);
        GroupMemberStatus[] statuses = [.. record.Members.Select(static member => new GroupMemberStatus(member.IdentityKey, member.Name, MemberJoin.Joined, NoticeState.None, NoticeState.None))];
        await result.Stores.Groups.AddGroupAsync(record, key, statuses, cancellationToken);
        Assert.True(GroupText.TryCreate(MessageId.Create(), 2, DateTimeOffset.UnixEpoch, "Hello team", out var text));
        var envelope = GroupMessage.Seal(key, record.GroupId, 1, bobKeys, text);
        var message = new GroupChatMessage(envelope, text.Id, 2, text.WrittenAt, text.Text, DateTimeOffset.UnixEpoch, new Dictionary<Ed25519PublicKey, DeliveryState>());
        Assert.True(await result.Stores.Groups.AddMessageAsync(message, cancellationToken));
        Assert.Equal(1, await result.Stores.Groups.DeleteChatAsync(record.GroupId, [new AuthorSequenceRange(bobKeys.IdentityKey, 1, 2)], cancellationToken));
        Assert.True(await result.Stores.Groups.AddMessageAsync(Again(key, record, bobKeys), cancellationToken));
    }

    private static GroupChatMessage Again(GroupKey key, GroupRecord record, KeySet author)
    {
        Assert.True(GroupText.TryCreate(MessageId.Create(), 3, DateTimeOffset.UnixEpoch, "Still here", out var text));
        var envelope = GroupMessage.Seal(key, record.GroupId, 1, author, text);
        return new GroupChatMessage(envelope, text.Id, 3, text.WrittenAt, text.Text, DateTimeOffset.UnixEpoch, new Dictionary<Ed25519PublicKey, DeliveryState>());
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
