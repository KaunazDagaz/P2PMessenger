using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Tcp;
using static P2PMessenger.Integration.Tests.Connections.TestContacts;

namespace P2PMessenger.Integration.Tests.Connections;

public sealed class AdversarialPeerTests
{
    private static readonly TimeSpan CloseLimit = TimeSpan.FromSeconds(5);

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public async Task Stranger_IsClosedWithoutAByteSentBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start();
        using var stranger = KeySet.Create();
        using var bobForStranger = Create(stranger, bob.Keys);

        var bytesBack = await SendFirstFlightAsync(bob, stranger, bobForStranger, cancellationToken);

        Assert.Equal(0, bytesBack);
        Assert.Equal(0, bob.Observer.ConnectedCount);
        Assert.False(bob.Manager.TryGetConnection(stranger.IdentityKey, out _));
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public async Task ContactWithNewKeys_IsClosedLikeAStranger()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start();
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var aliceAfterReinstall = KeySet.Create();
        using var bobForNewAlice = Create(aliceAfterReinstall, bob.Keys);

        var bytesBack = await SendFirstFlightAsync(bob, aliceAfterReinstall, bobForNewAlice, cancellationToken);

        Assert.Equal(0, bytesBack);
        Assert.Equal(0, bob.Observer.ConnectedCount);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public async Task ContactsStaticKeyUnderAnotherIdentity_IsClosedWithoutAByteSentBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start();
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var impostor = WithAnotherIdentityKey(alice);
        Assert.Equal(alice.StaticKey, impostor.StaticKey);
        Assert.NotEqual(alice.IdentityKey, impostor.IdentityKey);
        using var bobForImpostor = Create(impostor, bob.Keys);

        var bytesBack = await SendFirstFlightAsync(bob, impostor, bobForImpostor, cancellationToken);

