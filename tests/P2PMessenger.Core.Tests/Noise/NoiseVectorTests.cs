using P2PMessenger.Core.Noise;

namespace P2PMessenger.Core.Tests.Noise;

public sealed class NoiseVectorTests
{
    [Theory]
    [Trait("NFR", "NFR-08")]
    [MemberData(nameof(NoiseVectors.Names), MemberType = typeof(NoiseVectors))]
    public void KkHandshake_PublishedVector_WritesAndReadsEveryMessageAsTheVector(string name)
    {
        var vector = NoiseVectors.Get(name);
        var hash = NoiseKeys.Hash(vector.Hash);
        var prologue = Convert.FromHexString(vector.Prologue);
        using var initiatorStatic = NoiseKeys.Private(vector.InitiatorStatic);
        using var responderStatic = NoiseKeys.Private(vector.ResponderStatic);
        using var initiator = new KkHandshake(hash, true, prologue, initiatorStatic, NoiseKeys.Public(vector.InitiatorRemoteStatic), Convert.FromHexString(vector.InitiatorEphemeral));
        using var responder = new KkHandshake(hash, false, prologue, responderStatic, NoiseKeys.Public(vector.ResponderRemoteStatic), Convert.FromHexString(vector.ResponderEphemeral));

        for (var index = 0; index < 2; index++)
        {
            var expected = vector.Messages[index];
            var (sender, receiver) = index == 0 ? (initiator, responder) : (responder, initiator);
            var payload = Convert.FromHexString(expected.Payload);
            var message = new byte[payload.Length + KkHandshake.MessageOverhead];

            Assert.True(sender.TryWriteMessage(payload, message, out var written));
            Assert.Equal(expected.Ciphertext, Convert.ToHexStringLower(message.AsSpan(0, written)));

            var read = new byte[payload.Length];
            Assert.True(receiver.TryReadMessage(message.AsSpan(0, written), read, out var readLength));
            Assert.Equal(expected.Payload, Convert.ToHexStringLower(read.AsSpan(0, readLength)));
        }

        Assert.True(initiator.IsCompleted);
        Assert.True(responder.IsCompleted);
        Assert.Equal(Convert.ToHexStringLower(initiator.HandshakeHash), Convert.ToHexStringLower(responder.HandshakeHash));
        if (vector.HandshakeHash is not null)
        {
            Assert.Equal(vector.HandshakeHash, Convert.ToHexStringLower(initiator.HandshakeHash));
        }

        using var initiatorTransport = initiator.TakeTransport();
        using var responderTransport = responder.TakeTransport();
        for (var index = 2; index < vector.Messages.Count; index++)
        {
            var expected = vector.Messages[index];
            var (sender, receiver) = index % 2 == 0 ? (initiatorTransport, responderTransport) : (responderTransport, initiatorTransport);
            var payload = Convert.FromHexString(expected.Payload);
            var message = new byte[payload.Length + CipherState.TagLength];

            var written = sender.Encrypt(payload, message);
            Assert.Equal(expected.Ciphertext, Convert.ToHexStringLower(message.AsSpan(0, written)));

            var read = new byte[payload.Length];
            Assert.True(receiver.TryDecrypt(message.AsSpan(0, written), read, out var readLength));
            Assert.Equal(expected.Payload, Convert.ToHexStringLower(read.AsSpan(0, readLength)));
        }
    }

    [Theory]
    [Trait("NFR", "NFR-09")]
    [MemberData(nameof(NoiseVectors.Names), MemberType = typeof(NoiseVectors))]
    public void KkHandshake_PublishedVectorWithAnotherPrologue_RefusesMessage1(string name)
    {
        var vector = NoiseVectors.Get(name);
        var hash = NoiseKeys.Hash(vector.Hash);
        using var initiatorStatic = NoiseKeys.Private(vector.InitiatorStatic);
        using var responderStatic = NoiseKeys.Private(vector.ResponderStatic);
        using var initiator = new KkHandshake(hash, true, Convert.FromHexString(vector.Prologue), initiatorStatic, NoiseKeys.Public(vector.InitiatorRemoteStatic));
        using var responder = new KkHandshake(hash, false, "another prologue"u8, responderStatic, NoiseKeys.Public(vector.ResponderRemoteStatic));
        var message = new byte[KkHandshake.MessageOverhead];

        Assert.True(initiator.TryWriteMessage(default, message, out _));

        Assert.False(responder.TryReadMessage(message, Span<byte>.Empty, out _));
        Assert.True(responder.HasFailed);
    }
}
