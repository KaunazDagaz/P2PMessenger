using System.Text;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Tests.Noise;

namespace P2PMessenger.Core.Tests.Copy;

public sealed class CopySessionTests
{
    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-08")]
    public void CopySession_IndependentVector_MatchesHandshakeAndAllTenTransportFramesByteForByte()
    {
        using var pcKeyPair = NoiseKeys.Private(CopyVectors.PcPrivateKey);
        Assert.True(CopyPairingCode.TryParseText(CopyVectors.PairingText, out var pairingCode, out _));
        Assert.True(CopyOriginId.TryCreate(Convert.FromHexString(CopyVectors.ContactOrigin), out var origin));
        Assert.True(DisplayName.TryCreate("Bob", out var bob));

        using var initiator = CopyInitiator.Start(pairingCode, Convert.FromHexString(CopyVectors.PhoneEphemeral));
        Assert.Equal(CopyVectors.HandshakeMsg1, Convert.ToHexStringLower(initiator.FirstMessage));

        Assert.True(CopyResponder.TryAccept(pcKeyPair, pairingCode, initiator.FirstMessage, Convert.FromHexString(CopyVectors.PcEphemeral), out var pcSession, out var response));
        Assert.Equal(CopyVectors.HandshakeMsg2, Convert.ToHexStringLower(response));

        Assert.True(initiator.TryFinish(response, out var phoneSession));
        using (phoneSession)
        using (pcSession)
        {
            Assert.Equal(CopyVectors.HandshakeHash, Convert.ToHexStringLower(phoneSession.HandshakeHash));
            Assert.Equal(CopyVectors.HandshakeHash, Convert.ToHexStringLower(pcSession.HandshakeHash));
            Assert.Throws<InvalidOperationException>(() => pcSession.WriteFrame(CopyReady.Instance));

            AssertFrame(phoneSession, pcSession, CopyReady.Instance, CopyVectors.PhoneReadyFrame);
            AssertFrame(pcSession, phoneSession, CopyReady.Instance, CopyVectors.PcReadyFrame);

            Assert.True(CopyTransferStart.TryCreate(1, out var transferStart));
            AssertFrame(phoneSession, pcSession, transferStart, CopyVectors.TransferStartFrame);

            Assert.True(CopyConversationStart.TryCreate(origin, bob, 2, out var convStart));
            AssertFrame(phoneSession, pcSession, convStart, CopyVectors.ConvStartFrame);

            Assert.True(CopyConversationMessage.TryCreate(1, true, null, DateTimeOffset.FromUnixTimeMilliseconds(CopyVectors.MsgOwnWrittenAtMs), "Hello, PC", out var msgOwn));
            AssertFrame(phoneSession, pcSession, msgOwn, CopyVectors.MsgOwnFrame);

            Assert.True(CopyConversationMessage.TryCreate(2, false, bob, DateTimeOffset.FromUnixTimeMilliseconds(CopyVectors.MsgOtherWrittenAtMs), "Hi from Bob", out var msgOther));
            AssertFrame(phoneSession, pcSession, msgOther, CopyVectors.MsgOtherFrame);

            AssertFrame(phoneSession, pcSession, CopyConversationEnd.Create(origin), CopyVectors.ConvEndFrame);
            AssertFrame(pcSession, phoneSession, CopyConversationSaved.Create(origin), CopyVectors.ConvSavedFrame);

            Assert.True(CopyTransferDone.TryCreate(1, out var transferDone));
            AssertFrame(phoneSession, pcSession, transferDone, CopyVectors.TransferDoneFrame);

            Assert.True(CopyTransferComplete.TryCreate(1, out var transferComplete));
            AssertFrame(pcSession, phoneSession, transferComplete, CopyVectors.TransferCompleteFrame);
        }
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-09")]
    public void CopySession_AlteredPairingCodeInPrologue_RefusesHandshake()
    {
        using var pcKeyPair = X25519KeyPair.Create();
        Assert.True(Ipv4Address.TryCreate(192, 168, 1, 10, out var first));
        Assert.True(Ipv4Address.TryCreate(192, 168, 1, 11, out var second));
        var secret = Convert.FromHexString(CopyVectors.Psk);
        var pcPairing = CopyPairingCode.Create(pcKeyPair.PublicKey, secret, 50000, [first]);
        var phonePairing = CopyPairingCode.Create(pcKeyPair.PublicKey, secret, 50000, [second]);

        using var initiator = CopyInitiator.Start(phonePairing);
        Assert.False(CopyResponder.TryAccept(pcKeyPair, pcPairing, initiator.FirstMessage, out _, out _));
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-09")]
    public void CopySession_EveryFlippedBitOfTransportFrame_IsRefusedAndClosesSession()
    {
        using var pcKeyPair = X25519KeyPair.Create();
        Assert.True(Ipv4Address.TryCreate(127, 0, 0, 1, out var loopback));
        var pairing = CopyPairingCode.Create(pcKeyPair.PublicKey, Convert.FromHexString(CopyVectors.Psk), 50000, [loopback]);

        var frame = BuildFirstFrame(pcKeyPair, pairing, out var firstMessage, out var response);
        var body = frame[CopyFrame.HeaderLength..];

        for (var bit = 0; bit < body.Length * 8; bit++)
        {
            Assert.True(CopyResponder.TryAccept(pcKeyPair, pairing, firstMessage, out var responderSession, out _));
            using (responderSession)
            {
                var damaged = (byte[])body.Clone();
                damaged[bit / 8] ^= (byte)(1 << (bit % 8));
                Assert.False(responderSession.TryReadFrame(damaged, out _));
                Assert.True(responderSession.IsClosed);
            }
        }

        Assert.NotEmpty(response);
    }

    [Fact]
    [Trait("Story", "US-22")]
    public void CopyConversationMessage_MaxLengthTextAndMaxAuthorName_FitsInLargestPaddingClass()
    {
        using var pcKeyPair = X25519KeyPair.Create();
        Assert.True(Ipv4Address.TryCreate(127, 0, 0, 1, out var loopback));
        var pairing = CopyPairingCode.Create(pcKeyPair.PublicKey, Convert.FromHexString(CopyVectors.Psk), 50000, [loopback]);

        using var initiator = CopyInitiator.Start(pairing);
        Assert.True(CopyResponder.TryAccept(pcKeyPair, pairing, initiator.FirstMessage, out var responder, out var response));
        Assert.True(initiator.TryFinish(response, out var sender));
        using (sender)
        using (responder)
        {
            Assert.True(DisplayName.TryCreate(new string('A', DisplayName.MaxUtf8Length), out var maxAuthor));
            var maxText = new string('x', CopyConversationMessage.MaxUtf8Length);
            Assert.Equal(CopyConversationMessage.MaxUtf8Length, Encoding.UTF8.GetByteCount(maxText));
            Assert.True(CopyConversationMessage.TryCreate(1, false, maxAuthor, DateTimeOffset.UnixEpoch, maxText, out var largest));

            var wire = sender.WriteFrame(largest);
            Assert.Equal(CopyFrame.HeaderLength + 32768 + 16, wire.Length);
            Assert.True(CopyFrame.TryReadTransportLength(wire.AsSpan(0, CopyFrame.HeaderLength), out var bodyLength));
            Assert.Equal(32768 + 16, bodyLength);
            Assert.True(responder.TryReadFrame(wire.AsSpan(CopyFrame.HeaderLength), out var decoded));
            Assert.Equal(largest, decoded);
        }
    }

    [Fact]
    [Trait("Story", "US-22")]
    public void CopyMessages_InvalidBoundaries_AreRefused()
    {
        Assert.True(DisplayName.TryCreate("Bob", out var bob));
        Assert.True(CopyOriginId.TryCreate(Convert.FromHexString(CopyVectors.ContactOrigin), out var origin));

        Assert.False(CopyTransferStart.TryCreate(0, out _));
        Assert.False(CopyTransferStart.TryCreate(CopyTransferStart.MaxConversations + 1, out _));
        Assert.False(CopyTransferDone.TryCreate(0, out _));
        Assert.False(CopyTransferComplete.TryCreate(0, out _));
        Assert.False(CopyConversationStart.TryCreate(origin, bob, -1, out _));
        Assert.False(CopyConversationStart.TryCreate(origin, bob, CopyConversationStart.MaxMessages + 1, out _));
        Assert.False(CopyConversationMessage.TryCreate(0, true, null, DateTimeOffset.UnixEpoch, "hi", out _));
        Assert.False(CopyConversationMessage.TryCreate(1, true, bob, DateTimeOffset.UnixEpoch, "hi", out _));
        Assert.False(CopyConversationMessage.TryCreate(1, false, null, DateTimeOffset.UnixEpoch, "hi", out _));
        Assert.False(CopyConversationMessage.TryCreate(1, true, null, DateTimeOffset.UnixEpoch, string.Empty, out _));
        Assert.False(CopyConversationMessage.TryCreate(1, true, null, DateTimeOffset.UnixEpoch, new string('x', CopyConversationMessage.MaxUtf8Length + 1), out _));
        Assert.False(CopyConversationMessage.TryCreate(1, true, null, DateTimeOffset.UnixEpoch.AddMilliseconds(-1), "hi", out _));
    }

    private static void AssertFrame(CopySession writer, CopySession reader, CopyMessage message, string expectedHex)
    {
        var frame = writer.WriteFrame(message);
        Assert.Equal(expectedHex, Convert.ToHexStringLower(frame));
        Assert.True(CopyFrame.TryReadTransportLength(frame.AsSpan(0, CopyFrame.HeaderLength), out var bodyLength));
        Assert.Equal(frame.Length - CopyFrame.HeaderLength, bodyLength);
        Assert.True(reader.TryReadFrame(frame.AsSpan(CopyFrame.HeaderLength), out var decoded));
        Assert.Equal(message, decoded);
    }

    private static byte[] BuildFirstFrame(X25519KeyPair pcKeyPair, CopyPairingCode pairing, out byte[] firstMessage, out byte[] response)
    {
        using var initiator = CopyInitiator.Start(pairing);
        firstMessage = initiator.FirstMessage.ToArray();
        Assert.True(CopyResponder.TryAccept(pcKeyPair, pairing, firstMessage, out var responder, out var reply));
        response = reply;
        using (responder)
        {
            Assert.True(initiator.TryFinish(response, out var sender));
            using (sender)
            {
                return sender.WriteFrame(CopyReady.Instance);
            }
        }
    }
}
