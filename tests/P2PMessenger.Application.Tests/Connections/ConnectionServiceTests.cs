using System.Net;
using System.Net.Sockets;
using P2PMessenger.Application.Connections;
using P2PMessenger.Core.Discovery;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Tests.Shared;
using P2PMessenger.Transport.Discovery;
using static P2PMessenger.Application.Tests.TestKeys;

namespace P2PMessenger.Application.Tests.Connections;

public sealed class ConnectionServiceTests
{
    [Fact]
    public async Task ListenAsync_PortTakenByAnotherProgram_ReportsPortInUseAndAnotherPortWorks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var other = new TcpListener(IPAddress.Loopback, 0);
        other.Start();
        int taken = ((IPEndPoint)other.LocalEndpoint).Port;

        var inUse = await messenger.Connections.ListenAsync(IPAddress.Loopback, taken, cancellationToken);

        Assert.Equal(ListenResult.PortInUse, inUse);
        Assert.Equal(ListenResult.PortInUse, messenger.Connections.LastListenResult);
        Assert.Null(messenger.Connections.ListeningPort);

        var free = await messenger.Connections.ListenAsync(IPAddress.Loopback, 0, cancellationToken);

        Assert.Equal(ListenResult.Listening, free);
        Assert.NotNull(messenger.Connections.ListeningPort);
        Assert.NotEqual(taken, messenger.Connections.ListeningPort);
        messenger.Identity.Dispose();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task ListenAsync_PortOutOfRange_IsRefused(int port)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        int? before = messenger.Connections.ListeningPort;

        var result = await messenger.Connections.ListenAsync(IPAddress.Loopback, port, cancellationToken);

