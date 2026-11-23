using System.Net;
using P2PMessenger.Tests.Shared;
using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Application.Tests;

internal static class TestMessengers
{
    public static ConnectionOptions Fast { get; } = new()
    {
        DialTimeout = TimeSpan.FromSeconds(5),
        HandshakeTimeout = TimeSpan.FromSeconds(5),
        ReconnectDelayMin = TimeSpan.FromMilliseconds(50),
        ReconnectDelayMax = TimeSpan.FromMilliseconds(400),
    };

    public static Task<Messenger> StartAsync(string name, CancellationToken cancellationToken) =>
        Messenger.StartAsync(
            TestKeys.NewIdentity(name),
            MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback, ListenPort = 0, Connection = Fast },
            cancellationToken);

    public static Task<Messenger> StartAsync(string name, LoopbackDiscovery.Node node, CancellationToken cancellationToken) =>
        Messenger.StartAsync(
            TestKeys.NewIdentity(name),
            MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback, ListenPort = 0, Connection = Fast, Discovery = node.Setup },
            cancellationToken);

    public static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}
