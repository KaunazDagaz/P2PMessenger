using P2PMessenger.Spikes.Noise.Candidates;
using P2PMessenger.Spikes.Noise.Own;

namespace P2PMessenger.Spikes.Noise.Checks;

internal static class Messages
{
    public static byte[] Write(INoiseParty sender, ReadOnlySpan<byte> payload)
    {
        byte[] buffer = new byte[KkHandshake.MaxMessageLength];
        int written = sender.WriteMessage(payload, buffer);
        return buffer[..written];
    }

    public static bool TryRead(INoiseParty receiver, ReadOnlySpan<byte> message, out byte[] payload)
    {
        byte[] buffer = new byte[KkHandshake.MaxMessageLength + 1];
        if (!receiver.TryReadMessage(message, buffer, out int read))
        {
            payload = [];
            return false;
        }

        payload = buffer[..read];
        return true;
    }

    public static bool Transfer(INoiseParty sender, INoiseParty receiver, ReadOnlySpan<byte> payload, out string problem)
    {
        byte[] message = Write(sender, payload);
        if (!TryRead(receiver, message, out byte[] received))
        {
            problem = "the receiver refused it";
            return false;
        }

        if (!received.AsSpan().SequenceEqual(payload))
        {
            problem = "it decrypted to a different payload";
            return false;
        }

        problem = string.Empty;
        return true;
    }

    public static byte[] FlipBit(ReadOnlySpan<byte> message, int bit)
    {
        byte[] changed = message.ToArray();
        changed[bit / 8] ^= (byte)(1 << (bit % 8));
        return changed;
    }
}
