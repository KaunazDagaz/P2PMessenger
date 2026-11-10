namespace P2PMessenger.Spikes.Noise.Candidates;

public interface INoiseParty : IDisposable
{
    bool IsHandshakeComplete { get; }

    byte[]? HandshakeHash { get; }

    int WriteMessage(ReadOnlySpan<byte> payload, Span<byte> message);

    bool TryReadMessage(ReadOnlySpan<byte> message, Span<byte> payload, out int payloadLength);
}
