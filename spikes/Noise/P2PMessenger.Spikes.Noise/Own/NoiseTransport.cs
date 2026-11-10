namespace P2PMessenger.Spikes.Noise.Own;

public sealed class NoiseTransport : IDisposable
{
    public const int MaxPayloadLength = KkHandshake.MaxMessageLength - CipherState.TagLength;

    private readonly CipherState _sending;
    private readonly CipherState _receiving;

    internal NoiseTransport(CipherState sending, CipherState receiving)
    {
        _sending = sending;
        _receiving = receiving;
    }

    public int WriteMessage(ReadOnlySpan<byte> payload, Span<byte> message)
    {
        if (payload.Length > MaxPayloadLength)
        {
            throw new ArgumentException("The payload does not fit into one Noise message.", nameof(payload));
        }

        if (message.Length < payload.Length + CipherState.TagLength)
        {
            throw new ArgumentException("The message buffer is too small.", nameof(message));
        }

        return _sending.EncryptWithAd(default, payload, message);
    }

    public bool TryReadMessage(ReadOnlySpan<byte> message, Span<byte> payload, out int payloadLength)
    {
        if (message.Length > KkHandshake.MaxMessageLength)
        {
            payloadLength = 0;
            return false;
        }

        return _receiving.TryDecryptWithAd(default, message, payload, out payloadLength);
    }

    public void Dispose()
    {
        _sending.Dispose();
        _receiving.Dispose();
    }
}
