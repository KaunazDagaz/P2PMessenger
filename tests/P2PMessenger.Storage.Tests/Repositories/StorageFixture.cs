using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Identity;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Storage.Tests.Repositories;

internal sealed class StorageFixture : IAsyncDisposable
{
    private readonly TemporaryDatabase file;
    private MessengerDatabase? database;
    private StoredData? data;

    private StorageFixture(TemporaryDatabase file)
    {
        this.file = file;
    }

    public string FilePath => file.FilePath;

    public StoredData Data => data ?? throw new InvalidOperationException("The fixture is closed.");

    public static async Task<StorageFixture> OpenAsync(CancellationToken cancellationToken)
    {
        StorageFixture fixture = new(new TemporaryDatabase());
        await fixture.ReopenAsync(Samples.DataKey(), cancellationToken);
        return fixture;
    }

    public async Task ReopenAsync(byte[] dataKey, CancellationToken cancellationToken)
    {
        await CloseAsync();
        var opened = await MessengerDatabase.OpenAsync(file.FilePath, cancellationToken);
        Assert.Equal(DatabaseOpenOutcome.Opened, opened.Outcome);
        database = opened.Database!;
        data = database.Unlock(dataKey);
    }

    public async Task CloseAsync()
    {
        data?.Dispose();
        data = null;
        if (database is not null)
        {
            await database.DisposeAsync();
            database = null;
        }
    }

    public async Task<SqliteConnection> RawAsync(CancellationToken cancellationToken)
    {
        var connectionString = SqliteDatabase.ConnectionStringFor(file.FilePath);
        connectionString.Pooling = false;
        return await SqliteDatabase.OpenAsync(connectionString, cancellationToken);
    }

    public async Task<(KeySet User, KeySet Bob, KeySet Carol)> FillAsync(CancellationToken cancellationToken)
    {
        var user = KeySet.Create();
        var bob = KeySet.Create();
        var carol = KeySet.Create();
        Assert.True(await Data.Identity.TryCreateAsync(user, Samples.Name("Alice"), cancellationToken));
        await Data.Contacts.SaveAsync(Samples.Contact(bob, "Bob", StoredContactStatus.Confirmed, keyChecked: true), cancellationToken);
        await Data.Contacts.SaveAsync(Samples.Contact(carol, "Carol"), cancellationToken);
        await Data.Messages.AddAsync(Samples.Message(bob.IdentityKey, StoredMessageDirection.Outgoing, 1, "Hello Bob", 0), cancellationToken);
        await Data.Messages.AddAsync(Samples.Message(bob.IdentityKey, StoredMessageDirection.Incoming, 1, "Hi Alice", 1), cancellationToken);
        await Data.Messages.AddAsync(Samples.Message(carol.IdentityKey, StoredMessageDirection.Incoming, 1, "Carol here", 2), cancellationToken);
        return (user, bob, carol);
    }

    public async Task LoadEverythingAsync(CancellationToken cancellationToken)
    {
        var identity = await Data.Identity.LoadAsync(cancellationToken);
        identity?.Keys.Dispose();
        await Data.Contacts.LoadAllAsync(cancellationToken);
        await Data.Messages.LoadAllAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        file.Dispose();
    }
}
