namespace P2PMessenger.Application.LocalData;

public interface ILocalData : IAsyncDisposable
{
    bool SupportsPassphrase { get; }

    bool HasKeyStore { get; }

    LocalDataProtection Protection { get; }

    Task<LocalDataResult> OpenAsync(CancellationToken cancellationToken);

    Task<LocalDataResult> CreateAsync(string? passphrase, CancellationToken cancellationToken);

    Task<LocalDataResult> UnlockAsync(string passphrase, CancellationToken cancellationToken);

    Task<bool> CheckPassphraseAsync(string passphrase, CancellationToken cancellationToken);

    Task<PassphraseChangeResult> SetPassphraseAsync(string passphrase, CancellationToken cancellationToken);

    Task<PassphraseChangeResult> ChangePassphraseAsync(string currentPassphrase, string newPassphrase, CancellationToken cancellationToken);

    Task<PassphraseChangeResult> RemovePassphraseAsync(string currentPassphrase, CancellationToken cancellationToken);

    Task<bool> DeleteAllAsync(CancellationToken cancellationToken);
}
