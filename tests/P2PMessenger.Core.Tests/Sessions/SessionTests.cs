using System.Text;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;

namespace P2PMessenger.Core.Tests.Sessions;

public sealed class SessionTests
{
    [Fact]
    [Trait("Story", "US-11")]
    public void Connect_TwoContacts_ShareOneHandshakeHash()
    {
        using var peers = SessionPeers.CreateRandom();
        var (alice, bob) = peers.Connect();
        using (alice)
        using (bob)
        {
            Assert.True(alice.IsInitiator);
            Assert.False(bob.IsInitiator);
            Assert.Equal(Convert.ToHexStringLower(alice.HandshakeHash), Convert.ToHexStringLower(bob.HandshakeHash));
            Assert.Same(peers.BobForAlice, alice.Peer);
            Assert.Same(peers.AliceForBob, bob.Peer);
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    public void Confirmation_InitiatorAfterTheResponse_IsConfirmedAndResponderIsNotYet()
    {
        using var peers = SessionPeers.CreateRandom();
        var (alice, bob) = peers.Connect();
        using (alice)
        using (bob)
        {
            Assert.True(alice.IsConfirmed);
            Assert.False(bob.IsConfirmed);
            Assert.Throws<InvalidOperationException>(() => bob.WriteFrame(KeepAlive.Instance));

            Assert.True(bob.TryReadFrame(alice.WriteFrame(KeepAlive.Instance).AsSpan(Frame.HeaderLength), out _));

            Assert.True(bob.IsConfirmed);
            Assert.True(alice.TryReadFrame(bob.WriteFrame(KeepAlive.Instance).AsSpan(Frame.HeaderLength), out _));
        }
    }

    [Fact]
    [Trait("Story", "US-11")]
    public void Messages_BothWays_ArriveAsWritten()
    {
        using var peers = SessionPeers.CreateRandom();
        var (alice, bob) = peers.ConnectConfirmed();
        using (alice)
        using (bob)
        {
            var hello = Text(1, "Hello, Bob");
            var reply = Text(1, "Hi Alice, привет \U0001F44B");

            Assert.True(bob.TryReadFrame(alice.WriteFrame(hello).AsSpan(Frame.HeaderLength), out var received));
            Assert.Equal(hello, received);
            Assert.True(alice.TryReadFrame(bob.WriteFrame(Acknowledgement.Create(hello.Id)).AsSpan(Frame.HeaderLength), out var acknowledgement));
            Assert.Equal(Acknowledgement.Create(hello.Id), acknowledgement);
            Assert.True(alice.TryReadFrame(bob.WriteFrame(reply).AsSpan(Frame.HeaderLength), out var answer));
            Assert.Equal(reply, answer);
        }
    }

    [Fact]
    [Trait("Story", "US-11")]
    public void Messages_ManyInARow_ArriveInTheOrderWritten()
    {
        using var peers = SessionPeers.CreateRandom();
        var (alice, bob) = peers.ConnectConfirmed();
        using (alice)
        using (bob)
        {
            var frames = Enumerable.Range(1, 50).Select(sequence => alice.WriteFrame(Text(sequence, $"message {sequence}"))).ToList();

            for (var index = 0; index < frames.Count; index++)
            {
                Assert.True(bob.TryReadFrame(frames[index].AsSpan(Frame.HeaderLength), out var message));
                Assert.Equal(index + 1, Assert.IsType<TextMessage>(message).Sequence);
            }
        }
    }

    [Fact]
    [Trait("NFR", "NFR-08")]
    public void Frame_OnTheWire_ShowsNeitherTheTextNorTheMessageId()
    {
        using var peers = SessionPeers.CreateRandom();
        var (alice, bob) = peers.ConnectConfirmed();
        using (alice)
        using (bob)
        {
            const string secret = "The meeting point is the old bridge";
            var message = Text(7, secret);

            var frame = alice.WriteFrame(message);

            Assert.Equal(-1, frame.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)));
            Assert.Equal(-1, frame.AsSpan().IndexOf(Encoding.UTF8.GetBytes("bridge")));
            Assert.Equal(-1, frame.AsSpan().IndexOf(message.Id.Bytes));
        }
    }

    [Fact]
    [Trait("NFR", "NFR-08")]
    public void Frame_KeepAliveAcknowledgementAndShortText_HaveTheSameLength()
    {
        using var peers = SessionPeers.CreateRandom();
        var (alice, bob) = peers.ConnectConfirmed();
        using (alice)
        using (bob)
        {
            var keepAlive = alice.WriteFrame(KeepAlive.Instance);
            var acknowledgement = alice.WriteFrame(Acknowledgement.Create(MessageId.Create()));
            var text = alice.WriteFrame(Text(1, "ok"));
            var longest = alice.WriteFrame(Text(2, new string('a', 220)));

            Assert.Equal(Frame.HeaderLength + Frame.MinTransportBodyLength, keepAlive.Length);
            Assert.Equal(keepAlive.Length, acknowledgement.Length);
            Assert.Equal(keepAlive.Length, text.Length);
            Assert.Equal(keepAlive.Length, longest.Length);
        }
    }

