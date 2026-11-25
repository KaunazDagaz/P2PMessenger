using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using P2PMessenger.Tests.Shared;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Integration.Tests.Connections;

[Trait("Story", "US-09")]
public sealed class RouteTests
{
    [Fact(Timeout = 30_000)]
    public async Task TorSession_ReplacesTheNearbySessionOnBothSides()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);
        await using var onion = OnionListener.Start(bob.Manager, null, NullLogger<OnionListener>.Instance);
        await using var proxy = new FakeSocksProxy();
        proxy.Route(bob.Keys.OnionAddress, onion.Target);

        var nearby = await alice.ConnectAsync(bobForAlice, bob, cancellationToken);
        await bob.Observer.NextConnectedAsync(cancellationToken);
        var tor = await alice.Manager.ConnectAsync(bobForAlice, new OnionDialer(proxy.Endpoint, bob.Keys.OnionAddress, NullLogger.Instance), cancellationToken);

        Assert.Equal(ConnectionRoute.Nearby, nearby.Connection!.Route);
        Assert.True(tor.IsConnected);
        Assert.Equal(ConnectionRoute.Tor, tor.Connection.Route);
        await nearby.Connection.Completion.WaitAsync(cancellationToken);
        Assert.Contains(nearby.Connection.CloseReason, new[] { ConnectionCloseReason.Replaced, ConnectionCloseReason.PeerClosed });
        Assert.True(await Raw.EventuallyAsync(() => RouteAt(bob, alice) == ConnectionRoute.Tor, TimeSpan.FromSeconds(5), cancellationToken));
        Assert.True(alice.Manager.TryGetConnection(bob.Keys.IdentityKey, out var current));
        Assert.Same(tor.Connection, current);
    }

    [Fact(Timeout = 30_000)]
    public async Task TorSession_ReplacesAYoungNearbySessionThatTheSessionRuleOfOneRouteWouldKeep()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var first = TestPeer.Start();
        await using var second = TestPeer.Start();
        var firstIsHigher = first.Keys.StaticKey.Bytes.SequenceCompareTo(second.Keys.StaticKey.Bytes) > 0;
        var alice = firstIsHigher ? first : second;
        var bob = firstIsHigher ? second : first;
        var bobForAlice = alice.Know(bob);
        var aliceForBob = bob.Know(alice);
        await using var onion = OnionListener.Start(bob.Manager, null, NullLogger<OnionListener>.Instance);
        await using var proxy = new FakeSocksProxy();
        proxy.Route(bob.Keys.OnionAddress, onion.Target);

        var nearby = await bob.ConnectAsync(aliceForBob, alice, cancellationToken);
        await alice.Observer.NextConnectedAsync(cancellationToken);
        var tor = await alice.Manager.ConnectAsync(bobForAlice, new OnionDialer(proxy.Endpoint, bob.Keys.OnionAddress, NullLogger.Instance), cancellationToken);

        Assert.Equal(ConnectionRoute.Nearby, nearby.Connection!.Route);
        Assert.Equal(ConnectionRoute.Tor, tor.Connection!.Route);
        Assert.True(await Raw.EventuallyAsync(() => RouteAt(alice, bob) == ConnectionRoute.Tor && RouteAt(bob, alice) == ConnectionRoute.Tor, TimeSpan.FromSeconds(5), cancellationToken));
    }

    [Fact(Timeout = 30_000)]
    public async Task NearbyConnect_WhileATorSessionCounts_GivesTheTorSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);
        await using var onion = OnionListener.Start(bob.Manager, null, NullLogger<OnionListener>.Instance);
        await using var proxy = new FakeSocksProxy();
        proxy.Route(bob.Keys.OnionAddress, onion.Target);
        var tor = await alice.Manager.ConnectAsync(bobForAlice, new OnionDialer(proxy.Endpoint, bob.Keys.OnionAddress, NullLogger.Instance), cancellationToken);
        var dialer = new CountingDialer(bob.Dialer);

        var nearby = await alice.Manager.ConnectAsync(bobForAlice, dialer, cancellationToken);

        Assert.Same(tor.Connection, nearby.Connection);
        Assert.Equal(0, dialer.Calls);
    }

    [Fact(Timeout = 30_000)]
    public async Task NearbyDial_WhileATorDialHangs_IsNotHeldUp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start(TestOptions.Fast with { TorDialTimeout = TimeSpan.FromSeconds(20) });
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);
        await using var proxy = new FakeSocksProxy { Silent = true };
        var torDial = alice.Manager.ConnectAsync(bobForAlice, new OnionDialer(proxy.Endpoint, bob.Keys.OnionAddress, NullLogger.Instance), cancellationToken);
        Assert.True(await Raw.EventuallyAsync(() => proxy.Hosts.Count == 1, TimeSpan.FromSeconds(5), cancellationToken));
        var watch = Stopwatch.StartNew();

        var nearby = await alice.ConnectAsync(bobForAlice, bob, cancellationToken);

        Assert.True(nearby.IsConnected);
        Assert.Equal(ConnectionRoute.Nearby, nearby.Connection.Route);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"The nearby dial took {watch.Elapsed}.");
        Assert.False(torDial.IsCompleted);
    }

    [Fact(Timeout = 30_000)]
    public async Task TorDial_ProxyNeverAnswers_TimesOutAfterTheTorDialTimeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start(TestOptions.Fast with { DialTimeout = TimeSpan.FromSeconds(20), TorDialTimeout = TimeSpan.FromMilliseconds(500) });
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        await using var proxy = new FakeSocksProxy { Silent = true };
        var watch = Stopwatch.StartNew();

        var result = await alice.Manager.ConnectAsync(bobForAlice, new OnionDialer(proxy.Endpoint, bob.Keys.OnionAddress, NullLogger.Instance), cancellationToken);

        Assert.Equal(ConnectFailure.TimedOut, result.Failure);
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(10));
    }

    [Fact(Timeout = 30_000)]
    [Trait("NFR", "NFR-09")]
    public async Task OnionCallerWithoutAHint_IsClosedAfterTheTorHintTimeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start(TestOptions.Fast with { HintTimeout = TimeSpan.FromSeconds(20), TorHintTimeout = TimeSpan.FromMilliseconds(300) });
        await using var onion = OnionListener.Start(bob.Manager, null, NullLogger<OnionListener>.Instance);
        using var client = await Raw.ConnectAsync(new IPEndPoint(IPAddress.Loopback, onion.Target.Port), cancellationToken);
        var watch = Stopwatch.StartNew();

        var count = await Raw.CountBytesUntilClosedAsync(client.GetStream(), TimeSpan.FromSeconds(10), cancellationToken);

        Assert.Equal(0, count);
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5));
    }

    private static ConnectionRoute? RouteAt(TestPeer peer, TestPeer other) =>
        peer.Manager.TryGetConnection(other.Keys.IdentityKey, out var connection) ? connection.Route : null;
}
