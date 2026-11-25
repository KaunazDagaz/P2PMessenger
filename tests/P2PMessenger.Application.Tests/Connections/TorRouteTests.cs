using System.Net;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Tests.Shared;
using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Application.Tests.Connections;

[Trait("Story", "US-09")]
public sealed class TorRouteTests
{
    private static readonly ConnectionOptions Fast = TestMessengers.Fast with
    {
        TorDialTimeout = TimeSpan.FromSeconds(5),
        TorRetryDelayMin = TimeSpan.FromMilliseconds(200),
        TorRetryDelayMax = TimeSpan.FromSeconds(1),
    };

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-07")]
    [Trait("Story", "US-02")]
    public async Task ContactsOnDifferentNetworks_AddEachOtherOverTorAndChat()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await StartAsync("Alice", null, Fast, cancellationToken);
        await using var bob = await StartAsync("Bob", null, Fast, cancellationToken);
        await using var tor = new FakeTorRoute();
        tor.Join(alice);
        tor.Join(bob);
        var aliceKey = alice.Identity.Keys.IdentityKey;
        var bobKey = bob.Identity.Keys.IdentityKey;

        await ExchangeCodesAsync(alice, bob, cancellationToken);
        await TestMessengers.WaitUntilAsync(
            () => alice.Contacts.All.Single().IsAdded && bob.Contacts.All.Single().IsAdded
                && alice.Connections.ReachabilityOf(bobKey) == Reachability.Tor && bob.Connections.ReachabilityOf(aliceKey) == Reachability.Tor,
            cancellationToken);
        await alice.SendAsync(bobKey, "Over the internet route", cancellationToken);
        await TestMessengers.WaitUntilAsync(
            () => bob.Chats.MessagesWith(aliceKey).Any(static message => message.Text == "Over the internet route")
                && alice.Chats.MessagesWith(bobKey).Single().Delivery == DeliveryState.Received,
            cancellationToken);

        await TestMessengers.WaitUntilAsync(
            () => alice.Connections.TorStatusOf(bobKey) == TorDialStatus.None && bob.Connections.TorStatusOf(aliceKey) == TorDialStatus.None,
            cancellationToken);

        Assert.True(alice.Connections.UsesTor);
        Assert.Equal(tor.Proxy.Usernames.Count, tor.Proxy.Usernames.Distinct().Count());
        alice.Identity.Dispose();
        bob.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-07")]
    public async Task ContactReachableNearby_TorComesUp_TheTorSessionReplacesTheNearbyOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        await using var alice = await StartAsync("Alice", hub.CreateNode(), Fast, cancellationToken);
        await using var bob = await StartAsync("Bob", hub.CreateNode(), Fast, cancellationToken);
        await using var tor = new FakeTorRoute();
        var aliceKey = alice.Identity.Keys.IdentityKey;
        var bobKey = bob.Identity.Keys.IdentityKey;
        await ExchangeCodesAsync(alice, bob, cancellationToken);
        await TestMessengers.WaitUntilAsync(
            () => alice.Connections.ReachabilityOf(bobKey) == Reachability.Nearby && bob.Connections.ReachabilityOf(aliceKey) == Reachability.Nearby,
            cancellationToken);

        tor.Join(alice);
        tor.Join(bob);
        await TestMessengers.WaitUntilAsync(
            () => alice.Connections.ReachabilityOf(bobKey) == Reachability.Tor && bob.Connections.ReachabilityOf(aliceKey) == Reachability.Tor,
            cancellationToken);
        await alice.SendAsync(bobKey, "Tor first", cancellationToken);
        await TestMessengers.WaitUntilAsync(() => alice.Chats.MessagesWith(bobKey).Single().Delivery == DeliveryState.Received, cancellationToken);