    [Theory]
    [Trait("NFR", "NFR-08")]
    [InlineData(1, 256)]
    [InlineData(220, 256)]
    [InlineData(221, 1024)]
    [InlineData(988, 1024)]
    [InlineData(989, 4096)]
    [InlineData(4060, 4096)]
    [InlineData(4061, 16384)]
    [InlineData(TextMessage.MaxUtf8Length, 16384)]
    public void Frame_TextOfAGivenLength_IsPaddedToItsSizeClass(int textLength, int paddedLength)
    {
        using var peers = SessionPeers.CreateRandom();
        var (alice, bob) = peers.ConnectConfirmed();
        using (alice)
        using (bob)
        {
            var message = Text(1, new string('x', textLength));

            var frame = alice.WriteFrame(message);

            Assert.Equal(Frame.HeaderLength + paddedLength + Frame.TagLength, frame.Length);
            Assert.True(Frame.TryReadTransportLength(frame.AsSpan(0, Frame.HeaderLength), out var bodyLength));
            Assert.Equal(frame.Length - Frame.HeaderLength, bodyLength);
            Assert.True(bob.TryReadFrame(frame.AsSpan(Frame.HeaderLength), out var received));
            Assert.Equal(message, received);
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void Replay_ATransportFrameReadTwice_IsRefusedAndClosesTheSession()
    {
        using var peers = SessionPeers.CreateRandom();
        var (alice, bob) = peers.ConnectConfirmed();
        using (alice)
        using (bob)
        {
            var first = alice.WriteFrame(Text(1, "first"));
            var second = alice.WriteFrame(Text(2, "second"));
            Assert.True(bob.TryReadFrame(first.AsSpan(Frame.HeaderLength), out _));

            Assert.False(bob.TryReadFrame(first.AsSpan(Frame.HeaderLength), out var replayed));

            Assert.Null(replayed);
            Assert.True(bob.IsClosed);
            Assert.False(bob.TryReadFrame(second.AsSpan(Frame.HeaderLength), out _));
            Assert.Throws<ObjectDisposedException>(() => bob.WriteFrame(KeepAlive.Instance));
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void Reordering_TheSecondFrameBeforeTheFirst_IsRefusedAndClosesTheSession()
    {
        using var peers = SessionPeers.CreateRandom();
        var (alice, bob) = peers.ConnectConfirmed();
        using (alice)
        using (bob)
        {
            var first = alice.WriteFrame(Text(1, "first"));
            var second = alice.WriteFrame(Text(2, "second"));

            Assert.False(bob.TryReadFrame(second.AsSpan(Frame.HeaderLength), out _));

            Assert.True(bob.IsClosed);
            Assert.False(bob.TryReadFrame(first.AsSpan(Frame.HeaderLength), out _));
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void Replay_AFrameFromAnEarlierSession_IsRefused()
    {
        using var peers = SessionPeers.CreateRandom();
        var (oldAlice, oldBob) = peers.ConnectConfirmed();
        var (alice, bob) = peers.ConnectConfirmed();
        using (oldAlice)
        using (oldBob)
        using (alice)
        using (bob)
        {
            var old = oldAlice.WriteFrame(Text(1, "sent in the old session"));

            Assert.False(bob.TryReadFrame(old.AsSpan(Frame.HeaderLength), out _));
            Assert.True(oldBob.TryReadFrame(old.AsSpan(Frame.HeaderLength), out _));
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void Replay_ARecordedFirstFlight_GivesAResponderSessionThatNeverConfirms()
    {
        using var peers = SessionPeers.CreateRandom();
        using var initiator = SessionInitiator.Start(peers.Alice, peers.BobForAlice);
        var recorded = initiator.FirstFlight.ToArray();
        Assert.True(SessionResponder.TryAccept(peers.Bob, peers.AliceForBob, recorded, out var original, out var response));
        Assert.True(SessionResponder.TryAccept(peers.Bob, peers.AliceForBob, recorded, out var replayed, out _));
        Assert.True(initiator.TryFinish(response, out var alice));
        using (original)
        using (replayed)
        using (alice)
        {
            var frame = alice.WriteFrame(KeepAlive.Instance);

            Assert.False(replayed.TryReadFrame(frame.AsSpan(Frame.HeaderLength), out _));
            Assert.False(replayed.IsConfirmed);
            Assert.Throws<ObjectDisposedException>(() => replayed.WriteFrame(KeepAlive.Instance));
            Assert.True(original.TryReadFrame(frame.AsSpan(Frame.HeaderLength), out _));
            Assert.True(original.IsConfirmed);
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void Replay_AResponseFromAnEarlierConnection_IsRefused()
    {
        using var peers = SessionPeers.CreateRandom();
        using var earlier = SessionInitiator.Start(peers.Alice, peers.BobForAlice);
        Assert.True(SessionResponder.TryAccept(peers.Bob, peers.AliceForBob, earlier.FirstFlight, out var bob, out var oldResponse));
        using (bob)
        using (var later = SessionInitiator.Start(peers.Alice, peers.BobForAlice))
        {
            Assert.False(later.TryFinish(oldResponse, out var session));

            Assert.Null(session);
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void Stranger_WhoKnowsBobsKeys_IsNotIdentifiedAndNotAccepted()
    {
        using var peers = SessionPeers.CreateRandom();
        using var carol = KeySet.Create();
        using var bobForCarol = SessionPeers.Contact(carol, peers.Bob);
        using var stranger = SessionInitiator.Start(carol, bobForCarol);

        Assert.False(ConnectionHint.TryIdentify(stranger.FirstFlight[..ConnectionHint.Length], [peers.AliceForBob], out var caller));
        Assert.Null(caller);
        Assert.False(SessionResponder.TryAccept(peers.Bob, peers.AliceForBob, stranger.FirstFlight, out _, out _));
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void ChangedKey_AContactWhoComesBackWithNewKeys_IsNotIdentified()
    {
        using var peers = SessionPeers.CreateRandom();
        using var newAlice = KeySet.Create();
        using var bobForNewAlice = SessionPeers.Contact(newAlice, peers.Bob);
        using var initiator = SessionInitiator.Start(newAlice, bobForNewAlice);

        Assert.False(ConnectionHint.TryIdentify(initiator.FirstFlight[..ConnectionHint.Length], [peers.AliceForBob], out _));
        Assert.False(SessionResponder.TryAccept(peers.Bob, peers.AliceForBob, initiator.FirstFlight, out _, out _));
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void WrongResponder_ACallMeantForBob_IsNotIdentifiedByCarol()
    {
        using var peers = SessionPeers.CreateRandom();
        using var carol = KeySet.Create();
        using var aliceForCarol = SessionPeers.Contact(carol, peers.Alice);
        using var initiator = SessionInitiator.Start(peers.Alice, peers.BobForAlice);

        Assert.False(ConnectionHint.TryIdentify(initiator.FirstFlight[..ConnectionHint.Length], [aliceForCarol], out _));
        Assert.False(SessionResponder.TryAccept(carol, aliceForCarol, initiator.FirstFlight, out _, out _));
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void Reflection_AlicesFirstFlightSentBackToAlice_IsIdentifiedButRefused()
    {
        using var peers = SessionPeers.CreateRandom();
        using var initiator = SessionInitiator.Start(peers.Alice, peers.BobForAlice);
        var hint = initiator.FirstFlight[..ConnectionHint.Length];

        Assert.True(ConnectionHint.TryIdentify(hint, [peers.BobForAlice], out var caller));
        Assert.Same(peers.BobForAlice, caller);
        Assert.False(SessionResponder.TryAccept(peers.Alice, peers.BobForAlice, initiator.FirstFlight, out _, out _));
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void KeyBinding_ResponderHoldsAnotherIdentityKeyForTheCallersStaticKey_RefusesTheCall()
    {
        using var peers = SessionPeers.CreateRandom();
        using var carol = KeySet.Create();
        Assert.True(SessionContact.TryCreate(peers.Bob, carol.IdentityKey, peers.Alice.StaticKey, out var aliceUnderCarolsIdentity));
        using (aliceUnderCarolsIdentity)
        using (var initiator = SessionInitiator.Start(peers.Alice, peers.BobForAlice))
        {
            Assert.True(ConnectionHint.TryIdentify(initiator.FirstFlight[..ConnectionHint.Length], [aliceUnderCarolsIdentity], out _));

            Assert.False(SessionResponder.TryAccept(peers.Bob, aliceUnderCarolsIdentity, initiator.FirstFlight, out var session, out var response));

            Assert.Null(session);
            Assert.Null(response);
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void KeyBinding_InitiatorHoldsAnotherIdentityKeyForTheRespondersStaticKey_IsRefusedByTheResponder()
    {
        using var peers = SessionPeers.CreateRandom();
        using var carol = KeySet.Create();
        Assert.True(SessionContact.TryCreate(peers.Alice, carol.IdentityKey, peers.Bob.StaticKey, out var bobUnderCarolsIdentity));
        using (bobUnderCarolsIdentity)
        using (var initiator = SessionInitiator.Start(peers.Alice, bobUnderCarolsIdentity))
        {
            Assert.False(SessionResponder.TryAccept(peers.Bob, peers.AliceForBob, initiator.FirstFlight, out _, out _));
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    public void Contact_WithTheUsersOwnIdentityOrStaticKey_IsRefused()
    {
        using var peers = SessionPeers.CreateRandom();

        Assert.False(SessionContact.TryCreate(peers.Alice, peers.Alice.IdentityKey, peers.Bob.StaticKey, out _));
        Assert.False(SessionContact.TryCreate(peers.Alice, peers.Bob.IdentityKey, peers.Alice.StaticKey, out _));
    }

    [Fact]
    [Trait("Story", "US-05")]
    public void Contact_FromAContactCode_TakesTheKeysOfTheCodeAndConnects()
    {
        using var peers = SessionPeers.CreateRandom();
        var bobsCode = ContactCode.Create(peers.Bob, TestKeys.Name("Bob"));
        var alicesCode = ContactCode.Create(peers.Alice, TestKeys.Name("Alice"));

        Assert.True(SessionContact.TryCreate(peers.Alice, bobsCode, out var bob));
        using (bob)
        using (var initiator = SessionInitiator.Start(peers.Alice, bob))
        {
            Assert.Equal(peers.Bob.IdentityKey, bob.IdentityKey);
            Assert.Equal(peers.Bob.StaticKey, bob.StaticKey);
            Assert.True(SessionResponder.TryAccept(peers.Bob, peers.AliceForBob, initiator.FirstFlight, out var session, out _));
            session.Dispose();
        }

        Assert.False(SessionContact.TryCreate(peers.Alice, alicesCode, out var self));
        Assert.Null(self);
    }

    [Fact]
    public void TryFinish_CalledTwice_Throws()
    {
        using var peers = SessionPeers.CreateRandom();
        using var initiator = SessionInitiator.Start(peers.Alice, peers.BobForAlice);
        Assert.True(SessionResponder.TryAccept(peers.Bob, peers.AliceForBob, initiator.FirstFlight, out var bob, out var response));
        Assert.True(initiator.TryFinish(response, out var alice));
        using (bob)
        using (alice)
        {
            Assert.Throws<InvalidOperationException>(() => initiator.TryFinish(response, out _));
        }
    }

    [Fact]
    public void TryFinish_AfterDispose_Throws()
    {
        using var peers = SessionPeers.CreateRandom();
        var initiator = SessionInitiator.Start(peers.Alice, peers.BobForAlice);
        Assert.True(SessionResponder.TryAccept(peers.Bob, peers.AliceForBob, initiator.FirstFlight, out var bob, out var response));
        using (bob)
        {
            initiator.Dispose();

            Assert.Throws<ObjectDisposedException>(() => initiator.TryFinish(response, out _));
        }
    }

    [Fact]
    public void Session_AfterDispose_WritesNothingAndReadsNothing()
    {
        using var peers = SessionPeers.CreateRandom();
        var (alice, bob) = peers.ConnectConfirmed();
        var frame = alice.WriteFrame(KeepAlive.Instance);

        alice.Dispose();
        bob.Dispose();

        Assert.True(alice.IsClosed);
        Assert.Throws<ObjectDisposedException>(() => alice.WriteFrame(KeepAlive.Instance));
        Assert.False(bob.TryReadFrame(frame.AsSpan(Frame.HeaderLength), out _));
    }

    [Fact]
    [Trait("Story", "US-05")]
    public void Initiator_EveryStart_UsesAFreshHintAndAFreshEphemeralKey()
    {
        using var peers = SessionPeers.CreateRandom();

        var flights = Enumerable.Range(0, 20).Select(_ => peers.FirstFlight()).ToList();

        Assert.Equal(flights.Count, flights.Select(flight => Convert.ToHexStringLower(flight.AsSpan(0, ConnectionHint.NonceLength))).Distinct().Count());
        Assert.Equal(flights.Count, flights.Select(flight => Convert.ToHexStringLower(flight.AsSpan(ConnectionHint.Length, ConnectionHint.TagLength))).Distinct().Count());
        Assert.Equal(flights.Count, flights.Select(flight => Convert.ToHexStringLower(flight.AsSpan(ConnectionHint.Length + Frame.HeaderLength, 32))).Distinct().Count());
    }

    private static TextMessage Text(long sequence, string text)
    {
        if (!TextMessage.TryCreate(MessageId.Create(), sequence, DateTimeOffset.UnixEpoch.AddDays(20_000), text, out var message))
        {
            throw new InvalidOperationException("The test message was not accepted.");
        }

        return message;
    }
}
