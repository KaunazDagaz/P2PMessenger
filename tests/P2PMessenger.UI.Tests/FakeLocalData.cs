using P2PMessenger.Application.LocalData;

namespace P2PMessenger.UI.Tests;

internal sealed class FakeLocalData : ILocalData
{
    private readonly InMemoryLocalData memory;

    public FakeLocalData(LocalDataProblem problem, LocalDataMode mode = LocalDataMode.MemoryOnly)
    {
        Problem = problem;
        memory = new InMemoryLocalData(mode);
    }

    public LocalDataProblem Problem { get; set; }

    public int Opened { get; private set; }

    public bool SupportsPassphrase => false;

    public bool HasKeyStore => false;

    public LocalDataProtection Protection => LocalDataProtection.None;

    public async Task<LocalDataResult> OpenAsync(CancellationToken cancellationToken)
    {
        Opened++;
        return Problem == LocalDataProblem.None
            ? await memory.OpenAsync(cancellationToken)
            : LocalDataResult.Failed(Problem);
    }

    public Task<LocalDataResult> CreateAsync(string? passphrase, CancellationToken cancellationToken) => OpenAsync(cancellationToken);

    public Task<LocalDataResult> UnlockAsync(string passphrase, CancellationToken cancellationToken) => OpenAsync(cancellationToken);

    public Task<bool> CheckPassphraseAsync(string passphrase, CancellationToken cancellationToken) => memory.CheckPassphraseAsync(passphrase, cancellationToken);

    public Task<PassphraseChangeResult> SetPassphraseAsync(string passphrase, CancellationToken cancellationToken) => memory.SetPassphraseAsync(passphrase, cancellationToken);

    public Task<PassphraseChangeResult> ChangePassphraseAsync(string currentPassphrase, string newPassphrase, CancellationToken cancellationToken) =>
        memory.ChangePassphraseAsync(currentPassphrase, newPassphrase, cancellationToken);

    public Task<PassphraseChangeResult> RemovePassphraseAsync(string currentPassphrase, CancellationToken cancellationToken) => memory.RemovePassphraseAsync(currentPassphrase, cancellationToken);

    public int Deleted { get; private set; }

    public bool DeleteSucceeds { get; set; } = true;

    public async Task<bool> DeleteAllAsync(CancellationToken cancellationToken)
    {
        Deleted++;
        if (!DeleteSucceeds)
        {
            return false;
        }

        Problem = LocalDataProblem.None;
        return await memory.DeleteAllAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => memory.DisposeAsync();
}
