using P2PMessenger.Application;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.LocalData;

namespace P2PMessenger.UI.Services;

public sealed class AppServices : IAsyncDisposable
{
    private readonly ILocalData localData;
    private readonly SemaphoreSlim starting = new(1, 1);
    private MessengerSetup setup;
    private IdentityService? identities;
    private OwnIdentity? identity;

    public AppServices(
        ILocalData localData,
        MessengerSetup setup,
        IUiThread uiThread,
        IClipboardService clipboard,
        ICodeFileService codeFiles)
    {
        ArgumentNullException.ThrowIfNull(localData);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(codeFiles);

        this.localData = localData;
        this.setup = setup;
        UiThread = uiThread;
        Clipboard = clipboard;
        CodeFiles = codeFiles;
    }

    public IdentityService Identities => identities ?? throw new InvalidOperationException("The local data has not been opened yet.");

    public IUiThread UiThread { get; }

    public IClipboardService Clipboard { get; }

    public ICodeFileService CodeFiles { get; }

    public TimeProvider TimeProvider => setup.TimeProvider;

    public Messenger? Messenger { get; private set; }

    public event EventHandler? MessengerStarted;

    public async Task<LocalDataResult> OpenLocalDataAsync(CancellationToken cancellationToken)
    {
        LocalDataResult result = await localData.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsOpen && identities is null)
        {
            LocalDataStores stores = result.Stores;
            setup = setup with
            {
                ContactStore = stores.Contacts,
                ChatStore = stores.Chats,
                SeenMessageIds = stores.SeenMessageIds,
            };
            identities = new IdentityService(stores.Identity);
        }

        return result;
    }

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

            started = await Messenger.StartAsync(owner, setup, cancellationToken).ConfigureAwait(false);
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

    public async ValueTask DisposeAsync()
    {
        if (Messenger is { } running)
        {
            await running.DisposeAsync().ConfigureAwait(false);
        }

        identity?.Dispose();
        starting.Dispose();
        await localData.DisposeAsync().ConfigureAwait(false);
    }
}
