using System.Security.Cryptography;
using P2PMessenger.Spikes.Noise.Own;
using NoiseNet = Noise;

namespace P2PMessenger.Spikes.Noise.Candidates;

public sealed class NoiseNetCandidate : INoiseCandidate
{
    public string Name => "Noise.NET";

    public bool CanFixEphemeral(out string reason) => NoiseNetEphemeralHook.IsAvailable(out reason);

    public INoiseParty Create(
        NoiseProtocol protocol,
        bool initiator,
        ReadOnlySpan<byte> prologue,
        ReadOnlySpan<byte> localStaticPrivateKey,
        ReadOnlySpan<byte> remoteStaticPublicKey,
        ReadOnlySpan<byte> fixedEphemeralPrivateKey = default)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        NoiseNet.Protocol noiseProtocol = NoiseNet.Protocol.Parse(protocol.Name.AsSpan());
        byte[] staticPrivateKey = localStaticPrivateKey.ToArray();
        NoiseNet.HandshakeState handshake;
        try
        {
            handshake = noiseProtocol.Create(initiator, prologue, staticPrivateKey, remoteStaticPublicKey.ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(staticPrivateKey);
        }

        try
        {
            if (!fixedEphemeralPrivateKey.IsEmpty)
            {
                NoiseNetEphemeralHook.Apply(handshake, fixedEphemeralPrivateKey);
            }

            return new Party(handshake);
        }
        catch
        {
            handshake.Dispose();
            throw;
        }
    }

    private sealed class Party(NoiseNet.HandshakeState handshake) : INoiseParty
    {
        private NoiseNet.HandshakeState? _handshake = handshake;
        private NoiseNet.Transport? _transport;

        public bool IsHandshakeComplete => _transport is not null;

        public byte[]? HandshakeHash { get; private set; }

        public int WriteMessage(ReadOnlySpan<byte> payload, Span<byte> message)
        {
            if (_transport is not null)
            {
                return _transport.WriteMessage(payload, message);
            }

            (int written, byte[] hash, NoiseNet.Transport transport) = Handshake.WriteMessage(payload, message);
            Complete(hash, transport);
            return written;
        }

        public bool TryReadMessage(ReadOnlySpan<byte> message, Span<byte> payload, out int payloadLength)
        {
            try
            {
                if (_transport is not null)
                {
                    payloadLength = _transport.ReadMessage(message, payload);
                    return true;
                }

                (int read, byte[] hash, NoiseNet.Transport transport) = Handshake.ReadMessage(message, payload);
                Complete(hash, transport);
                payloadLength = read;
                return true;
            }
            catch (Exception exception) when (exception is CryptographicException or ArgumentException)
            {
                payloadLength = 0;
                return false;
            }
        }

        public void Dispose()
        {
            _handshake?.Dispose();
            _handshake = null;
            _transport?.Dispose();
            _transport = null;
        }

        private NoiseNet.HandshakeState Handshake => _handshake ?? throw new ObjectDisposedException(nameof(Party));

        private void Complete(byte[]? hash, NoiseNet.Transport? transport)
        {
            if (transport is null)
            {
                return;
            }

            HandshakeHash = hash;
            _transport = transport;
            _handshake?.Dispose();
            _handshake = null;
        }
    }
}
