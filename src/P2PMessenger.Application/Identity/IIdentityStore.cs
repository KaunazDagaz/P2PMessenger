namespace P2PMessenger.Application.Identity;

public interface IIdentityStore
{
    ValueTask<OwnIdentity?> LoadAsync(CancellationToken cancellationToken);

    ValueTask<bool> TrySaveAsync(OwnIdentity identity, CancellationToken cancellationToken);
}
