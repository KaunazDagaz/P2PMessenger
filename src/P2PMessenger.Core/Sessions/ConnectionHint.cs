using System.Diagnostics.CodeAnalysis;
using NSec.Cryptography;

namespace P2PMessenger.Core.Sessions;

public static class ConnectionHint
{
    public const int NonceLength = 16;
    public const int TagLength = 16;
    public const int Length = NonceLength + TagLength;

    private static ReadOnlySpan<byte> Label => "P2PM/connection-hint/v1"u8;

    public static bool TryIdentify(ReadOnlySpan<byte> hint, IReadOnlyList<SessionContact> contacts, [NotNullWhen(true)] out SessionContact? caller)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        caller = null;
        if (hint.Length != Length)
        {
            return false;
        }

        Span<byte> data = stackalloc byte[Label.Length + NonceLength];
        WriteMacInput(hint[..NonceLength], data);
        ReadOnlySpan<byte> tag = hint[NonceLength..];
        for (int index = 0; index < contacts.Count; index++)
        {
            SessionContact contact = contacts[index];
            if (MacAlgorithm.Blake2b_128.Verify(contact.PairwiseKey, data, tag))
            {
                caller = contact;
                return true;
            }
        }

        return false;
    }

    internal static bool Matches(SessionContact contact, ReadOnlySpan<byte> hint)
    {
        if (hint.Length != Length)
        {
            return false;
        }

        Span<byte> data = stackalloc byte[Label.Length + NonceLength];
        WriteMacInput(hint[..NonceLength], data);
        return MacAlgorithm.Blake2b_128.Verify(contact.PairwiseKey, data, hint[NonceLength..]);
    }

    internal static void Write(SessionContact contact, ReadOnlySpan<byte> nonce, Span<byte> hint)
    {
        if (nonce.Length != NonceLength || hint.Length != Length)
        {
            throw new ArgumentException("The nonce must be 16 bytes and the hint 32 bytes long.", nameof(hint));
        }

        nonce.CopyTo(hint);
        Span<byte> data = stackalloc byte[Label.Length + NonceLength];
        WriteMacInput(nonce, data);
        MacAlgorithm.Blake2b_128.Mac(contact.PairwiseKey, data, hint[NonceLength..]);
    }

    private static void WriteMacInput(ReadOnlySpan<byte> nonce, Span<byte> data)
    {
        Label.CopyTo(data);
        nonce.CopyTo(data[Label.Length..]);
    }
}
