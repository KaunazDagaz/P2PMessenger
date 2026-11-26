using System.Security.Cryptography;
using System.Text;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Storage.Tests.Repositories;

public sealed class CopiedFilesTests
{
    private const string UserName = "Zelda Quixote";
    private const string ContactName = "Yorick Marlowe";
    private const string MessageText = "The keyword is periwinkle-avalanche-7341";

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public async Task DatabaseFiles_HoldNoNameTextKeyOrMessageIdInPlain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        using var user = KeySet.Create();
        using var contact = KeySet.Create();
        var id = MessageId.Create();
        Assert.True(await store.Data.Identity.TryCreateAsync(user, Samples.Name(UserName), cancellationToken));
        await store.Data.Contacts.SaveAsync(Samples.Contact(contact, ContactName, StoredContactStatus.Confirmed, keyChecked: true), cancellationToken);
        await store.Data.Messages.AddAsync(Samples.Message(contact.IdentityKey, StoredMessageDirection.Incoming, 1, MessageText, 0) with { Id = id }, cancellationToken);
        await store.Data.SeenMessages.AddAsync(id, cancellationToken);
        await store.CloseAsync();

        var files = string.Join(", ", DatabaseFiles(store.FilePath).Select(Path.GetFileName));
        var contents = DatabaseFiles(store.FilePath).Select(File.ReadAllBytes).ToList();
        var exported = new byte[KeySet.ExportLength];
        Assert.True(user.TryExport(exported, out _));

        byte[][] secrets =
        [
            Encoding.UTF8.GetBytes(UserName),
            Encoding.UTF8.GetBytes(ContactName),
            Encoding.UTF8.GetBytes(MessageText),
            Encoding.Unicode.GetBytes(MessageText),
            Encoding.UTF8.GetBytes("periwinkle"),
            user.IdentityKey.Bytes.ToArray(),
            user.StaticKey.Bytes.ToArray(),
            user.OnionKey.Bytes.ToArray(),
            exported.AsSpan(1, 32).ToArray(),
            exported.AsSpan(33, 32).ToArray(),
            exported.AsSpan(65, 32).ToArray(),
            contact.IdentityKey.Bytes.ToArray(),
            contact.StaticKey.Bytes.ToArray(),
            contact.OnionKey.Bytes.ToArray(),
            id.Bytes.ToArray(),
            Samples.DataKey(),
        ];

        Assert.NotEmpty(contents);
        foreach (var secret in secrets)
        {
            Assert.All(contents, content => Assert.Equal(-1, content.AsSpan().IndexOf(secret)));
        }

        Assert.Contains("db", files, StringComparison.Ordinal);
        CryptographicOperations.ZeroMemory(exported);
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public async Task DatabaseFiles_CopiedAndOpenedWithAnotherDataKey_GiveNothingReadable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        user.Dispose();
        bob.Dispose();
        carol.Dispose();
        await store.CloseAsync();

        using var copy = new TemporaryDatabase();
        foreach (var file in DatabaseFiles(store.FilePath))
        {
            File.Copy(file, copy.FilePath + file[store.FilePath.Length..]);
        }

        await using var database = (await MessengerDatabase.OpenAsync(copy.FilePath, cancellationToken)).Database!;
        using var data = database.Unlock(RandomNumberGenerator.GetBytes(32));
        await Assert.ThrowsAsync<StoredDataException>(() => data.Identity.LoadAsync(cancellationToken));
        await Assert.ThrowsAsync<StoredDataException>(() => data.Contacts.LoadAllAsync(cancellationToken));
        await Assert.ThrowsAsync<StoredDataException>(() => data.Messages.LoadAllAsync(cancellationToken));
        Assert.False(await data.SeenMessages.ContainsAsync(MessageId.Create(), cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-11")]
    public async Task CopiedChatFiles_HoldNoCopiedNamesOrTextsInPlainAndRejectAnotherDataKey()
    {
        const string CopiedTitle = "Vespera Nightshade";
        const string CopiedAuthor = "Orpheus Lindholm";
        const string CopiedText = "Secret copied phrase amethyst-glacier-9042";

        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        using var phoneOwner = KeySet.Create();
        using var phoneContact = KeySet.Create();
        var origin = CopyOriginId.ForContact(phoneOwner.IdentityKey, phoneContact.IdentityKey);
        await store.Data.CopiedConversations.SaveAsync(
            new StoredCopiedConversation(
                origin,
                Samples.Name(CopiedTitle),
                Samples.WrittenAt.AddHours(1),
                [
                    new StoredCopiedMessage(1, true, null, "First copied note cobalt-lantern-5182", Samples.WrittenAt),
                    new StoredCopiedMessage(2, false, Samples.Name(CopiedAuthor), CopiedText, Samples.WrittenAt.AddMinutes(1)),
                ]),
            cancellationToken);
        await store.CloseAsync();

        var contents = DatabaseFiles(store.FilePath).Select(File.ReadAllBytes).ToList();
        Assert.NotEmpty(contents);

        byte[][] secrets =
        [
            Encoding.UTF8.GetBytes(CopiedTitle),
            Encoding.Unicode.GetBytes(CopiedTitle),
            Encoding.UTF8.GetBytes(CopiedAuthor),
            Encoding.Unicode.GetBytes(CopiedAuthor),
            Encoding.UTF8.GetBytes(CopiedText),
            Encoding.Unicode.GetBytes(CopiedText),
            Encoding.UTF8.GetBytes("amethyst"),
            Encoding.UTF8.GetBytes("cobalt-lantern"),
            origin.Bytes.ToArray(),
        ];

        foreach (var secret in secrets)
        {
            Assert.All(contents, content => Assert.Equal(-1, content.AsSpan().IndexOf(secret)));
        }

        using var copy = new TemporaryDatabase();
        foreach (var file in DatabaseFiles(store.FilePath))
        {
            File.Copy(file, copy.FilePath + file[store.FilePath.Length..]);
        }

        await using var database = (await MessengerDatabase.OpenAsync(copy.FilePath, cancellationToken)).Database!;
        using var data = database.Unlock(RandomNumberGenerator.GetBytes(32));
        await Assert.ThrowsAsync<StoredDataException>(() => data.CopiedConversations.LoadAllAsync(cancellationToken));
    }

    private static IEnumerable<string> DatabaseFiles(string path) =>
        new[] { path, path + "-wal", path + "-shm", path + "-journal" }.Where(File.Exists);
}
