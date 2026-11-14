using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Noise;

namespace P2PMessenger.Core.Tests.Noise;

public sealed class KkHandshakeTests
{
    [Fact]
    [Trait("NFR", "NFR-09")]
    public void Message1_FromAKeyThatIsNotTheExpectedContact_IsRefused()
    {
        using var initiator = X25519KeyPair.Create();
        using var responder = X25519KeyPair.Create();
        using var stranger = X25519KeyPair.Create();
        using var write = new KkHandshake(NoiseHash.Blake2b, true, "p"u8, stranger, responder.PublicKey);
        using var read = new KkHandshake(NoiseHash.Blake2b, false, "p"u8, responder, initiator.PublicKey);
        var message = new byte[KkHandshake.MessageOverhead];

        Assert.True(write.TryWriteMessage(default, message, out _));

        Assert.False(read.TryReadMessage(message, Span<byte>.Empty, out _));
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void Message1_MeantForAnotherKey_IsRefused()
    {
        using var initiator = X25519KeyPair.Create();
        using var responder = X25519KeyPair.Create();
        using var other = X25519KeyPair.Create();
        using var write = new KkHandshake(NoiseHash.Blake2b, true, "p"u8, initiator, other.PublicKey);
        using var read = new KkHandshake(NoiseHash.Blake2b, false, "p"u8, responder, initiator.PublicKey);
        var message = new byte[KkHandshake.MessageOverhead];

        Assert.True(write.TryWriteMessage(default, message, out _));

        Assert.False(read.TryReadMessage(message, Span<byte>.Empty, out _));
    }

    [Fact]
    public void Handshake_AfterAFailure_RefusesEverythingElse()
    {
        using var initiator = X25519KeyPair.Create();
        using var responder = X25519KeyPair.Create();
        using var write = new KkHandshake(NoiseHash.Blake2b, true, "p"u8, initiator, responder.PublicKey);
        using var read = new KkHandshake(NoiseHash.Blake2b, false, "p"u8, responder, initiator.PublicKey);
        var message = new byte[KkHandshake.MessageOverhead];
        Assert.True(write.TryWriteMessage(default, message, out _));
        var damaged = (byte[])message.Clone();
        damaged[^1] ^= 1;

        Assert.False(read.TryReadMessage(damaged, Span<byte>.Empty, out _));

        Assert.True(read.HasFailed);
        Assert.False(read.TryReadMessage(message, Span<byte>.Empty, out _));
        Assert.Throws<InvalidOperationException>(() => read.TryWriteMessage(default, new byte[KkHandshake.MessageOverhead], out _));
    }

    [Fact]
    public void Handshake_WritingOutOfTurn_Throws()
    {
        using var initiator = X25519KeyPair.Create();
        using var responder = X25519KeyPair.Create();
        using var read = new KkHandshake(NoiseHash.Blake2b, false, "p"u8, responder, initiator.PublicKey);

        Assert.Throws<InvalidOperationException>(() => read.TryWriteMessage(default, new byte[KkHandshake.MessageOverhead], out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(KkHandshake.MessageOverhead - 1)]
    [InlineData(KkHandshake.MaxMessageLength + 1)]
    public void Message1_OfImpossibleLength_IsRefused(int length)
    {
        using var initiator = X25519KeyPair.Create();
        using var responder = X25519KeyPair.Create();
        using var read = new KkHandshake(NoiseHash.Blake2b, false, "p"u8, responder, initiator.PublicKey);

        Assert.False(read.TryReadMessage(new byte[length], new byte[length], out _));
    }

    [Fact]
    public void Handshake_CompletedAndTransportTaken_CannotHandItOverTwice()
    {
        using var initiator = X25519KeyPair.Create();
        using var responder = X25519KeyPair.Create();
        using var write = new KkHandshake(NoiseHash.Blake2b, true, "p"u8, initiator, responder.PublicKey);
        using var read = new KkHandshake(NoiseHash.Blake2b, false, "p"u8, responder, initiator.PublicKey);
        var message1 = new byte[KkHandshake.MessageOverhead];
        var message2 = new byte[KkHandshake.MessageOverhead];
        Assert.True(write.TryWriteMessage(default, message1, out _));
        Assert.True(read.TryReadMessage(message1, Span<byte>.Empty, out _));
        Assert.True(read.TryWriteMessage(default, message2, out _));
        Assert.True(write.TryReadMessage(message2, Span<byte>.Empty, out _));

        using var transport = write.TakeTransport();

        Assert.Throws<InvalidOperationException>(() => write.TakeTransport());
        Assert.Throws<InvalidOperationException>(() => write.TryWriteMessage(default, message1, out _));
    }
}
