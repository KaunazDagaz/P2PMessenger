using P2PMessenger.Application.Identity;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Application.LocalData;

internal sealed class DatabaseIdentityStore : IIdentityStore
{
    private readonly IdentityRepository repository;
    private OwnIdentity? current;

    public DatabaseIdentityStore(IdentityRepository repository, OwnIdentity? loaded)
    {
        this.repository = repository;
        current = loaded;
    }

    public ValueTask<OwnIdentity?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Volatile.Read(ref current));

    public async ValueTask<bool> TrySaveAsync(OwnIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (Volatile.Read(ref current) is not null
            || !await repository.TryCreateAsync(identity.Keys, identity.Name, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        return Interlocked.CompareExchange(ref current, identity, null) is null;
    }
}
