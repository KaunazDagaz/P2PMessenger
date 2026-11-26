using Microsoft.Extensions.Logging;
using P2PMessenger.Application;
using P2PMessenger.Application.Background;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.Internet;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Application.Locking;

namespace P2PMessenger.UI.Services;

public sealed class AppServices : IAsyncDisposable
{
    private readonly ILocalData localData;
    private readonly SemaphoreSlim starting = new(1, 1);
    private readonly MessengerSetup initialSetup;
    private MessengerSetup setup;
    private IdentityService? identities;
    private OwnIdentity? identity;
    private int disposed;

    public AppServices(
        ILocalData localData,
        MessengerSetup setup,
        IUiThread uiThread,
        IClipboardService clipboard,
        ICodeFileService codeFiles)
        : this(localData, setup, uiThread, clipboard, codeFiles, NoQrScanner.Instance)
    {
    }

    public AppServices(
        ILocalData localData,
        MessengerSetup setup,
        IUiThread uiThread,
        IClipboardService clipboard,
        ICodeFileService codeFiles,
        IQrScanner qrScanner)
    {
        ArgumentNullException.ThrowIfNull(localData);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(codeFiles);
        ArgumentNullException.ThrowIfNull(qrScanner);

        this.localData = localData;
        initialSetup = setup;
        this.setup = setup;
        UiThread = uiThread;
        Clipboard = clipboard;
        CodeFiles = codeFiles;
        QrScanner = qrScanner;
        FailureLog = new FailureLog(setup.Loggers.CreateLogger<FailureLog>());
        Lock = new AppLock(localData, setup.TimeProvider);
    }

    public event EventHandler<LocalDataStores>? StoresOpened;

    public event EventHandler? ProtectionChanged;

    public event EventHandler? MessengerStarted;

    public event EventHandler? MessengerStopped;

    public event EventHandler<bool>? PassphraseSetChanged;

    public IdentityService Identities => identities ?? throw new InvalidOperationException("The local data has not been opened yet.");

    public bool HasStores => identities is not null;

    public IUiThread UiThread { get; }

    public IClipboardService Clipboard { get; }

    public ICodeFileService CodeFiles { get; }

    public IQrScanner QrScanner { get; }

    public TimeProvider TimeProvider => setup.TimeProvider;

    public AppLock Lock { get; }

    public bool SupportsPassphrase => localData.SupportsPassphrase;

    public bool HasKeyStore => localData.HasKeyStore;

    public LocalDataProtection Protection => localData.Protection;

    public LocalDataMode? Mode { get; private set; }

    public LocalDataStatus? OpenStatus { get; private set; }

    public BackgroundPorts? Background { get; init; }

    public InternetRouteSettings TorSettings { get; init; } = new(new InMemorySettingsStore());

    public bool OffersTor => initialSetup.Tor is { IsInBuild: true };

    public Messenger? Messenger { get; private set; }

    public FailureLog FailureLog { get; }
    public async Task<LocalDataResult> OpenLocalDataAsync(CancellationToken cancellationToken) =>
        Adopt(await localData.OpenAsync(cancellationToken).ConfigureAwait(false));

    public async Task<LocalDataResult> CreateLocalDataAsync(string? passphrase, CancellationToken cancellationToken) =>
        Adopt(await localData.CreateAsync(passphrase, cancellationToken).ConfigureAwait(false));

    public async Task<LocalDataResult> UnlockLocalDataAsync(string passphrase, CancellationToken cancellationToken) =>
        Adopt(await localData.UnlockAsync(passphrase, cancellationToken).ConfigureAwait(false));

    public Task<PassphraseChangeResult> SetPassphraseAsync(string passphrase, CancellationToken cancellationToken) =>
        Changing(localData.SetPassphraseAsync(passphrase, cancellationToken));

    public Task<PassphraseChangeResult> ChangePassphraseAsync(string currentPassphrase, string newPassphrase, CancellationToken cancellationToken) =>
        Changing(localData.ChangePassphraseAsync(currentPassphrase, newPassphrase, cancellationToken));

    public Task<PassphraseChangeResult> RemovePassphraseAsync(string currentPassphrase, CancellationToken cancellationToken) =>
        Changing(localData.RemovePassphraseAsync(currentPassphrase, cancellationToken));

    public async Task<Messenger> StartMessengerAsync(OwnIdentity owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);

        Messenger started;
        await starting.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Messenger is { } running)
            {
                return running;
            }

            MessengerSetup chosen = setup.Tor is { } tor ? setup with { Tor = tor with { Enabled = TorSettings.UseTor } } : setup;
            started = await Messenger.StartAsync(owner, chosen, cancellationToken).ConfigureAwait(false);
            identity = owner;
            Messenger = started;
        }
        finally
        {
            starting.Release();
        }

        MessengerStarted?.Invoke(this, EventArgs.Empty);
        return started;
    }

    public async Task<bool> DeleteAllDataAsync(CancellationToken cancellationToken)
    {
        bool deleted;
        bool stopped = false;
        await starting.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Messenger is { } running)
            {
                Messenger = null;
                stopped = true;
                await running.DisposeAsync().ConfigureAwait(false);
            }

            identity?.Dispose();
            identity = null;
            deleted = await localData.DeleteAllAsync(cancellationToken).ConfigureAwait(false);
            deleted &= initialSetup.Tor?.DeleteData() ?? true;
            TorSettings.Reset();
            identities = null;
            Mode = null;
            OpenStatus = null;
            setup = initialSetup;
            Lock.Reset();
        }
        finally
        {
            starting.Release();
        }

        if (stopped)
        {
            MessengerStopped?.Invoke(this, EventArgs.Empty);
        }

        ProtectionChanged?.Invoke(this, EventArgs.Empty);
        PassphraseSetChanged?.Invoke(this, false);
        return deleted;
    }

    public async Task SetTorAsync(bool on, CancellationToken cancellationToken)
    {
        TorSettings.UseTor = on;
        if (Messenger is { } running)
        {
            await running.InternetRoute.SetEnabledAsync(on, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task QuitAsync()
    {
        Background?.Service.StopRunning();
        try
        {
            await DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            Background?.Service.EndProcess();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Lock.Dispose();
        if (Messenger is { } running)
        {
            await running.DisposeAsync().ConfigureAwait(false);
        }

        identity?.Dispose();
        starting.Dispose();
        await localData.DisposeAsync().ConfigureAwait(false);
    }

    private LocalDataResult Adopt(LocalDataResult result)
    {
        OpenStatus = result.Status;
        if (!result.IsOpen || identities is not null)
        {
            return result;
        }

        LocalDataStores stores = result.Stores;
        setup = setup with
        {
            ContactStore = stores.Contacts,
            ChatStore = stores.Chats,
            GroupStore = stores.Groups,
            SeenMessageIds = stores.SeenMessageIds,
        };
        identities = new IdentityService(stores.Identity);
        Mode = stores.Mode;
        StoresOpened?.Invoke(this, stores);
        PassphraseSetChanged?.Invoke(this, Protection == LocalDataProtection.Passphrase);
        return result;
    }

    private async Task<PassphraseChangeResult> Changing(Task<PassphraseChangeResult> change)
    {
        PassphraseChangeResult result = await change.ConfigureAwait(false);
        if (result.IsChanged)
        {
            ProtectionChanged?.Invoke(this, EventArgs.Empty);
            PassphraseSetChanged?.Invoke(this, Protection == LocalDataProtection.Passphrase);
        }

        return result;
    }
}
