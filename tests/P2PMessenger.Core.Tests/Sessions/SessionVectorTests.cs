using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;

namespace P2PMessenger.Core.Tests.Sessions;

public sealed class SessionVectorTests
{
    [Fact]
    [Trait("Story", "US-05")]
    public void Hint_FixedNonce_EqualsAnIndependentImplementation()
    {
        using var peers = SessionPeers.Create();
        var hint = new byte[ConnectionHint.Length];

        ConnectionHint.Write(peers.BobForAlice, Convert.FromHexString(SessionVectors.HintNonce), hint);

        Assert.Equal(SessionVectors.Hint, Convert.ToHexStringLower(hint));
    }

    [Fact]
    [Trait("Story", "US-05")]
    public void Hint_WrittenByEitherSideOfThePair_IsTheSame()
    {
        using var peers = SessionPeers.Create();
        var nonce = Convert.FromHexString(SessionVectors.HintNonce);
        var fromAlice = new byte[ConnectionHint.Length];
        var fromBob = new byte[ConnectionHint.Length];

        ConnectionHint.Write(peers.BobForAlice, nonce, fromAlice);
        ConnectionHint.Write(peers.AliceForBob, nonce, fromBob);

        Assert.Equal(Convert.ToHexStringLower(fromAlice), Convert.ToHexStringLower(fromBob));
    }

    [Fact]
    public void Prologue_AliceCallingBob_EqualsAnIndependentImplementation()
    {
        using var peers = SessionPeers.Create();

        var prologue = SessionPrologue.Build(Convert.FromHexString(SessionVectors.Hint), peers.Alice.IdentityKey, peers.Bob.IdentityKey);

        Assert.Equal(SessionVectors.Prologue, Convert.ToHexStringLower(prologue));
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("Story", "US-11")]
    [Trait("NFR", "NFR-08")]
    public void Session_FixedNonceAndEphemeralKeys_EqualsAnIndependentImplementationByteForByte()
    {
        using var peers = SessionPeers.Create();
        using var initiator = SessionInitiator.Start(
            peers.Alice,
            peers.BobForAlice,
            Convert.FromHexString(SessionVectors.HintNonce),
            Convert.FromHexString(SessionVectors.InitiatorEphemeral));

        Assert.Equal(SessionVectors.FirstFlight, Convert.ToHexStringLower(initiator.FirstFlight));

        Assert.True(SessionResponder.TryAccept(
            peers.Bob,
            peers.AliceForBob,
            initiator.FirstFlight,
            Convert.FromHexString(SessionVectors.ResponderEphemeral),
            out var bob,
            out var response));
        using (bob)
        {
            Assert.Equal(SessionVectors.Response, Convert.ToHexStringLower(response));

            Assert.True(initiator.TryFinish(response, out var alice));
            using (alice)
            {
                Assert.Equal(SessionVectors.HandshakeHash, Convert.ToHexStringLower(alice.HandshakeHash));
                Assert.Equal(SessionVectors.HandshakeHash, Convert.ToHexStringLower(bob.HandshakeHash));

                var keepAliveFrame = alice.WriteFrame(KeepAlive.Instance);
                Assert.Equal(SessionVectors.KeepAliveFrame, Convert.ToHexStringLower(keepAliveFrame));
                var textFrame = alice.WriteFrame(VectorText());
                Assert.Equal(SessionVectors.TextFrame, Convert.ToHexStringLower(textFrame));

                Assert.True(bob.TryReadFrame(keepAliveFrame.AsSpan(Frame.HeaderLength), out var first));
                Assert.Same(KeepAlive.Instance, first);
                Assert.True(bob.TryReadFrame(textFrame.AsSpan(Frame.HeaderLength), out var second));
                Assert.Equal(VectorText(), Assert.IsType<TextMessage>(second));

                var acknowledgementFrame = bob.WriteFrame(Acknowledgement.Create(VectorId()));
                Assert.Equal(SessionVectors.AcknowledgementFrame, Convert.ToHexStringLower(acknowledgementFrame));
                Assert.True(alice.TryReadFrame(acknowledgementFrame.AsSpan(Frame.HeaderLength), out var third));
                Assert.Equal(VectorId(), Assert.IsType<Acknowledgement>(third).Id);
            }
        }
    }

    [Fact]
    public void Session_VectorFramesReadByBob_GiveTheMessagesOfTheIndependentImplementation()
    {
        using var peers = SessionPeers.Create();
        Assert.True(SessionResponder.TryAccept(
            peers.Bob,
            peers.AliceForBob,
            Convert.FromHexString(SessionVectors.FirstFlight),
            Convert.FromHexString(SessionVectors.ResponderEphemeral),
            out var bob,
            out _));
        using (bob)
        {
            Assert.True(bob.TryReadFrame(Convert.FromHexString(SessionVectors.KeepAliveFrame).AsSpan(Frame.HeaderLength), out var first));
            Assert.IsType<KeepAlive>(first);
            Assert.True(bob.TryReadFrame(Convert.FromHexString(SessionVectors.TextFrame).AsSpan(Frame.HeaderLength), out var second));

            var text = Assert.IsType<TextMessage>(second);
            Assert.Equal(SessionVectors.MessageId, Convert.ToHexStringLower(text.Id.Bytes));
            Assert.Equal(1, text.Sequence);
            Assert.Equal(SessionVectors.WrittenAtMilliseconds, text.WrittenAt.ToUnixTimeMilliseconds());
            Assert.Equal(SessionVectors.Text, text.Text);
        }
    }

    internal static MessageId VectorId()
    {
        if (!MessageId.TryCreate(Convert.FromHexString(SessionVectors.MessageId), out var id))
        {
            throw new InvalidOperationException("The vector message ID was not accepted.");
        }

        return id;
    }

    internal static TextMessage VectorText()
    {
        if (!TextMessage.TryCreate(VectorId(), 1, DateTimeOffset.FromUnixTimeMilliseconds(SessionVectors.WrittenAtMilliseconds), SessionVectors.Text, out var message))
        {
            throw new InvalidOperationException("The vector message was not accepted.");
        }

        return message;
    }
}
