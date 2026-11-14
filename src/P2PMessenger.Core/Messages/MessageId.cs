using System.Diagnostics.CodeAnalysis;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Core.Messages;

public sealed class MessageId : IEquatable<MessageId>
{
    public const int Length = 16;

    private readonly byte[] _bytes;

    private MessageId(byte[] bytes)
    {
        _bytes = bytes;
    }

    public ReadOnlySpan<byte> Bytes => _bytes;

    public static MessageId Create() => new(RandomNumberGenerator.GetBytes(Length));

    public static bool TryCreate(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out MessageId? id)
    {
        if (bytes.Length != Length)
        {
            id = null;
            return false;
        }

        id = new MessageId(bytes.ToArray());
        return true;
    }

    public bool Equals(MessageId? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => obj is MessageId other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }
}
