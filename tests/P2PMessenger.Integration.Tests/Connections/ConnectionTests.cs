using System.Net;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Tcp;
using static P2PMessenger.Integration.Tests.Connections.TestContacts;

namespace P2PMessenger.Integration.Tests.Connections;

public sealed class ConnectionTests
{
    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-05")]
    public async Task Connect_TwoContacts_BothSidesHoldOneConfirmedConnection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        var aliceForBob = bob.Know(alice);

        var result = await alice.ConnectAsync(bobForAlice, bob, cancellationToken);

        Assert.True(result.IsConnected);
        Assert.Equal(ConnectFailure.None, result.Failure);
        Assert.True(result.Connection.IsInitiator);
        Assert.Same(bobForAlice, result.Connection.Contact);
        var accepted = await bob.Observer.NextConnectedAsync(cancellationToken);
        Assert.False(accepted.IsInitiator);
        Assert.Same(aliceForBob, accepted.Contact);
        Assert.True(alice.Manager.TryGetConnection(bob.Keys.IdentityKey, out var aliceSide));
        Assert.Same(result.Connection, aliceSide);
        Assert.True(bob.Manager.TryGetConnection(alice.Keys.IdentityKey, out var bobSide));
        Assert.Same(accepted, bobSide);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-11")]
    public async Task Messages_BothWays_ArriveInTheOrderWritten()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);
        bob.Observer.OnMessage = async (connection, message) =>
        {
            if (message is TextMessage text)
            {
                Assert.True(await connection.SendAsync(Acknowledgement.Create(text.Id), cancellationToken));
            }
        };

        var result = await alice.ConnectAsync(bobForAlice, bob, cancellationToken);
        Assert.True(result.IsConnected);
        var connection = result.Connection;
        var texts = Enumerable.Range(1, 20).Select(sequence => Text(sequence, $"message {sequence}")).ToList();
        foreach (var text in texts)
        {
            Assert.True(await connection.SendAsync(text, cancellationToken));
        }

        foreach (var text in texts)
        {
            var received = await bob.Observer.NextMessageAsync(cancellationToken);
            Assert.Equal(text, received.Message);
        }

        foreach (var text in texts)
        {
            var acknowledgement = await alice.Observer.NextMessageAsync(cancellationToken);
            Assert.Equal(Acknowledgement.Create(text.Id), acknowledgement.Message);
        }

        Assert.Equal(texts.Count, bob.Observer.MessageCount);
        Assert.Equal(texts.Count, alice.Observer.MessageCount);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task Connect_TheOtherSideDials_EitherSideCanOpenTheSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start();
        alice.Know(bob);
        var aliceForBob = bob.Know(alice);

        var result = await bob.ConnectAsync(aliceForBob, alice, cancellationToken);
        Assert.True(result.IsConnected);
        var accepted = await alice.Observer.NextConnectedAsync(cancellationToken);
        var hello = Text(1, "written by the side that was called");

        Assert.True(await accepted.SendAsync(hello, cancellationToken));

        var received = await bob.Observer.NextMessageAsync(cancellationToken);
        Assert.Same(result.Connection, received.Connection);
        Assert.Equal(hello, received.Message);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task Connect_AlreadyConnected_ReturnsTheSameConnection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);

        var first = await alice.ConnectAsync(bobForAlice, bob, cancellationToken);
        var second = await alice.ConnectAsync(bobForAlice, bob, cancellationToken);
        await bob.Observer.NextConnectedAsync(cancellationToken);
        await Task.Delay(200, cancellationToken);

        Assert.NotNull(first.Connection);
        Assert.Same(first.Connection, second.Connection);
        Assert.Equal(1, bob.Observer.ConnectedCount);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task Connect_ManyCallsAtOnce_OpenOneSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => alice.ConnectAsync(bobForAlice, bob, cancellationToken)));
        await bob.Observer.NextConnectedAsync(cancellationToken);
        await Task.Delay(200, cancellationToken);

        Assert.NotNull(results[0].Connection);
        Assert.All(results, result => Assert.Same(results[0].Connection, result.Connection));
        Assert.Equal(1, bob.Observer.ConnectedCount);
        Assert.Equal(1, alice.Observer.ConnectedCount);
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-06")]
    public async Task SimultaneousOpen_BothSidesDial_EndWithTheSameSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var round = 0; round < 10; round++)
        {
            await using var alice = TestPeer.Start();
            await using var bob = TestPeer.Start();
            var bobForAlice = alice.Know(bob);
            var aliceForBob = bob.Know(alice);

            var dials = await Task.WhenAll(
                alice.ConnectAsync(bobForAlice, bob, cancellationToken),
                bob.ConnectAsync(aliceForBob, alice, cancellationToken));

            Assert.All(dials, dial => Assert.True(dial.IsConnected));
            Assert.True(await Raw.EventuallyAsync(() => HoldTheSameSession(alice, bob), TimeSpan.FromSeconds(10), cancellationToken), $"Round {round} did not settle on one session.");
            Assert.True(alice.Manager.TryGetConnection(bob.Keys.IdentityKey, out var aliceSide));
            Assert.True(bob.Manager.TryGetConnection(alice.Keys.IdentityKey, out var bobSide));
            var hello = Text(1, "over the session both sides kept");
            Assert.True(await aliceSide.SendAsync(hello, cancellationToken));
            var received = await bob.Observer.NextMessageAsync(cancellationToken);
            Assert.Same(bobSide, received.Connection);
            Assert.Equal(hello, received.Message);
        }
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task NewSessionFromTheSameSide_ReplacesTheOldOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start();
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var bobForAlice = Create(alice, bob.Keys);
        using var first = await RawSession.OpenAsync(bob.Endpoint, alice, bobForAlice, cancellationToken);
        var old = await bob.Observer.NextConnectedAsync(cancellationToken);

        using var second = await RawSession.OpenAsync(bob.Endpoint, alice, bobForAlice, cancellationToken);

        var replacement = await bob.Observer.NextConnectedAsync(cancellationToken);
        Assert.Same(old, await bob.Observer.NextDisconnectedAsync(cancellationToken));
        Assert.Equal(ConnectionCloseReason.Replaced, old.CloseReason);
        Assert.True(bob.Manager.TryGetConnection(alice.IdentityKey, out var current));
        Assert.Same(replacement, current);
        Assert.NotEqual(Raw.StillOpen, await Raw.CountBytesUntilClosedAsync(first.Stream, TimeSpan.FromSeconds(5), cancellationToken));
        var text = Text(1, "after the replacement");
        await second.SendAsync(second.Session.WriteFrame(text), cancellationToken);
        var received = await bob.Observer.NextMessageAsync(cancellationToken);
        Assert.Same(replacement, received.Connection);
        Assert.Equal(text, received.Message);
    }

    [Theory(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewSessionFromTheOtherSide_WithinTheWindow_KeepsTheSessionOpenedByTheLowerKey(bool callerIsLower)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start(TestOptions.Fast with { SimultaneousOpenWindow = TimeSpan.FromMinutes(5) });
        using var bob = CreateWithStaticKeyOrder(alice.Keys, lower: callerIsLower);
        var bobForAlice = alice.Know(bob);
        using var aliceForBob = Create(bob, alice.Keys);
        using var server = new RawServer();
        var accepting = RawSession.AcceptAsync(server, bob, aliceForBob, cancellationToken);
        var dial = await alice.Manager.ConnectAsync(bobForAlice, new TcpDialer(server.Endpoint), cancellationToken);
        using var opened = await accepting;
        Assert.True(dial.IsConnected);
        var existing = dial.Connection;
        await alice.Observer.NextConnectedAsync(cancellationToken);

        using var incoming = await RawSession.OpenAsync(alice.Endpoint, bob, aliceForBob, cancellationToken);

        if (callerIsLower)
        {
            var replacement = await alice.Observer.NextConnectedAsync(cancellationToken);
            Assert.Same(existing, await alice.Observer.NextDisconnectedAsync(cancellationToken));
            Assert.Equal(ConnectionCloseReason.Replaced, existing.CloseReason);
            Assert.False(replacement.IsInitiator);
        }
        else
        {
            Assert.Equal(0, await Raw.CountBytesUntilClosedAsync(incoming.Stream, TimeSpan.FromSeconds(5), cancellationToken));
            Assert.False(existing.IsClosed);
            Assert.True(alice.Manager.TryGetConnection(bob.IdentityKey, out var current));
            Assert.Same(existing, current);
            Assert.Equal(1, alice.Observer.ConnectedCount);
        }
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task NewSessionFromTheOtherSide_AfterTheWindow_ReplacesTheOldOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start(TestOptions.Fast with { SimultaneousOpenWindow = TimeSpan.FromMilliseconds(200) });
        using var bob = CreateWithStaticKeyOrder(alice.Keys, lower: false);
        var bobForAlice = alice.Know(bob);
        using var aliceForBob = Create(bob, alice.Keys);
        using var server = new RawServer();
        var accepting = RawSession.AcceptAsync(server, bob, aliceForBob, cancellationToken);
        var dial = await alice.Manager.ConnectAsync(bobForAlice, new TcpDialer(server.Endpoint), cancellationToken);
        using var opened = await accepting;
        Assert.True(dial.IsConnected);
        var existing = dial.Connection;
        await alice.Observer.NextConnectedAsync(cancellationToken);
        await Task.Delay(500, cancellationToken);

        using var incoming = await RawSession.OpenAsync(alice.Endpoint, bob, aliceForBob, cancellationToken);

        var replacement = await alice.Observer.NextConnectedAsync(cancellationToken);
        Assert.Same(existing, await alice.Observer.NextDisconnectedAsync(cancellationToken));
        Assert.Equal(ConnectionCloseReason.Replaced, existing.CloseReason);
        Assert.False(replacement.IsInitiator);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task KeepConnected_ConnectionDrops_IsOpenedAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);

        alice.Manager.KeepConnected(bobForAlice, bob.Dialer);
        var first = await bob.Observer.NextConnectedAsync(cancellationToken);
        first.Close();

        Assert.Same(first, await bob.Observer.NextDisconnectedAsync(cancellationToken));
        var second = await bob.Observer.NextConnectedAsync(cancellationToken);
        Assert.NotSame(first, second);
        Assert.True(alice.Manager.StopKeepingConnected(bob.Keys.IdentityKey));
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task KeepConnected_FirstDialsFail_RetriesUntilConnected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);
        var dialer = new CountingDialer(bob.Dialer, failuresFirst: 3);

        alice.Manager.KeepConnected(bobForAlice, dialer);

        await bob.Observer.NextConnectedAsync(cancellationToken);
        Assert.Equal(4, dialer.Calls);
        Assert.True(alice.Manager.StopKeepingConnected(bob.Keys.IdentityKey));
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task StopKeepingConnected_ConnectionDropsAfterwards_IsNotOpenedAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);
        var dialer = new CountingDialer(bob.Dialer);
        alice.Manager.KeepConnected(bobForAlice, dialer);
        var first = await bob.Observer.NextConnectedAsync(cancellationToken);

        Assert.True(alice.Manager.StopKeepingConnected(bob.Keys.IdentityKey));
        first.Close();
        await bob.Observer.NextDisconnectedAsync(cancellationToken);
        await Task.Delay(1000, cancellationToken);

        Assert.Equal(1, dialer.Calls);
        Assert.Equal(1, bob.Observer.ConnectedCount);
        Assert.False(alice.Manager.StopKeepingConnected(bob.Keys.IdentityKey));
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task IdleConnection_KeepAlives_KeepItOpen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = TestOptions.Fast with { KeepAliveInterval = TimeSpan.FromMilliseconds(200), IdleTimeout = TimeSpan.FromMilliseconds(1500) };
        await using var alice = TestPeer.Start(options);
        await using var bob = TestPeer.Start(options);
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);
        var result = await alice.ConnectAsync(bobForAlice, bob, cancellationToken);
        Assert.True(result.IsConnected);
        var bobSide = await bob.Observer.NextConnectedAsync(cancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);

        Assert.False(result.Connection.IsClosed);
        Assert.False(bobSide.IsClosed);
        Assert.Equal(0, alice.Observer.DisconnectedCount);
        Assert.Equal(0, bob.Observer.DisconnectedCount);
        Assert.Equal(0, bob.Observer.MessageCount);
        Assert.Equal(0, alice.Observer.MessageCount);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task SilentPeer_IsClosedAfterTheIdleTimeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start(TestOptions.Fast with { KeepAliveInterval = TimeSpan.FromMilliseconds(200), IdleTimeout = TimeSpan.FromSeconds(1) });
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var bobForAlice = Create(alice, bob.Keys);
        using var raw = await RawSession.OpenAsync(bob.Endpoint, alice, bobForAlice, cancellationToken);
        var connection = await bob.Observer.NextConnectedAsync(cancellationToken);

        var closed = await bob.Observer.NextDisconnectedAsync(cancellationToken);

        Assert.Same(connection, closed);
        Assert.Equal(ConnectionCloseReason.IdleTimeout, closed.CloseReason);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task Dispose_WithAnOpenConnection_ClosesItAndBothSidesSeeTheEnd()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);
        var result = await alice.ConnectAsync(bobForAlice, bob, cancellationToken);
        Assert.True(result.IsConnected);
        var bobSide = await bob.Observer.NextConnectedAsync(cancellationToken);

        await alice.Manager.DisposeAsync();

        var aliceSide = await alice.Observer.NextDisconnectedAsync(cancellationToken);
        Assert.Same(result.Connection, aliceSide);
        Assert.Equal(ConnectionCloseReason.ShutDown, aliceSide.CloseReason);
        Assert.Same(bobSide, await bob.Observer.NextDisconnectedAsync(cancellationToken));
        Assert.True(
            bobSide.CloseReason is ConnectionCloseReason.PeerClosed or ConnectionCloseReason.ConnectionLost,
            $"Closed with {bobSide.CloseReason}.");
        var afterwards = await alice.ConnectAsync(bobForAlice, bob, cancellationToken);
        Assert.Equal(ConnectFailure.ShutDown, afterwards.Failure);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task Connect_NothingListens_IsUnreachable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        using var bob = KeySet.Create();
        var bobForAlice = alice.Know(bob);

        var result = await alice.Manager.ConnectAsync(bobForAlice, new TcpDialer(new IPEndPoint(IPAddress.Loopback, Raw.ClosedPort())), cancellationToken);

        Assert.False(result.IsConnected);
        Assert.Equal(ConnectFailure.Unreachable, result.Failure);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task Connect_PeerNeverAnswers_TimesOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start(TestOptions.Fast with { HandshakeTimeout = TimeSpan.FromMilliseconds(500) });
        using var bob = KeySet.Create();
        var bobForAlice = alice.Know(bob);
        using var server = new RawServer();

        var result = await alice.Manager.ConnectAsync(bobForAlice, new TcpDialer(server.Endpoint), cancellationToken);

        Assert.Equal(ConnectFailure.TimedOut, result.Failure);
        Assert.Equal(0, alice.Observer.ConnectedCount);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task Connect_Cancelled_StopsWaitingAndOpensNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start(TestOptions.Fast with { HandshakeTimeout = TimeSpan.FromSeconds(1) });
        using var bob = KeySet.Create();
        var bobForAlice = alice.Know(bob);
        using var server = new RawServer();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => alice.Manager.ConnectAsync(bobForAlice, new TcpDialer(server.Endpoint), cancellation.Token));

        await Task.Delay(1500, cancellationToken);
        Assert.False(alice.Manager.TryGetConnection(bob.IdentityKey, out _));
        Assert.Equal(0, alice.Observer.ConnectedCount);
    }

    private static bool HoldTheSameSession(TestPeer alice, TestPeer bob) =>
        alice.Manager.TryGetConnection(bob.Keys.IdentityKey, out var aliceSide)
        && bob.Manager.TryGetConnection(alice.Keys.IdentityKey, out var bobSide)
        && aliceSide.IsInitiator != bobSide.IsInitiator
        && alice.Observer.OpenCount == 1
        && bob.Observer.OpenCount == 1;
}