        Assert.Equal(0, bytesBack);
        Assert.Equal(0, bob.Observer.ConnectedCount);
    }

    [Theory(Timeout = 30_000)]
    [Trait("NFR", "NFR-09")]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(34)]
    [InlineData(65)]
    [InlineData(66)]
    [InlineData(81)]
    public async Task TamperedFirstFlight_IsClosedWithoutAByteSentBack(int position)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start();
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var bobForAlice = Create(alice, bob.Keys);
        using var initiator = SessionInitiator.Start(alice, bobForAlice);
        var firstFlight = initiator.FirstFlight.ToArray();
        firstFlight[position] ^= 0x01;
        using var client = await Raw.ConnectAsync(bob.Endpoint, cancellationToken);

        await client.GetStream().WriteAsync(firstFlight, cancellationToken);

        Assert.Equal(0, await Raw.CountBytesUntilClosedAsync(client.GetStream(), CloseLimit, cancellationToken));
        Assert.Equal(0, bob.Observer.ConnectedCount);
    }

    [Fact(Timeout = 30_000)]
    [Trait("NFR", "NFR-09")]
    public async Task TruncatedFirstFlight_ThenClosed_IsClosedWithoutAByteSentBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start();
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var bobForAlice = Create(alice, bob.Keys);
        using var initiator = SessionInitiator.Start(alice, bobForAlice);
        using var client = await Raw.ConnectAsync(bob.Endpoint, cancellationToken);
        var stream = client.GetStream();

        await stream.WriteAsync(initiator.FirstFlight[..50].ToArray(), cancellationToken);
        client.Client.Shutdown(SocketShutdown.Send);

        Assert.Equal(0, await Raw.CountBytesUntilClosedAsync(stream, CloseLimit, cancellationToken));
        Assert.Equal(0, bob.Observer.ConnectedCount);
    }

    [Theory(Timeout = 30_000)]
    [Trait("NFR", "NFR-09")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    public async Task CallerWithoutAWholeHint_IsClosedAfterTheHintTimeout_NotTheHandshakeTimeout(int bytesSent)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var log = new LogCapture();
        var options = TestOptions.Fast with { HintTimeout = TimeSpan.FromMilliseconds(300), HandshakeTimeout = TimeSpan.FromSeconds(20) };
        await using var bob = TestPeer.Start(options, log);
        using var client = await Raw.ConnectAsync(bob.Endpoint, cancellationToken);

        await client.GetStream().WriteAsync(RandomNumberGenerator.GetBytes(bytesSent), cancellationToken);

        Assert.Equal(0, await Raw.CountBytesUntilClosedAsync(client.GetStream(), CloseLimit, cancellationToken));
        Assert.True(await Raw.EventuallyAsync(() => log.Entries.Any(entry => entry.EventId.Name == "Refused" && entry.Values.Contains("HintTimedOut")), CloseLimit, cancellationToken));
        Assert.Equal(0, bob.Observer.ConnectedCount);
    }

    [Fact(Timeout = 30_000)]
    [Trait("NFR", "NFR-09")]
    public async Task HintSentInPieces_WithinTheHintTimeout_IsAccepted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start(TestOptions.Fast with { HintTimeout = TimeSpan.FromSeconds(2) });
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var bobForAlice = Create(alice, bob.Keys);
        using var initiator = SessionInitiator.Start(alice, bobForAlice);
        var firstFlight = initiator.FirstFlight.ToArray();
        using var client = await Raw.ConnectAsync(bob.Endpoint, cancellationToken);
        var stream = client.GetStream();

        await stream.WriteAsync(firstFlight.AsMemory(0, 10), cancellationToken);
        await Task.Delay(300, cancellationToken);
        await stream.WriteAsync(firstFlight.AsMemory(10), cancellationToken);
        var response = new byte[SessionInitiator.ResponseLength];
        await stream.ReadExactlyAsync(response, cancellationToken);
        Assert.True(initiator.TryFinish(response, out var session));
        using (session)
        {
            await stream.WriteAsync(session.WriteFrame(KeepAlive.Instance), cancellationToken);

            Assert.True(await Raw.EventuallyAsync(() => bob.Observer.ConnectedCount == 1, CloseLimit, cancellationToken));
        }
    }

    [Fact(Timeout = 30_000)]
    [Trait("NFR", "NFR-09")]
    public async Task TruncatedFirstFlight_ThenSilence_IsClosedAfterTheHandshakeTimeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start(TestOptions.Fast with { HandshakeTimeout = TimeSpan.FromMilliseconds(500) });
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var bobForAlice = Create(alice, bob.Keys);
        using var initiator = SessionInitiator.Start(alice, bobForAlice);
        using var client = await Raw.ConnectAsync(bob.Endpoint, cancellationToken);

        await client.GetStream().WriteAsync(initiator.FirstFlight[..40].ToArray(), cancellationToken);

        Assert.Equal(0, await Raw.CountBytesUntilClosedAsync(client.GetStream(), CloseLimit, cancellationToken));
        Assert.Equal(0, bob.Observer.ConnectedCount);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public async Task ReplayedFirstFlight_IsAnsweredButNeverCounts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start();
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var bobForAlice = Create(alice, bob.Keys);
        using var legitimate = await RawSession.OpenAsync(bob.Endpoint, alice, bobForAlice, cancellationToken);
        var original = await bob.Observer.NextConnectedAsync(cancellationToken);
        using var replayer = await Raw.ConnectAsync(bob.Endpoint, cancellationToken);
        var stream = replayer.GetStream();

        await stream.WriteAsync(legitimate.FirstFlight, cancellationToken);
        var response = new byte[SessionInitiator.ResponseLength];
        await stream.ReadExactlyAsync(response, cancellationToken);
        await stream.WriteAsync(legitimate.Confirmation, cancellationToken);

        Assert.Equal(0, await Raw.CountBytesUntilClosedAsync(stream, CloseLimit, cancellationToken));
        Assert.Equal(1, bob.Observer.ConnectedCount);
        Assert.True(bob.Manager.TryGetConnection(alice.IdentityKey, out var current));
        Assert.Same(original, current);
        Assert.False(original.IsClosed);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public async Task ReplayedFrame_ClosesTheConnection_AndTheMessageArrivesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start();
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var bobForAlice = Create(alice, bob.Keys);
        using var raw = await RawSession.OpenAsync(bob.Endpoint, alice, bobForAlice, cancellationToken);
        var connection = await bob.Observer.NextConnectedAsync(cancellationToken);
        var text = Text(1, "only once");
        var frame = raw.Session.WriteFrame(text);

        await raw.SendAsync(frame, cancellationToken);
        await raw.SendAsync(frame, cancellationToken);

        Assert.Equal(text, (await bob.Observer.NextMessageAsync(cancellationToken)).Message);
        Assert.Same(connection, await bob.Observer.NextDisconnectedAsync(cancellationToken));
        Assert.Equal(ConnectionCloseReason.ProtocolError, connection.CloseReason);
        Assert.Equal(1, bob.Observer.MessageCount);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-11")]
    [Trait("NFR", "NFR-09")]
    public async Task ReorderedFrames_CloseTheConnection_WithoutAMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start();
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var bobForAlice = Create(alice, bob.Keys);
        using var raw = await RawSession.OpenAsync(bob.Endpoint, alice, bobForAlice, cancellationToken);
        var connection = await bob.Observer.NextConnectedAsync(cancellationToken);
        var first = raw.Session.WriteFrame(Text(1, "first"));
        var second = raw.Session.WriteFrame(Text(2, "second"));

        await raw.SendAsync(second, cancellationToken);
        await raw.SendAsync(first, cancellationToken);

        Assert.Same(connection, await bob.Observer.NextDisconnectedAsync(cancellationToken));
        Assert.Equal(ConnectionCloseReason.ProtocolError, connection.CloseReason);
        Assert.Equal(0, bob.Observer.MessageCount);
    }

    [Fact(Timeout = 30_000)]
    [Trait("NFR", "NFR-09")]
    public async Task TruncatedFrame_ClosesTheConnection_WithoutAMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start();
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var bobForAlice = Create(alice, bob.Keys);
        using var raw = await RawSession.OpenAsync(bob.Endpoint, alice, bobForAlice, cancellationToken);
        var connection = await bob.Observer.NextConnectedAsync(cancellationToken);
        var frame = raw.Session.WriteFrame(Text(1, "cut short"));

        await raw.SendAsync(frame[..100], cancellationToken);
        raw.Client.Client.Shutdown(SocketShutdown.Send);

        Assert.Same(connection, await bob.Observer.NextDisconnectedAsync(cancellationToken));
        Assert.Equal(ConnectionCloseReason.PeerClosed, connection.CloseReason);
        Assert.Equal(0, bob.Observer.MessageCount);
    }

    [Theory(Timeout = 30_000)]
    [Trait("NFR", "NFR-09")]
    [InlineData(0)]
    [InlineData(48)]
    [InlineData(271)]
    [InlineData(273)]
    [InlineData(1024)]
    [InlineData(65535)]
    public async Task FrameWithAWrongLength_ClosesTheConnection(int length)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start();
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var bobForAlice = Create(alice, bob.Keys);
        using var raw = await RawSession.OpenAsync(bob.Endpoint, alice, bobForAlice, cancellationToken);
        var connection = await bob.Observer.NextConnectedAsync(cancellationToken);
        var header = new byte[Frame.HeaderLength];
        BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)length);

        await raw.SendAsync(header, cancellationToken);

        Assert.Same(connection, await bob.Observer.NextDisconnectedAsync(cancellationToken));
        Assert.Equal(ConnectionCloseReason.ProtocolError, connection.CloseReason);
        Assert.NotEqual(Raw.StillOpen, await Raw.CountBytesUntilClosedAsync(raw.Stream, CloseLimit, cancellationToken));
    }

    [Theory(Timeout = 30_000)]
    [Trait("NFR", "NFR-09")]
    [InlineData(2)]
    [InlineData(100)]
    [InlineData(257)]
    [InlineData(273)]
    public async Task TamperedFrame_ClosesTheConnection_WithoutAMessage(int position)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start();
        using var alice = KeySet.Create();
        bob.Know(alice);
        using var bobForAlice = Create(alice, bob.Keys);
        using var raw = await RawSession.OpenAsync(bob.Endpoint, alice, bobForAlice, cancellationToken);
        var connection = await bob.Observer.NextConnectedAsync(cancellationToken);
        var frame = raw.Session.WriteFrame(Text(1, "changed on the way"));
        frame[position] ^= 0x80;

        await raw.SendAsync(frame, cancellationToken);

        Assert.Same(connection, await bob.Observer.NextDisconnectedAsync(cancellationToken));
        Assert.Equal(ConnectionCloseReason.ProtocolError, connection.CloseReason);
        Assert.Equal(0, bob.Observer.MessageCount);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public async Task GarbageResponse_IsRefused_AndNothingMoreIsSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        using var bob = KeySet.Create();
        var bobForAlice = alice.Know(bob);
        using var server = new RawServer();
        var dialing = alice.Manager.ConnectAsync(bobForAlice, new TcpDialer(server.Endpoint), cancellationToken);
        using var client = await server.AcceptAsync(cancellationToken);
        var stream = client.GetStream();
        var firstFlight = new byte[SessionInitiator.FirstFlightLength];
        await stream.ReadExactlyAsync(firstFlight, cancellationToken);
        var garbage = RandomNumberGenerator.GetBytes(SessionInitiator.ResponseLength);
        BinaryPrimitives.WriteUInt16BigEndian(garbage, Frame.HandshakeMessageLength);

        await stream.WriteAsync(garbage, cancellationToken);

        var result = await dialing;
        Assert.Equal(ConnectFailure.Refused, result.Failure);
        Assert.Equal(0, await Raw.CountBytesUntilClosedAsync(stream, CloseLimit, cancellationToken));
        Assert.Equal(0, alice.Observer.ConnectedCount);
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public async Task ReplayedResponse_IsRefused_AndNothingMoreIsSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        using var bob = KeySet.Create();
        var bobForAlice = alice.Know(bob);
        using var aliceForBob = Create(bob, alice.Keys);
        using var server = new RawServer();
        byte[] recordedResponse;
        var accepting = RawSession.AcceptAsync(server, bob, aliceForBob, cancellationToken);
        var first = await alice.Manager.ConnectAsync(bobForAlice, new TcpDialer(server.Endpoint), cancellationToken);
        using (var legitimate = await accepting)
        {
            Assert.True(first.IsConnected);
            recordedResponse = legitimate.Response;
        }

        Assert.Same(first.Connection, await alice.Observer.NextConnectedAsync(cancellationToken));
        Assert.Same(first.Connection, await alice.Observer.NextDisconnectedAsync(cancellationToken));
        var dialing = alice.Manager.ConnectAsync(bobForAlice, new TcpDialer(server.Endpoint), cancellationToken);
        using var client = await server.AcceptAsync(cancellationToken);
        var stream = client.GetStream();
        var firstFlight = new byte[SessionInitiator.FirstFlightLength];
        await stream.ReadExactlyAsync(firstFlight, cancellationToken);

        await stream.WriteAsync(recordedResponse, cancellationToken);

        var result = await dialing;
        Assert.Equal(ConnectFailure.Refused, result.Failure);
        Assert.Equal(0, await Raw.CountBytesUntilClosedAsync(stream, CloseLimit, cancellationToken));
        Assert.Equal(1, alice.Observer.ConnectedCount);
    }

    private static async Task<int> SendFirstFlightAsync(TestPeer responder, KeySet caller, SessionContact responderForCaller, CancellationToken cancellationToken)
    {
        using var initiator = SessionInitiator.Start(caller, responderForCaller);
        using var client = await Raw.ConnectAsync(responder.Endpoint, cancellationToken);
        var stream = client.GetStream();
        await stream.WriteAsync(initiator.FirstFlight.ToArray(), cancellationToken);
        return await Raw.CountBytesUntilClosedAsync(stream, CloseLimit, cancellationToken);
    }
}
