using System.Security.Cryptography;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Copy;
using P2PMessenger.Application.Groups;
using P2PMessenger.Application.Identity;
using P2PMessenger.Core;
using P2PMessenger.Storage;
using P2PMessenger.Storage.Protection;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Application.LocalData;

public sealed class DatabaseLocalData : ILocalData
{
    public const string FileName = "messenger.db";

    private static readonly string[] FileSuffixes = [string.Empty, "-wal", "-shm", "-journal"];

    private readonly IKeyProtector? keyStore;
    private readonly PassphraseKdf kdf;
    private readonly SemaphoreSlim gate = new(1, 1);
    private MessengerDatabase? database;
    private StoredData? data;
    private WrappedDataKey? wrapped;
    private byte[]? share;
    private LocalDataResult? opened;
    private int protection;

    public DatabaseLocalData(string databasePath, IKeyProtector? keyStore)
        : this(databasePath, keyStore, PassphraseKdf.Default)
    {
    }

    public DatabaseLocalData(string databasePath, IKeyProtector? keyStore, PassphraseKdf kdf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!kdf.IsAcceptable)
        {
            throw new ArgumentOutOfRangeException(nameof(kdf), kdf, "The key-derivation parameters are outside the accepted range.");
        }

        DatabasePath = Path.GetFullPath(databasePath);
        this.keyStore = keyStore;
        this.kdf = kdf;
    }

    public string DatabasePath { get; }

    public bool SupportsPassphrase => true;

    public bool HasKeyStore => keyStore is not null;

    public LocalDataProtection Protection => (LocalDataProtection)Volatile.Read(ref protection);

    public async Task<LocalDataResult> OpenAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return opened ?? await GuardAsync(OpenOnceAsync, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<LocalDataResult> CreateAsync(string? passphrase, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (opened is { } done)
            {
                return done;
            }

            LocalDataResult current = await GuardAsync(OpenOnceAsync, cancellationToken).ConfigureAwait(false);
            return current.Status == LocalDataStatus.NotCreated
                ? await GuardAsync(token => CreateOnceAsync(passphrase, token), cancellationToken).ConfigureAwait(false)
                : current;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<LocalDataResult> UnlockAsync(string passphrase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passphrase);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (opened is { } done)
            {
                return done;
            }

            if (database is null || wrapped is not { Protection: DataKeyProtection.Passphrase })
            {
                LocalDataResult current = await GuardAsync(OpenOnceAsync, cancellationToken).ConfigureAwait(false);
                if (current.Status != LocalDataStatus.PassphraseNeeded)
                {
                    return current;
                }
            }

            return await GuardAsync(token => UnlockOnceAsync(passphrase, token), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> CheckPassphraseAsync(string passphrase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passphrase);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (opened is null || wrapped is not { Protection: DataKeyProtection.Passphrase } current)
            {
                return false;
            }

            byte[] dataKey = new byte[RecordKeys.DataKeyLength];
            try
            {
                return await UnwrapAsync(current, passphrase, dataKey, cancellationToken).ConfigureAwait(false) == PassphraseUnwrapOutcome.Succeeded;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PassphraseChangeResult> SetPassphraseAsync(string passphrase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passphrase);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (opened is null || database is null || keyStore is null || wrapped is not { } current || current.Protection == DataKeyProtection.Passphrase)
            {
                return PassphraseChangeResult.Of(PassphraseChangeOutcome.NotAvailable);
            }

            if (PassphrasePolicy.Check(passphrase) != PassphraseProblem.None)
            {
                return PassphraseChangeResult.Of(PassphraseChangeOutcome.NotAccepted);
            }

            byte[] dataKey = new byte[RecordKeys.DataKeyLength];
            try
            {
                KeyStoreOutcome outcome = await keyStore.UnprotectAsync(current.WrappedKey, dataKey, cancellationToken).ConfigureAwait(false);
                if (outcome != KeyStoreOutcome.Succeeded)
                {
                    return PassphraseChangeResult.KeyStoreFailed(ProblemOf(outcome));
                }

                (WrappedDataKey? replacement, byte[]? newShare, KeyStoreOutcome shareOutcome) = await WrapWithShareAsync(dataKey, passphrase, cancellationToken).ConfigureAwait(false);
                if (replacement is null)
                {
                    return PassphraseChangeResult.KeyStoreFailed(ProblemOf(shareOutcome));
                }

                try
                {
                    await ReplaceAsync(replacement, LocalDataProtection.Passphrase, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    ClearShare(newShare);
                    await ForgetQuietlyAsync(replacement).ConfigureAwait(false);
                    throw;
                }

                SetShare(newShare);
                await ForgetQuietlyAsync(current).ConfigureAwait(false);
                return PassphraseChangeResult.Of(PassphraseChangeOutcome.Changed);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PassphraseChangeResult> ChangePassphraseAsync(string currentPassphrase, string newPassphrase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentPassphrase);
        ArgumentNullException.ThrowIfNull(newPassphrase);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (opened is null || database is null || wrapped is not { Protection: DataKeyProtection.Passphrase } current)
            {
                return PassphraseChangeResult.Of(PassphraseChangeOutcome.NotAvailable);
            }

            if (PassphrasePolicy.Check(newPassphrase) != PassphraseProblem.None)
            {
                return PassphraseChangeResult.Of(PassphraseChangeOutcome.NotAccepted);
            }

            byte[] dataKey = new byte[RecordKeys.DataKeyLength];
            try
            {
                if (await UnwrapAsync(current, currentPassphrase, dataKey, cancellationToken).ConfigureAwait(false) != PassphraseUnwrapOutcome.Succeeded)
                {
                    return PassphraseChangeResult.Of(PassphraseChangeOutcome.WrongPassphrase);
                }

                WrappedDataKey replacement = await RewrapAsync(current, dataKey, newPassphrase, cancellationToken).ConfigureAwait(false);
                await ReplaceAsync(replacement, LocalDataProtection.Passphrase, cancellationToken).ConfigureAwait(false);
                return PassphraseChangeResult.Of(PassphraseChangeOutcome.Changed);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PassphraseChangeResult> RemovePassphraseAsync(string currentPassphrase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentPassphrase);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (opened is null || database is null || keyStore is null || wrapped is not { Protection: DataKeyProtection.Passphrase } current)
            {
                return PassphraseChangeResult.Of(PassphraseChangeOutcome.NotAvailable);
            }

            byte[] dataKey = new byte[RecordKeys.DataKeyLength];
            try
            {
                if (await UnwrapAsync(current, currentPassphrase, dataKey, cancellationToken).ConfigureAwait(false) != PassphraseUnwrapOutcome.Succeeded)
                {
                    return PassphraseChangeResult.Of(PassphraseChangeOutcome.WrongPassphrase);
                }

                ProtectResult protectedKey = await keyStore.ProtectAsync(dataKey, cancellationToken).ConfigureAwait(false);
                if (!protectedKey.Succeeded)
                {
                    return PassphraseChangeResult.KeyStoreFailed(ProblemOf(protectedKey.Outcome));
                }

                WrappedDataKey replacement = new(StorageCode(keyStore.Protection), protectedKey.ProtectedKey.Span);
                try
                {
                    await ReplaceAsync(replacement, LocalDataProtection.KeyStore, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await ForgetQuietlyAsync(replacement).ConfigureAwait(false);
                    throw;
                }

                SetShare(null);
                await ForgetQuietlyAsync(current).ConfigureAwait(false);
                return PassphraseChangeResult.Of(PassphraseChangeOutcome.Changed);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> DeleteAllAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WrappedDataKey? held = wrapped;
            if (database is not null)
            {
                held ??= await ReadKeyQuietlyAsync(database, cancellationToken).ConfigureAwait(false);
                await EraseQuietlyAsync(database, cancellationToken).ConfigureAwait(false);
            }

            opened = null;
            await CloseAsync().ConfigureAwait(false);
            if (held is not null)
            {
                await ForgetQuietlyAsync(held).ConfigureAwait(false);
            }

            return DeleteFiles();
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            opened = null;
            await CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
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

    private static LocalDataResult InMemoryWithoutKeyStore()
    {
        InMemorySeenMessageIds seen = new();
        return LocalDataResult.Opened(new LocalDataStores(
            new InMemoryIdentityStore(),
            new InMemoryContactStore(),
            new InMemoryChatStore(seen),
            new InMemoryGroupStore(seen),
            seen,
            new InMemoryCopiedChatStore(),
            LocalDataMode.MemoryOnlyNoKeyStore));
    }

    private static async Task<WrappedDataKey?> ReadKeyQuietlyAsync(MessengerDatabase open, CancellationToken cancellationToken)
    {
        try
        {
            return await open.ReadDataKeyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            return null;
        }
    }

    private static async Task EraseQuietlyAsync(MessengerDatabase open, CancellationToken cancellationToken)
    {
        try
        {
            await open.EraseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            return;
        }
    }

    private static void ClearShare(byte[]? value)
    {
        if (value is not null)
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }

    private Task<PassphraseUnwrapOutcome> UnwrapAsync(WrappedDataKey key, string passphrase, byte[] dataKey, CancellationToken cancellationToken)
    {
        byte[] held = [];
        if (PassphraseKeyWrap.TryGetKeyStoreShare(key, out _, out _))
        {
            if (share is null)
            {
                return Task.FromResult(PassphraseUnwrapOutcome.Damaged);
            }

            held = share;
        }

        return Task.Run(() => PassphraseKeyWrap.TryUnwrap(key, passphrase, held, dataKey), cancellationToken);
    }

    private Task<WrappedDataKey> WrapAsync(byte[] dataKey, string passphrase, CancellationToken cancellationToken) =>
        Task.Run(() => PassphraseKeyWrap.Wrap(dataKey, passphrase, kdf), cancellationToken);

    private async Task<(WrappedDataKey? Wrapped, byte[]? Share, KeyStoreOutcome Outcome)> WrapWithShareAsync(byte[] dataKey, string passphrase, CancellationToken cancellationToken)
    {
        if (keyStore is null)
        {
            return (await WrapAsync(dataKey, passphrase, cancellationToken).ConfigureAwait(false), null, KeyStoreOutcome.Succeeded);
        }

        byte[] newShare = RandomNumberGenerator.GetBytes(PassphraseKeyWrap.ShareLength);
        ProtectResult protectedShare = await keyStore.ProtectAsync(newShare, cancellationToken).ConfigureAwait(false);
        if (!protectedShare.Succeeded)
        {
            ClearShare(newShare);
            return (null, null, protectedShare.Outcome);
        }

        DataKeyProtection code = StorageCode(keyStore.Protection);
        WrappedDataKey sealedKey = await Task.Run(
            () => PassphraseKeyWrap.Wrap(dataKey, passphrase, kdf, code, newShare, protectedShare.ProtectedKey.Span),
            cancellationToken).ConfigureAwait(false);
        return (sealedKey, newShare, KeyStoreOutcome.Succeeded);
    }

    private Task<WrappedDataKey> RewrapAsync(WrappedDataKey current, byte[] dataKey, string passphrase, CancellationToken cancellationToken)
    {
        if (!PassphraseKeyWrap.TryGetKeyStoreShare(current, out DataKeyProtection code, out ReadOnlyMemory<byte> protectedShare) || share is not { } held)
        {
            return WrapAsync(dataKey, passphrase, cancellationToken);
        }

        return Task.Run(() => PassphraseKeyWrap.Wrap(dataKey, passphrase, kdf, code, held, protectedShare.Span), cancellationToken);
    }

    private void SetShare(byte[]? value)
    {
        ClearShare(share);
        share = value;
    }

    private async Task<LocalDataResult> GuardAsync(Func<CancellationToken, Task<LocalDataResult>> step, CancellationToken cancellationToken)
    {
        LocalDataResult result;
        try
        {
            result = await step(cancellationToken).ConfigureAwait(false);
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
        else if (result.Status == LocalDataStatus.Failed)
        {
            await CloseAsync().ConfigureAwait(false);
        }

        return result;
    }

    private async Task<LocalDataResult> OpenOnceAsync(CancellationToken cancellationToken)
    {
        if (database is null)
        {
            if (!File.Exists(DatabasePath))
            {
                return LocalDataResult.Waiting(LocalDataStatus.NotCreated);
            }

            DatabaseOpenResult result = await MessengerDatabase.OpenAsync(DatabasePath, cancellationToken).ConfigureAwait(false);
            if (!result.IsOpened)
            {
                return LocalDataResult.Failed(result.Outcome == DatabaseOpenOutcome.NewerThanApp ? LocalDataProblem.NewerVersion : LocalDataProblem.Damaged);
            }

            database = result.Database;
        }

        try
        {
            WrappedDataKey? stored = await database.ReadDataKeyAsync(cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
                return await database.HoldsDataAsync(cancellationToken).ConfigureAwait(false)
                    ? LocalDataResult.Failed(LocalDataProblem.Damaged)
                    : LocalDataResult.Waiting(LocalDataStatus.NotCreated);
            }

            if (stored.Protection == DataKeyProtection.Passphrase)
            {
                if (PassphraseKeyWrap.TryGetKeyStoreShare(stored, out DataKeyProtection holder, out ReadOnlyMemory<byte> protectedShare))
                {
                    if (keyStore is null || holder != StorageCode(keyStore.Protection))
                    {
                        return LocalDataResult.Failed(LocalDataProblem.KeyNotFound);
                    }

                    byte[] heldShare = new byte[PassphraseKeyWrap.ShareLength];
                    KeyStoreOutcome shareOutcome = await keyStore.UnprotectAsync(protectedShare, heldShare, cancellationToken).ConfigureAwait(false);
                    if (shareOutcome != KeyStoreOutcome.Succeeded)
                    {
                        ClearShare(heldShare);
                        return LocalDataResult.Failed(ProblemOf(shareOutcome));
                    }

                    SetShare(heldShare);
                }

                wrapped = stored;
                return LocalDataResult.Waiting(LocalDataStatus.PassphraseNeeded);
            }

            if (keyStore is null || stored.Protection != StorageCode(keyStore.Protection))
            {
                return LocalDataResult.Failed(LocalDataProblem.KeyNotFound);
            }

            byte[] dataKey = new byte[RecordKeys.DataKeyLength];
            try
            {
                KeyStoreOutcome outcome = await keyStore.UnprotectAsync(stored.WrappedKey, dataKey, cancellationToken).ConfigureAwait(false);
                if (outcome != KeyStoreOutcome.Succeeded)
                {
                    return LocalDataResult.Failed(ProblemOf(outcome));
                }

                wrapped = stored;
                return await LoadAsync(dataKey, LocalDataProtection.KeyStore, cancellationToken).ConfigureAwait(false);
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

    private async Task<LocalDataResult> CreateOnceAsync(string? passphrase, CancellationToken cancellationToken)
    {
        if (passphrase is not null && PassphrasePolicy.Check(passphrase) != PassphraseProblem.None)
        {
            return LocalDataResult.Waiting(LocalDataStatus.PassphraseNotAccepted);
        }

        if (passphrase is null && keyStore is null)
        {
            await CloseAsync().ConfigureAwait(false);
            return InMemoryWithoutKeyStore();
        }

        byte[] dataKey = RandomNumberGenerator.GetBytes(RecordKeys.DataKeyLength);
        try
        {
            WrappedDataKey created;
            byte[]? newShare = null;
            if (passphrase is not null)
            {
                (WrappedDataKey? withShare, newShare, KeyStoreOutcome shareOutcome) = await WrapWithShareAsync(dataKey, passphrase, cancellationToken).ConfigureAwait(false);
                if (withShare is not null)
                {
                    created = withShare;
                }
                else if (shareOutcome == KeyStoreOutcome.Unavailable)
                {
                    created = await WrapAsync(dataKey, passphrase, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    return LocalDataResult.Failed(ProblemOf(shareOutcome));
                }
            }
            else
            {
                ProtectResult protectedKey = await keyStore!.ProtectAsync(dataKey, cancellationToken).ConfigureAwait(false);
                if (protectedKey.Outcome == KeyStoreOutcome.Unavailable)
                {
                    return LocalDataResult.Waiting(LocalDataStatus.PassphraseRequired);
                }

                if (!protectedKey.Succeeded)
                {
                    return LocalDataResult.Failed(ProblemOf(protectedKey.Outcome));
                }

                created = new WrappedDataKey(StorageCode(keyStore.Protection), protectedKey.ProtectedKey.Span);
            }

            bool saved = false;
            try
            {
                if (await SaveNewKeyAsync(created, cancellationToken).ConfigureAwait(false) is { } failed)
                {
                    return failed;
                }

                saved = true;
            }
            finally
            {
                if (!saved)
                {
                    ClearShare(newShare);
                    await ForgetQuietlyAsync(created).ConfigureAwait(false);
                }
            }

            SetShare(newShare);
            wrapped = created;
            return await LoadAsync(dataKey, passphrase is null ? LocalDataProtection.KeyStore : LocalDataProtection.Passphrase, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private async Task<LocalDataResult?> SaveNewKeyAsync(WrappedDataKey created, CancellationToken cancellationToken)
    {
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

        await database.SaveDataKeyAsync(created, cancellationToken).ConfigureAwait(false);
        return null;
    }

    private async Task<LocalDataResult> UnlockOnceAsync(string passphrase, CancellationToken cancellationToken)
    {
        WrappedDataKey key = wrapped ?? throw new InvalidOperationException("No data key was read.");
        byte[] dataKey = new byte[RecordKeys.DataKeyLength];
        try
        {
            return await UnwrapAsync(key, passphrase, dataKey, cancellationToken).ConfigureAwait(false) switch
            {
                PassphraseUnwrapOutcome.Succeeded => await LoadAsync(dataKey, LocalDataProtection.Passphrase, cancellationToken).ConfigureAwait(false),
                PassphraseUnwrapOutcome.WrongPassphrase => LocalDataResult.Waiting(LocalDataStatus.WrongPassphrase),
                _ => LocalDataResult.Failed(LocalDataProblem.Damaged),
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private async Task<LocalDataResult> LoadAsync(byte[] dataKey, LocalDataProtection protectedBy, CancellationToken cancellationToken)
    {
        MessengerDatabase unlocked = database ?? throw new InvalidOperationException("The database is not open.");
        data = unlocked.Unlock(dataKey);

        StoredIdentity? identity = null;
        try
        {
            identity = await data.Identity.LoadAsync(cancellationToken).ConfigureAwait(false);
            await data.Contacts.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            await data.Messages.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            await data.Messages.LoadSentSequencesAsync(cancellationToken).ConfigureAwait(false);
            foreach (StoredGroup group in await data.Groups.LoadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                group.Key.Dispose();
            }

            await data.GroupMessages.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            await data.GroupMessages.LoadDeletedRangesAsync(cancellationToken).ConfigureAwait(false);
            await data.GroupMessages.LoadAcknowledgementsAsync(cancellationToken).ConfigureAwait(false);
            await data.CopiedConversations.LoadAllAsync(cancellationToken).ConfigureAwait(false);
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

        Volatile.Write(ref protection, (int)protectedBy);
        OwnIdentity? own = identity is null ? null : new OwnIdentity(identity.Keys, identity.Name);
        return LocalDataResult.Opened(new LocalDataStores(
            new DatabaseIdentityStore(data.Identity, own),
            new DatabaseContactStore(data.Contacts),
            new DatabaseChatStore(data.Messages),
            new DatabaseGroupStore(data.Groups, data.GroupMessages),
            data.SeenMessages,
            new DatabaseCopiedChatStore(data.CopiedConversations),
            LocalDataMode.Saved));
    }

    private async Task ReplaceAsync(WrappedDataKey replacement, LocalDataProtection protectedBy, CancellationToken cancellationToken)
    {
        MessengerDatabase open = database ?? throw new InvalidOperationException("The database is not open.");
        await open.ReplaceDataKeyAsync(replacement, cancellationToken).ConfigureAwait(false);
        wrapped = replacement;
        Volatile.Write(ref protection, (int)protectedBy);
    }

    private async Task ForgetQuietlyAsync(WrappedDataKey key)
    {
        if (keyStore is null)
        {
            return;
        }

        ReadOnlyMemory<byte> held;
        if (key.Protection != DataKeyProtection.Passphrase)
        {
            held = key.WrappedKey;
        }
        else if (!PassphraseKeyWrap.TryGetKeyStoreShare(key, out _, out held))
        {
            return;
        }

        try
        {
            await keyStore.ForgetAsync(held, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (Failures.IsRecoverable(exception))
        {
            return;
        }
    }

    private bool DeleteFiles()
    {
        bool deleted = true;
        foreach (string suffix in FileSuffixes)
        {
            try
            {
                File.Delete(DatabasePath + suffix);
            }
            catch (DirectoryNotFoundException)
            {
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                deleted = false;
            }
        }

        return deleted && !FileSuffixes.Any(suffix => File.Exists(DatabasePath + suffix));
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
        wrapped = null;
        SetShare(null);
        Volatile.Write(ref protection, (int)LocalDataProtection.None);
        if (database is not null)
        {
            await database.DisposeAsync().ConfigureAwait(false);
            database = null;
        }
    }
}
