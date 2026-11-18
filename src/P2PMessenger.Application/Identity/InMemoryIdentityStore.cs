namespace P2PMessenger.Application.Identity;

public sealed class InMemoryIdentityStore : IIdentityStore
{
    private readonly Lock gate = new();
    private OwnIdentity? current;

    public ValueTask<OwnIdentity?> LoadAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return ValueTask.FromResult(current);
        }
    }

    public ValueTask<bool> TrySaveAsync(OwnIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        lock (gate)
        {
            if (current is not null)
            {
                return ValueTask.FromResult(false);
            }

            current = identity;
            return ValueTask.FromResult(true);
        }
    }
}