        Assert.Equal(ListenResult.InvalidPort, result);
        Assert.Equal(before, messenger.Connections.ListeningPort);
        messenger.Identity.Dispose();
    }

    [Fact]
    public async Task ListenAsync_WithoutAPort_ListensOnARandomPortOfTheDynamicRange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        var ports = new HashSet<int>();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(ListenResult.Listening, await messenger.Connections.ListenAsync(IPAddress.Loopback, null, cancellationToken));
            var port = Assert.IsType<int>(messenger.Connections.ListeningPort);
            Assert.InRange(port, ConnectionService.RandomPortMin, ConnectionService.RandomPortMax);
            ports.Add(port);
        }

        Assert.True(ports.Count > 1, "Five listens all chose the same port.");
        messenger.Identity.Dispose();
    }

    [Fact]
    public async Task StartAsync_WithoutAPort_ListensOnARandomPortOfTheDynamicRange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var identity = NewIdentity("Alice");

        await using var messenger = await Messenger.StartAsync(identity, MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback }, cancellationToken);

        Assert.InRange(Assert.IsType<int>(messenger.Connections.ListeningPort), ConnectionService.RandomPortMin, ConnectionService.RandomPortMax);
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-06")]
    [Trait("Story", "US-07")]
    public async Task Discovery_TwoContactsOnOneNetwork_FindEachOtherWithoutAnAddressAndAreReachableNearby()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        await using var alice = await TestMessengers.StartAsync("Alice", hub.CreateNode(), cancellationToken);
        await using var bob = await TestMessengers.StartAsync("Bob", hub.CreateNode(), cancellationToken);
        var aliceKey = alice.Identity.Keys.IdentityKey;
        var bobKey = bob.Identity.Keys.IdentityKey;
        Assert.Equal(Reachability.NotReachable, alice.Connections.ReachabilityOf(bobKey));

        Assert.True((await alice.Contacts.AddAsync(alice.Contacts.Check(bob.Identity.CodeText), cancellationToken)).IsSaved);
        Assert.True((await bob.Contacts.AddAsync(bob.Contacts.Check(alice.Identity.CodeText), cancellationToken)).IsSaved);

        await TestMessengers.WaitUntilAsync(
            () => alice.Connections.ReachabilityOf(bobKey) == Reachability.Nearby && bob.Connections.ReachabilityOf(aliceKey) == Reachability.Nearby
                && alice.Contacts.All.Single().IsAdded && bob.Contacts.All.Single().IsAdded
                && alice.Connections.IsHeard(bobKey) && bob.Connections.IsHeard(aliceKey)
                && alice.Connections.DialStatusOf(bobKey) == DialStatus.None,
            cancellationToken);
        Assert.True(alice.Connections.TryGetHeardEndpoint(bobKey, out var heard));
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, bob.Connections.ListeningPort!.Value), heard);
        Assert.True(bob.Connections.IsHeard(aliceKey));
        Assert.Equal(DialStatus.None, alice.Connections.DialStatusOf(bobKey));
        alice.Identity.Dispose();
        bob.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-06")]
    [Trait("Story", "US-07")]
    public async Task Discovery_ContactWhoHasNotAddedYourCode_IsNeitherHeardNorDialed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        await using var alice = await TestMessengers.StartAsync("Alice", hub.CreateNode(), cancellationToken);
        await using var bob = await TestMessengers.StartAsync("Bob", hub.CreateNode(), cancellationToken);
        var bobKey = bob.Identity.Keys.IdentityKey;
        Assert.True((await alice.Contacts.AddAsync(alice.Contacts.Check(bob.Identity.CodeText), cancellationToken)).IsSaved);

        await TestMessengers.WaitUntilAsync(() => hub.Sent.Count >= 6, cancellationToken);
        await Task.Delay(300, cancellationToken);

        Assert.False(alice.Connections.IsHeard(bobKey));
        Assert.Equal(DialStatus.None, alice.Connections.DialStatusOf(bobKey));
        Assert.Equal(Reachability.NotReachable, alice.Connections.ReachabilityOf(bobKey));
        Assert.Empty(bob.Contacts.All);
        alice.Identity.Dispose();
        bob.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-07")]
    public async Task Heard_ButNobodyListens_ReportsUnreachableAndStopsWhenNoLongerHeard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        await using var alice = await TestMessengers.StartAsync("Alice", hub.CreateNode(), cancellationToken);
        using var bob = KeySet.Create();
        await alice.Contacts.AddAsync(alice.Contacts.Check(Code(bob, "Bob")), cancellationToken);
        int closedPort = ClosedPort();

        using (var heardUntil = new CancellationTokenSource())
        {
            var announcing = AnnounceAsync(hub, bob, alice, closedPort, heardUntil.Token);
            await TestMessengers.WaitUntilAsync(() => alice.Connections.DialStatusOf(bob.IdentityKey) == DialStatus.Unreachable, cancellationToken);
            Assert.True(alice.Connections.IsHeard(bob.IdentityKey));
            Assert.Equal(Reachability.NotReachable, alice.Connections.ReachabilityOf(bob.IdentityKey));
            await heardUntil.CancelAsync();
            await announcing;
        }

        await TestMessengers.WaitUntilAsync(
            () => !alice.Connections.IsHeard(bob.IdentityKey) && alice.Connections.DialStatusOf(bob.IdentityKey) == DialStatus.None,
            cancellationToken);
        Assert.False(alice.Connections.IsConnected(bob.IdentityKey));
        alice.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-07")]
    public async Task Heard_RetryAfterAFailure_KeepsTheLastFailureUntilTheRetryEnds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        await using var alice = await TestMessengers.StartAsync("Alice", hub.CreateNode(), cancellationToken);
        using var bob = KeySet.Create();
        await alice.Contacts.AddAsync(alice.Contacts.Check(Code(bob, "Bob")), cancellationToken);
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var silentPort = ((IPEndPoint)silent.LocalEndpoint).Port;

        using (var closedUntil = new CancellationTokenSource())
        {
            var announcingClosed = AnnounceAsync(hub, bob, alice, ClosedPort(), closedUntil.Token);
            await TestMessengers.WaitUntilAsync(() => alice.Connections.DialStatusOf(bob.IdentityKey) == DialStatus.Unreachable, cancellationToken);
            await closedUntil.CancelAsync();
            await announcingClosed;
        }

        using var silentUntil = new CancellationTokenSource();
        var announcingSilent = AnnounceAsync(hub, bob, alice, silentPort, silentUntil.Token);
        using (var accepted = await silent.AcceptTcpClientAsync(cancellationToken))
        {
            await Task.Delay(200, cancellationToken);

            Assert.Equal(DialStatus.Unreachable, alice.Connections.DialStatusOf(bob.IdentityKey));
        }

        await TestMessengers.WaitUntilAsync(() => alice.Connections.DialStatusOf(bob.IdentityKey) == DialStatus.Refused, cancellationToken);
        await silentUntil.CancelAsync();
        await announcingSilent;
        alice.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-05")]
    [Trait("Story", "US-06")]
    public async Task Heard_AnnouncementsOfStrangersAndForOthers_AreIgnored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        await using var alice = await TestMessengers.StartAsync("Alice", hub.CreateNode(), cancellationToken);
        using var bob = KeySet.Create();
        using var carol = KeySet.Create();
        using var stranger = KeySet.Create();
        await alice.Contacts.AddAsync(alice.Contacts.Check(Code(bob, "Bob")), cancellationToken);
        Assert.True(SessionContact.TryCreate(bob, carol.IdentityKey, carol.StaticKey, out var carolForBob));
        Assert.True(SessionContact.TryCreate(stranger, alice.Identity.Keys.IdentityKey, alice.Identity.Keys.StaticKey, out var aliceForStranger));
        using (carolForBob)
        using (aliceForStranger)
        {
            for (var round = 0; round < 10; round++)
            {
                hub.Inject(Announcement.Build([carolForBob], 50_000));
                hub.Inject(Announcement.Build([aliceForStranger], 50_001));
                await Task.Delay(20, cancellationToken);
            }
        }

        await Task.Delay(300, cancellationToken);

        Assert.False(alice.Connections.IsHeard(bob.IdentityKey));
        Assert.Equal(DialStatus.None, alice.Connections.DialStatusOf(bob.IdentityKey));
        alice.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-07")]
    public async Task Network_WithDiscovery_IsReadFromThePlatform()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();

        await using var alice = await TestMessengers.StartAsync("Alice", hub.CreateNode(), cancellationToken);

        Assert.Equal(DiscoveryState.Running, alice.Connections.DiscoveryState);
        Assert.Equal(LoopbackDiscovery.FastOptions.Port, alice.Connections.DiscoveryPort);
        var network = Assert.IsType<NetworkSnapshot>(alice.Connections.Network);
        Assert.Equal(LoopbackDiscovery.Interface, Assert.Single(network.Interfaces));
        Assert.True(network.HasAnyNetwork);
        alice.Identity.Dispose();
    }

    [Fact]
    public async Task Network_WithoutDiscovery_IsUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);

        Assert.Equal(DiscoveryState.NotStarted, alice.Connections.DiscoveryState);
        Assert.Null(alice.Connections.Network);
        Assert.Null(alice.Connections.DiscoveryPort);
        alice.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    public async Task DisposeAsync_WhileDialingAHeardContact_StopsEverything()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        var alice = await TestMessengers.StartAsync("Alice", hub.CreateNode(), cancellationToken);
        using var bob = KeySet.Create();
        await alice.Contacts.AddAsync(alice.Contacts.Check(Code(bob, "Bob")), cancellationToken);
        using var heardUntil = new CancellationTokenSource();
        var announcing = AnnounceAsync(hub, bob, alice, ClosedPort(), heardUntil.Token);
        await TestMessengers.WaitUntilAsync(() => alice.Connections.DialStatusOf(bob.IdentityKey) != DialStatus.None, cancellationToken);

        await alice.DisposeAsync();

        Assert.Equal(DiscoveryState.Stopped, alice.Connections.DiscoveryState);
        Assert.Equal(DialStatus.None, alice.Connections.DialStatusOf(bob.IdentityKey));
        await heardUntil.CancelAsync();
        await announcing;
        alice.Identity.Dispose();
    }

    internal static async Task AnnounceAsync(LoopbackDiscovery hub, KeySet sender, Messenger recipient, int port, CancellationToken until)
    {
        Assert.True(SessionContact.TryCreate(sender, recipient.Identity.Keys.IdentityKey, recipient.Identity.Keys.StaticKey, out var recipientForSender));
        using (recipientForSender)
        {
            while (!until.IsCancellationRequested)
            {
                hub.Inject(Announcement.Build([recipientForSender], port));
                try
                {
                    await Task.Delay(50, until);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static int ClosedPort()
    {
        using var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        return port;
    }
}
