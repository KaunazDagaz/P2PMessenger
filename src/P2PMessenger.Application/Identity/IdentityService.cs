using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Identity;

public sealed class IdentityService
{
    private readonly IIdentityStore store;

    public IdentityService(IIdentityStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
    }

    public ValueTask<OwnIdentity?> LoadAsync(CancellationToken cancellationToken) => store.LoadAsync(cancellationToken);

    public async Task<CreateIdentityResult> CreateAsync(string? name, CancellationToken cancellationToken)
    {
        if (!DisplayName.TryCreate(name, out DisplayName? displayName))
        {
            return CreateIdentityResult.Failed(CreateIdentityError.InvalidName);
        }

        if (await store.LoadAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return CreateIdentityResult.Failed(CreateIdentityError.AlreadyCreated);
        }

        OwnIdentity identity = await Task.Run(() => OwnIdentity.Create(displayName), cancellationToken).ConfigureAwait(false);
        bool saved;
        try
        {
            saved = await store.TrySaveAsync(identity, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            identity.Dispose();
            throw;
        }

        if (!saved)
        {
            identity.Dispose();
            return CreateIdentityResult.Failed(CreateIdentityError.AlreadyCreated);
        }

        return CreateIdentityResult.Created(identity);
    }
}