        Assert.True(alice.Connections.TryGetConnection(bobKey, out var connection));
        Assert.Equal(ConnectionRoute.Tor, connection.Route);
        alice.Identity.Dispose();
        bob.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("NFR", "NFR-03")]
    public async Task TorSessionLost_TheNearbyRouteCarriesTheMessages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        await using var alice = await StartAsync("Alice", hub.CreateNode(), Fast, cancellationToken);
        await using var bob = await StartAsync("Bob", hub.CreateNode(), Fast, cancellationToken);
        await using var tor = new FakeTorRoute();
        tor.Join(alice);
        tor.Join(bob);
        var aliceKey = alice.Identity.Keys.IdentityKey;
        var bobKey = bob.Identity.Keys.IdentityKey;
        await ExchangeCodesAsync(alice, bob, cancellationToken);
        await TestMessengers.WaitUntilAsync(
            () => alice.Connections.ReachabilityOf(bobKey) == Reachability.Tor && bob.Connections.ReachabilityOf(aliceKey) == Reachability.Tor,
            cancellationToken);

        tor.Proxy.Answer(alice.Identity.Keys.OnionAddress, 0xF2);
        tor.Proxy.Answer(bob.Identity.Keys.OnionAddress, 0xF2);
        tor.Proxy.CutAll();
        await TestMessengers.WaitUntilAsync(
            () => alice.Connections.ReachabilityOf(bobKey) == Reachability.Nearby && bob.Connections.ReachabilityOf(aliceKey) == Reachability.Nearby,
            cancellationToken);
        await alice.SendAsync(bobKey, "Still here, nearby", cancellationToken);
        await TestMessengers.WaitUntilAsync(() => alice.Chats.MessagesWith(bobKey).Single().Delivery == DeliveryState.Received, cancellationToken);
        await TestMessengers.WaitUntilAsync(() => alice.Connections.TorStatusOf(bobKey) == TorDialStatus.NoAnswer, cancellationToken);

        Assert.Equal(Reachability.Nearby, alice.Connections.ReachabilityOf(bobKey));
        alice.Identity.Dispose();
        bob.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-02")]
    [Trait("NFR", "NFR-15")]
    public async Task TorAttemptsHang_TheFirstConnectionGoesOverTheNearbyRoute()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        var slowTor = Fast with { TorDialTimeout = TimeSpan.FromSeconds(30) };
        await using var alice = await StartAsync("Alice", hub.CreateNode(), slowTor, cancellationToken);
        await using var bob = await StartAsync("Bob", hub.CreateNode(), slowTor, cancellationToken);
        await using var tor = new FakeTorRoute();
        tor.Proxy.Silent = true;
        tor.Join(alice);
        tor.Join(bob);
        var aliceKey = alice.Identity.Keys.IdentityKey;
        var bobKey = bob.Identity.Keys.IdentityKey;
        var started = TimeProvider.System.GetTimestamp();

        await ExchangeCodesAsync(alice, bob, cancellationToken);
        await TestMessengers.WaitUntilAsync(
            () => alice.Contacts.All.Single().IsAdded && bob.Contacts.All.Single().IsAdded
                && alice.Connections.ReachabilityOf(bobKey) == Reachability.Nearby && bob.Connections.ReachabilityOf(aliceKey) == Reachability.Nearby,
            cancellationToken);

        Assert.True(TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(10));
        Assert.Equal(TorDialStatus.Connecting, alice.Connections.TorStatusOf(bobKey));
        alice.Identity.Dispose();
        bob.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    public async Task ContactWithoutAnOnionService_IsTriedOnANewCircuitAtOnceAndThenAfterADelay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var slowRetry = Fast with { TorRetryDelayMin = TimeSpan.FromSeconds(2), TorRetryDelayMax = TimeSpan.FromSeconds(2) };
        await using var alice = await StartAsync("Alice", null, slowRetry, cancellationToken);
        await using var tor = new FakeTorRoute();
        tor.Join(alice);
        using var bob = TestKeys.NewIdentity("Bob");
        Assert.True((await alice.Contacts.AddAsync(alice.Contacts.Check(bob.CodeText), cancellationToken)).IsSaved);

        await TestMessengers.WaitUntilAsync(() => tor.Proxy.ConnectsTo(bob.Keys.OnionAddress) >= 1, cancellationToken);
        var first = TimeProvider.System.GetTimestamp();
        await TestMessengers.WaitUntilAsync(() => tor.Proxy.ConnectsTo(bob.Keys.OnionAddress) == 2, cancellationToken);
        Assert.True(TimeProvider.System.GetElapsedTime(first) < TimeSpan.FromSeconds(1), "The retry on a new circuit did not follow at once.");
        await TestMessengers.WaitUntilAsync(() => alice.Connections.TorStatusOf(bob.Keys.IdentityKey) == TorDialStatus.NotFound, cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(800), cancellationToken);
        Assert.Equal(2, tor.Proxy.ConnectsTo(bob.Keys.OnionAddress));
        await TestMessengers.WaitUntilAsync(() => tor.Proxy.ConnectsTo(bob.Keys.OnionAddress) == 4, cancellationToken);

        Assert.Equal(tor.Proxy.Usernames.Count, tor.Proxy.Usernames.Distinct().Count());
        alice.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-10")]
    public async Task TorRouteGone_StopsTheAttempts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await StartAsync("Alice", null, Fast, cancellationToken);
        await using var tor = new FakeTorRoute();
        tor.Join(alice);
        using var bob = TestKeys.NewIdentity("Bob");
        Assert.True((await alice.Contacts.AddAsync(alice.Contacts.Check(bob.CodeText), cancellationToken)).IsSaved);
        await TestMessengers.WaitUntilAsync(() => tor.Proxy.ConnectsTo(bob.Keys.OnionAddress) >= 2, cancellationToken);

        alice.Connections.UseTorProxy(null);
        var attempts = tor.Proxy.ConnectsTo(bob.Keys.OnionAddress);
        await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);

        Assert.False(alice.Connections.UsesTor);
        Assert.Equal(TorDialStatus.None, alice.Connections.TorStatusOf(bob.Keys.IdentityKey));
        Assert.InRange(tor.Proxy.ConnectsTo(bob.Keys.OnionAddress), attempts, attempts + 1);
        Assert.Equal(Reachability.NotReachable, alice.Connections.ReachabilityOf(bob.Keys.IdentityKey));
        alice.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    public async Task ContactWithNewKeys_IsDialledAtItsNewOnionAddress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await StartAsync("Alice", null, Fast, cancellationToken);
        await using var tor = new FakeTorRoute();
        tor.Join(alice);
        using var bob = TestKeys.NewIdentity("Bob");
        using var bobWithNewOnionKey = TestKeys.WithNewOnionKey(bob.Keys);
        Assert.True((await alice.Contacts.AddAsync(alice.Contacts.Check(bob.CodeText), cancellationToken)).IsSaved);
        await TestMessengers.WaitUntilAsync(() => tor.Proxy.ConnectsTo(bob.Keys.OnionAddress) >= 1, cancellationToken);

        var check = alice.Contacts.Check(TestKeys.Code(bobWithNewOnionKey, "Bob").ToText());
        Assert.True((await alice.Contacts.AddAsync(check, cancellationToken)).IsSaved);

        await TestMessengers.WaitUntilAsync(() => tor.Proxy.ConnectsTo(bobWithNewOnionKey.OnionAddress) >= 1, cancellationToken);
        alice.Identity.Dispose();
    }

    private static Task<Messenger> StartAsync(string name, LoopbackDiscovery.Node? node, ConnectionOptions options, CancellationToken cancellationToken) =>
        Messenger.StartAsync(
            TestKeys.NewIdentity(name),
            MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback, ListenPort = 0, Connection = options, Discovery = node?.Setup },
            cancellationToken);

    private static async Task ExchangeCodesAsync(Messenger alice, Messenger bob, CancellationToken cancellationToken)
    {
        Assert.True((await alice.Contacts.AddAsync(alice.Contacts.Check(bob.Identity.CodeText), cancellationToken)).IsSaved);
        Assert.True((await bob.Contacts.AddAsync(bob.Contacts.Check(alice.Identity.CodeText), cancellationToken)).IsSaved);
    }
}
