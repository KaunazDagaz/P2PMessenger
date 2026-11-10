using NSec.Cryptography;
using P2PMessenger.Spikes.Noise.Own;

namespace P2PMessenger.Spikes.Noise.Candidates;

public sealed class OwnCandidate : INoiseCandidate
{
    public string Name => "own on NSec";

    public bool CanFixEphemeral(out string reason)
    {
        reason = "the handshake takes an ephemeral key for tests";
        return true;
    }

    public INoiseParty Create(
        NoiseProtocol protocol,
        bool initiator,
        ReadOnlySpan<byte> prologue,
        ReadOnlySpan<byte> localStaticPrivateKey,
        ReadOnlySpan<byte> remoteStaticPublicKey,
        ReadOnlySpan<byte> fixedEphemeralPrivateKey = default)
    {
        Key localStatic = Key.Import(KeyAgreementAlgorithm.X25519, localStaticPrivateKey, KeyBlobFormat.RawPrivateKey);
        try
        {
            KkHandshake handshake = new(protocol, initiator, prologue, localStatic, remoteStaticPublicKey, fixedEphemeralPrivateKey);
            return new Party(localStatic, handshake);
        }
        catch
        {
            localStatic.Dispose();
            throw;
        }
    }

    private sealed class Party(Key localStatic, KkHandshake handshake) : INoiseParty
    {
        public bool IsHandshakeComplete => handshake.IsCompleted;

        public byte[]? HandshakeHash => handshake.IsCompleted ? handshake.HandshakeHash.ToArray() : null;

        public int WriteMessage(ReadOnlySpan<byte> payload, Span<byte> message)
        {
            return handshake.IsCompleted
                ? handshake.Transport.WriteMessage(payload, message)
                : handshake.WriteMessage(payload, message);
        }

        public bool TryReadMessage(ReadOnlySpan<byte> message, Span<byte> payload, out int payloadLength)
        {
            return handshake.IsCompleted
                ? handshake.Transport.TryReadMessage(message, payload, out payloadLength)
                : handshake.TryReadMessage(message, payload, out payloadLength);
        }

        public void Dispose()
        {
            handshake.Dispose();
            localStatic.Dispose();
        }
    }
}
