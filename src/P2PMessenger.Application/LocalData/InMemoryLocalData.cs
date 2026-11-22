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

    public bool SupportsPassphrase => false;

    public bool HasKeyStore => false;

    public LocalDataProtection Protection => LocalDataProtection.None;

    public Task<LocalDataResult> OpenAsync(CancellationToken cancellationToken) => Task.FromResult(LocalDataResult.Opened(stores));

    public Task<LocalDataResult> CreateAsync(string? passphrase, CancellationToken cancellationToken) => OpenAsync(cancellationToken);

    public Task<LocalDataResult> UnlockAsync(string passphrase, CancellationToken cancellationToken) => OpenAsync(cancellationToken);

    public Task<bool> CheckPassphraseAsync(string passphrase, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<PassphraseChangeResult> SetPassphraseAsync(string passphrase, CancellationToken cancellationToken) => NotAvailable();

    public Task<PassphraseChangeResult> ChangePassphraseAsync(string currentPassphrase, string newPassphrase, CancellationToken cancellationToken) => NotAvailable();

    public Task<PassphraseChangeResult> RemovePassphraseAsync(string currentPassphrase, CancellationToken cancellationToken) => NotAvailable();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static Task<PassphraseChangeResult> NotAvailable() => Task.FromResult(PassphraseChangeResult.Of(PassphraseChangeOutcome.NotAvailable));
}
