using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Noise;
using P2PMessenger.Core.Tests.Copy;

namespace P2PMessenger.Core.Tests.Noise;

public sealed class NkPsk0HandshakeTests
{
    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-08")]
    public void NkPsk0Handshake_IndependentVector_MatchesEveryByteOfMessagesAndHandshakeHash()
    {
        using var responderStatic = NoiseKeys.Private(CopyVectors.PcPrivateKey);
        var prologue = Convert.FromHexString(CopyVectors.Prologue);
        var psk = Convert.FromHexString(CopyVectors.Psk);
        using var initiator = NkPsk0Handshake.CreateInitiator(
            NoiseHash.Blake2b,
            prologue,
            psk,
            responderStatic.PublicKey,
            Convert.FromHexString(CopyVectors.PhoneEphemeral));
        using var responder = NkPsk0Handshake.CreateResponder(
            NoiseHash.Blake2b,
            prologue,
            psk,
            responderStatic,
            Convert.FromHexString(CopyVectors.PcEphemeral));

        var message1 = new byte[NkPsk0Handshake.MessageOverhead];
        Assert.True(initiator.TryWriteMessage(default, message1, out var written1));
        Assert.Equal(NkPsk0Handshake.MessageOverhead, written1);
        Assert.Equal(CopyVectors.HandshakeMsg1[4..], Convert.ToHexStringLower(message1));

        Assert.True(responder.TryReadMessage(message1, Span<byte>.Empty, out var read1));
        Assert.Equal(0, read1);

        var message2 = new byte[NkPsk0Handshake.MessageOverhead];
        Assert.True(responder.TryWriteMessage(default, message2, out var written2));
        Assert.Equal(NkPsk0Handshake.MessageOverhead, written2);
        Assert.Equal(CopyVectors.HandshakeMsg2[4..], Convert.ToHexStringLower(message2));

        Assert.True(initiator.TryReadMessage(message2, Span<byte>.Empty, out var read2));
        Assert.Equal(0, read2);

        Assert.True(initiator.IsCompleted);
        Assert.True(responder.IsCompleted);
        Assert.Equal(CopyVectors.HandshakeHash, Convert.ToHexStringLower(initiator.HandshakeHash));
        Assert.Equal(CopyVectors.HandshakeHash, Convert.ToHexStringLower(responder.HandshakeHash));

        using var iTransport = initiator.TakeTransport();
        using var rTransport = responder.TakeTransport();
        Assert.Throws<InvalidOperationException>(() => initiator.TakeTransport());
        Assert.Throws<InvalidOperationException>(() => responder.TakeTransport());
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-09")]
    public void Message1_WithWrongPreSharedKey_IsRefused()
    {
        using var responderStatic = X25519KeyPair.Create();
        var psk1 = Convert.FromHexString(CopyVectors.Psk);
        var psk2 = (byte[])psk1.Clone();
        psk2[0] ^= 1;

        using var initiator = NkPsk0Handshake.CreateInitiator(NoiseHash.Blake2b, "p"u8, psk1, responderStatic.PublicKey);
        using var responder = NkPsk0Handshake.CreateResponder(NoiseHash.Blake2b, "p"u8, psk2, responderStatic);
        var message1 = new byte[NkPsk0Handshake.MessageOverhead];
        Assert.True(initiator.TryWriteMessage(default, message1, out _));

        Assert.False(responder.TryReadMessage(message1, Span<byte>.Empty, out _));
        Assert.True(responder.HasFailed);
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-09")]
    public void Message1_ForAnotherResponderKeyOrPrologue_IsRefused()
    {
        using var responderStatic = X25519KeyPair.Create();
        using var otherResponder = X25519KeyPair.Create();
        var psk = Convert.FromHexString(CopyVectors.Psk);

        using (var initiator = NkPsk0Handshake.CreateInitiator(NoiseHash.Blake2b, "p"u8, psk, otherResponder.PublicKey))
        using (var responder = NkPsk0Handshake.CreateResponder(NoiseHash.Blake2b, "p"u8, psk, responderStatic))
        {
            var message1 = new byte[NkPsk0Handshake.MessageOverhead];
            Assert.True(initiator.TryWriteMessage(default, message1, out _));
            Assert.False(responder.TryReadMessage(message1, Span<byte>.Empty, out _));
        }

        using (var initiator = NkPsk0Handshake.CreateInitiator(NoiseHash.Blake2b, "prologue-a"u8, psk, responderStatic.PublicKey))
        using (var responder = NkPsk0Handshake.CreateResponder(NoiseHash.Blake2b, "prologue-b"u8, psk, responderStatic))
        {
            var message1 = new byte[NkPsk0Handshake.MessageOverhead];
            Assert.True(initiator.TryWriteMessage(default, message1, out _));
            Assert.False(responder.TryReadMessage(message1, Span<byte>.Empty, out _));
        }
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-09")]
    public void HandshakeMessages_EveryFlippedBit_IsRefused()
    {
        using var responderStatic = NoiseKeys.Private(CopyVectors.PcPrivateKey);
        var prologue = Convert.FromHexString(CopyVectors.Prologue);
        var psk = Convert.FromHexString(CopyVectors.Psk);
        var message1 = Convert.FromHexString(CopyVectors.HandshakeMsg1[4..]);
        var message2 = Convert.FromHexString(CopyVectors.HandshakeMsg2[4..]);

        for (var bit = 0; bit < message1.Length * 8; bit++)
        {
            var damaged = (byte[])message1.Clone();
            damaged[bit / 8] ^= (byte)(1 << (bit % 8));
            using var responder = NkPsk0Handshake.CreateResponder(NoiseHash.Blake2b, prologue, psk, responderStatic);
            Assert.False(responder.TryReadMessage(damaged, Span<byte>.Empty, out _));
        }

        for (var bit = 0; bit < message2.Length * 8; bit++)
        {
            var damaged = (byte[])message2.Clone();
            damaged[bit / 8] ^= (byte)(1 << (bit % 8));
            using var initiator = NkPsk0Handshake.CreateInitiator(NoiseHash.Blake2b, prologue, psk, responderStatic.PublicKey, Convert.FromHexString(CopyVectors.PhoneEphemeral));
            var outbound = new byte[NkPsk0Handshake.MessageOverhead];
            Assert.True(initiator.TryWriteMessage(default, outbound, out _));
            Assert.False(initiator.TryReadMessage(damaged, Span<byte>.Empty, out _));
        }
    }

    [Fact]
    [Trait("Story", "US-22")]
    public void Handshake_OutOfTurnOrImpossibleLength_IsRefused()
    {
        using var responderStatic = X25519KeyPair.Create();
        var psk = Convert.FromHexString(CopyVectors.Psk);
        using var responder = NkPsk0Handshake.CreateResponder(NoiseHash.Blake2b, "p"u8, psk, responderStatic);

        Assert.Throws<InvalidOperationException>(() => responder.TryWriteMessage(default, new byte[NkPsk0Handshake.MessageOverhead], out _));
        Assert.False(responder.TryReadMessage(new byte[NkPsk0Handshake.MessageOverhead - 1], Span<byte>.Empty, out _));
        Assert.True(responder.HasFailed);
    }
}
