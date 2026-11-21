using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;

namespace P2PMessenger.Application.LocalData;

public sealed class InMemoryLocalData : ILocalData
{
    private readonly LocalDataStores stores;

    public InMemoryLocalData(LocalDataMode mode = LocalDataMode.MemoryOnly)
    {
        if (mode is not (LocalDataMode.MemoryOnly or LocalDataMode.MemoryOnlyNoKeyStore))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Data in memory is never saved.");
        }

        InMemorySeenMessageIds seen = new();
        stores = new LocalDataStores(new InMemoryIdentityStore(), new InMemoryContactStore(), new InMemoryChatStore(seen), seen, mode);
    }

    public Task<LocalDataResult> OpenAsync(CancellationToken cancellationToken) => Task.FromResult(LocalDataResult.Opened(stores));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
