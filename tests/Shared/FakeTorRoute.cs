using P2PMessenger.Application;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Tests.Shared;

internal sealed class FakeTorRoute : IAsyncDisposable
{
    private readonly List<OnionListener> listeners = [];

    public FakeSocksProxy Proxy { get; } = new();

    public void Join(Messenger messenger)
    {
        OnionListener listener = messenger.Connections.StartOnionListener(null);
        listeners.Add(listener);
        Proxy.Route(messenger.Identity.Keys.OnionAddress, listener.Target);
        messenger.Connections.UseTorProxy(Proxy.Endpoint);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (OnionListener listener in listeners)
        {
            await listener.DisposeAsync();
        }

        await Proxy.DisposeAsync();
    }
}
