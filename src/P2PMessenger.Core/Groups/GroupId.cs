using System.Diagnostics.CodeAnalysis;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Core.Groups;

public sealed class GroupId : IEquatable<GroupId>
{
    public const int Length = 16;

    private readonly byte[] bytes;

    private GroupId(byte[] bytes)
    {
        this.bytes = bytes;
    }

    public ReadOnlySpan<byte> Bytes => bytes;

    public static GroupId Create() => new(RandomNumberGenerator.GetBytes(Length));

    public static bool TryCreate(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out GroupId? id)
    {
        if (bytes.Length != Length)
        {
            id = null;
            return false;
        }

        id = new GroupId(bytes.ToArray());
        return true;
    }

    public bool Equals(GroupId? other) => other is not null && bytes.AsSpan().SequenceEqual(other.bytes);

    public override bool Equals(object? obj) => obj is GroupId other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }
}
