using P2PMessenger.Application.LocalData;

namespace P2PMessenger.Application.Tests.Locking;

internal sealed class LockedLocalData : ILocalData
{
    public const string RightPassphrase = "lantern orchard pebble";

    public bool SupportsPassphrase => true;

    public bool HasKeyStore => true;

    public LocalDataProtection Protection { get; set; } = LocalDataProtection.Passphrase;

    public int Checks { get; private set; }

    public Task<LocalDataResult> OpenAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LocalDataResult> CreateAsync(string? passphrase, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LocalDataResult> UnlockAsync(string passphrase, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> CheckPassphraseAsync(string passphrase, CancellationToken cancellationToken)
    {
        Checks++;
        return Task.FromResult(string.Equals(passphrase, RightPassphrase, StringComparison.Ordinal));
    }

    public Task<PassphraseChangeResult> SetPassphraseAsync(string passphrase, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<PassphraseChangeResult> ChangePassphraseAsync(string currentPassphrase, string newPassphrase, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<PassphraseChangeResult> RemovePassphraseAsync(string currentPassphrase, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> DeleteAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
