using P2PMessenger.Application;
using P2PMessenger.Application.Identity;

namespace P2PMessenger.UI.Services;

public sealed class AppServices : IAsyncDisposable
{
    private readonly MessengerSetup setup;
    private readonly SemaphoreSlim starting = new(1, 1);
    private OwnIdentity? identity;

    public AppServices(
        IdentityService identities,
        MessengerSetup setup,
        IUiThread uiThread,
        IClipboardService clipboard,
        ICodeFileService codeFiles)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(codeFiles);

        Identities = identities;
        this.setup = setup;
        UiThread = uiThread;
        Clipboard = clipboard;
        CodeFiles = codeFiles;
    }

    public IdentityService Identities { get; }

    public IUiThread UiThread { get; }

    public IClipboardService Clipboard { get; }

    public ICodeFileService CodeFiles { get; }

    public TimeProvider TimeProvider => setup.TimeProvider;

    public Messenger? Messenger { get; private set; }

    public event EventHandler? MessengerStarted;

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
    }
}
