using System.Net;
using P2PMessenger.Application;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Integration.Tests.Connections;

namespace P2PMessenger.Integration.Tests.AppSlice;

internal sealed class AppPeer : IAsyncDisposable
{
    private readonly List<ContactChangedEventArgs> changes = [];
    private readonly Lock gate = new();
    private readonly ILocalData? localData;

    private AppPeer(Messenger messenger, ILocalData? localData = null)
    {
        Messenger = messenger;
        this.localData = localData;
        messenger.Contacts.Changed += (_, change) =>
        {
            lock (gate)
            {
                changes.Add(change);
            }
        };
    }

    public Messenger Messenger { get; }

    public OwnIdentity Identity => Messenger.Identity;

    public IPEndPoint Endpoint => new(IPAddress.Loopback, Messenger.Connections.ListeningPort ?? throw new InvalidOperationException("The peer is not listening."));

    public IReadOnlyList<ContactChangedEventArgs> Changes
    {
        get
        {
            lock (gate)
            {
                return [.. changes];
            }
        }
    }

    public static Task<AppPeer> StartAsync(string name, CancellationToken cancellationToken) =>
        StartAsync(KeySet.Create(), name, cancellationToken);

    public static async Task<AppPeer> StartAsync(KeySet keys, string name, CancellationToken cancellationToken)
    {
        Assert.True(DisplayName.TryCreate(name, out var displayName));
        var setup = MessengerSetup.InMemory() with
        {
            ListenAddress = IPAddress.Loopback,
            ListenPort = 0,
            Connection = TestOptions.Fast,
        };
        return new AppPeer(await Messenger.StartAsync(new OwnIdentity(keys, displayName), setup, cancellationToken));
    }

    public static async Task<AppPeer> StartSavedAsync(string databasePath, IKeyProtector protector, string name, CancellationToken cancellationToken)
    {
        var localData = new DatabaseLocalData(databasePath, protector);
        try
        {
            var opened = await localData.OpenAsync(cancellationToken);
            Assert.True(opened.IsOpen);
            Assert.Equal(LocalDataMode.Saved, opened.Stores.Mode);

            var identities = new IdentityService(opened.Stores.Identity);
            var identity = await identities.LoadAsync(cancellationToken);
            if (identity is null)
            {
                var created = await identities.CreateAsync(name, cancellationToken);
                Assert.True(created.IsCreated);
                identity = created.Identity;
            }

            var setup = MessengerSetup.InMemory() with
            {
                ContactStore = opened.Stores.Contacts,
                ChatStore = opened.Stores.Chats,
                SeenMessageIds = opened.Stores.SeenMessageIds,
                ListenAddress = IPAddress.Loopback,
                ListenPort = 0,
                Connection = TestOptions.Fast,
            };
            return new AppPeer(await Messenger.StartAsync(identity, setup, cancellationToken), localData);
        }
        catch
        {
            await localData.DisposeAsync();
            throw;
        }
    }

    public async Task<Contact> ImportAsync(string codeText, CodeCheckOutcome expected, CancellationToken cancellationToken)
    {
        var check = Messenger.Contacts.Check(codeText);
        Assert.Equal(expected, check.Outcome);
        var result = await Messenger.Contacts.AddAsync(check, cancellationToken);
        Assert.True(result.IsSaved);
        return result.Contact;
    }

    public Task<Contact> ImportAsync(AppPeer other, CancellationToken cancellationToken) =>
        ImportAsync(other.Identity.CodeText, CodeCheckOutcome.New, cancellationToken);

    public Contact ContactFor(AppPeer other) => ContactFor(other.Identity.Keys.IdentityKey);

    public Contact ContactFor(KeySet other) => ContactFor(other.IdentityKey);

    public Contact ContactFor(Ed25519PublicKey identityKey)
    {
        Assert.True(Messenger.Contacts.TryGet(identityKey, out var contact));
        return contact;
    }

    public async ValueTask DisposeAsync()
    {
        await Messenger.DisposeAsync();
        Messenger.Identity.Dispose();
        if (localData is not null)
        {
            await localData.DisposeAsync();
        }
    }
}
