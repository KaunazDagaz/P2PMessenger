using System.Security.Cryptography;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;
using P2PMessenger.Storage;
using P2PMessenger.Storage.Protection;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Application.LocalData;

public sealed class DatabaseLocalData : ILocalData
{
    public const string FileName = "messenger.db";

    private readonly IKeyProtector protector;
    private readonly SemaphoreSlim opening = new(1, 1);
    private MessengerDatabase? database;
    private StoredData? data;
    private LocalDataResult? opened;

    public DatabaseLocalData(string databasePath, IKeyProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(protector);

        DatabasePath = Path.GetFullPath(databasePath);
        this.protector = protector;
    }

    public string DatabasePath { get; }

    public async Task<LocalDataResult> OpenAsync(CancellationToken cancellationToken)
    {
        await opening.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (opened is { } done)
            {
                return done;
            }

            LocalDataResult result;
            try
            {
                result = await OpenOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await CloseAsync().ConfigureAwait(false);
                throw;
            }

            if (result.IsOpen)
            {
                opened = result;
            }
            else
            {
                await CloseAsync().ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            opening.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await opening.WaitAsync().ConfigureAwait(false);
        try
        {
            opened = null;
            await CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            opening.Release();
        }
    }

    private static DataKeyProtection StorageCode(KeyProtection protection) => protection switch
    {
        KeyProtection.Passphrase => DataKeyProtection.Passphrase,
        KeyProtection.WindowsDpapi => DataKeyProtection.WindowsDpapi,
        KeyProtection.AndroidKeystore => DataKeyProtection.AndroidKeystore,
        KeyProtection.LinuxSecretService => DataKeyProtection.LinuxSecretService,
        _ => throw new ArgumentOutOfRangeException(nameof(protection), protection, "Unknown key protection."),
    };

    private static LocalDataProblem ProblemOf(KeyStoreOutcome outcome) => outcome switch
    {
        KeyStoreOutcome.Unavailable => LocalDataProblem.KeyStoreUnavailable,
        KeyStoreOutcome.Refused => LocalDataProblem.KeyStoreRefused,
        KeyStoreOutcome.KeyNotFound => LocalDataProblem.KeyNotFound,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The outcome is not a failure."),
    };

    private static LocalDataResult InMemoryWithoutKeyStore() =>
        LocalDataResult.Opened(new LocalDataStores(
            new InMemoryIdentityStore(),
            new InMemoryContactStore(),
            new InMemoryChatStore(),
            new InMemorySeenMessageIds(),
            LocalDataMode.MemoryOnlyNoKeyStore));

    private async Task<LocalDataResult> OpenOnceAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(DatabasePath))
        {
            return await CreateAsync(cancellationToken).ConfigureAwait(false);
        }

        DatabaseOpenResult result = await MessengerDatabase.OpenAsync(DatabasePath, cancellationToken).ConfigureAwait(false);
        if (!result.IsOpened)
        {
            return LocalDataResult.Failed(result.Outcome == DatabaseOpenOutcome.NewerThanApp ? LocalDataProblem.NewerVersion : LocalDataProblem.Damaged);
        }

        database = result.Database;
        try
        {
            WrappedDataKey? wrapped = await database.ReadDataKeyAsync(cancellationToken).ConfigureAwait(false);
            if (wrapped is null)
            {
                return await database.HoldsDataAsync(cancellationToken).ConfigureAwait(false)
                    ? LocalDataResult.Failed(LocalDataProblem.Damaged)
                    : await CreateAsync(cancellationToken).ConfigureAwait(false);
            }

            if (wrapped.Protection != StorageCode(protector.Protection))
            {
                return LocalDataResult.Failed(LocalDataProblem.KeyNotFound);
            }

            byte[] dataKey = new byte[RecordKeys.DataKeyLength];
            try
            {
                KeyStoreOutcome outcome = await protector.UnprotectAsync(wrapped.WrappedKey, dataKey, cancellationToken).ConfigureAwait(false);
                return outcome == KeyStoreOutcome.Succeeded
                    ? await UnlockAsync(dataKey, cancellationToken).ConfigureAwait(false)
                    : LocalDataResult.Failed(ProblemOf(outcome));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
        catch (StoredDataException)
        {
            return LocalDataResult.Failed(LocalDataProblem.Damaged);
        }
    }

    private async Task<LocalDataResult> CreateAsync(CancellationToken cancellationToken)
    {
        byte[] dataKey = RandomNumberGenerator.GetBytes(RecordKeys.DataKeyLength);
        try
        {
            ProtectResult protectedKey = await protector.ProtectAsync(dataKey, cancellationToken).ConfigureAwait(false);
            if (protectedKey.Outcome == KeyStoreOutcome.Unavailable)
            {
                await CloseAsync().ConfigureAwait(false);
                return InMemoryWithoutKeyStore();
            }

            if (!protectedKey.Succeeded)
            {
                return LocalDataResult.Failed(ProblemOf(protectedKey.Outcome));
            }

            if (database is null)
            {
                CreateFolder();
                DatabaseOpenResult result = await MessengerDatabase.OpenAsync(DatabasePath, cancellationToken).ConfigureAwait(false);
                if (!result.IsOpened)
                {
                    return LocalDataResult.Failed(result.Outcome == DatabaseOpenOutcome.NewerThanApp ? LocalDataProblem.NewerVersion : LocalDataProblem.Damaged);
                }

                database = result.Database;
            }

            await database.SaveDataKeyAsync(new WrappedDataKey(StorageCode(protector.Protection), protectedKey.ProtectedKey.Span), cancellationToken).ConfigureAwait(false);
            return await UnlockAsync(dataKey, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private async Task<LocalDataResult> UnlockAsync(byte[] dataKey, CancellationToken cancellationToken)
    {
        MessengerDatabase unlocked = database ?? throw new InvalidOperationException("The database is not open.");
        data = unlocked.Unlock(dataKey);

        StoredIdentity? identity = null;
        try
        {
            identity = await data.Identity.LoadAsync(cancellationToken).ConfigureAwait(false);
            await data.Contacts.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            await data.Messages.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (StoredDataException)
        {
            identity?.Keys.Dispose();
            return LocalDataResult.Failed(LocalDataProblem.Damaged);
        }
        catch
        {
            identity?.Keys.Dispose();
            throw;
        }

        OwnIdentity? own = identity is null ? null : new OwnIdentity(identity.Keys, identity.Name);
        return LocalDataResult.Opened(new LocalDataStores(
            new DatabaseIdentityStore(data.Identity, own),
            new DatabaseContactStore(data.Contacts),
            new DatabaseChatStore(data.Messages),
            data.SeenMessages,
            LocalDataMode.Saved));
    }

    private void CreateFolder()
    {
        string? folder = Path.GetDirectoryName(DatabasePath);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
        }
        else
        {
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private async ValueTask CloseAsync()
    {
        data?.Dispose();
        data = null;
        if (database is not null)
        {
            await database.DisposeAsync().ConfigureAwait(false);
            database = null;
        }
    }
}
