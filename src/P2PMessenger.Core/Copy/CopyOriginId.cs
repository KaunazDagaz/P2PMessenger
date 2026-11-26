using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;

namespace P2PMessenger.Core.Copy;

public sealed class CopyOriginId : IEquatable<CopyOriginId>
{
    public const int Length = 32;

    private const byte ContactKind = 1;
    private const byte GroupKind = 2;

    private readonly byte[] bytes;

    private CopyOriginId(byte[] bytes)
    {
        this.bytes = bytes;
    }

    public ReadOnlySpan<byte> Bytes => bytes;

    private static ReadOnlySpan<byte> DomainLabel => "P2PM/copy-origin/v1"u8;

    public static CopyOriginId ForContact(Ed25519PublicKey ownIdentityKey, Ed25519PublicKey contactIdentityKey)
    {
        ArgumentNullException.ThrowIfNull(ownIdentityKey);
        ArgumentNullException.ThrowIfNull(contactIdentityKey);

        byte[] input = new byte[DomainLabel.Length + 1 + Ed25519PublicKey.Length + Ed25519PublicKey.Length];
        DomainLabel.CopyTo(input);
        input[DomainLabel.Length] = ContactKind;
        ownIdentityKey.Bytes.CopyTo(input.AsSpan(DomainLabel.Length + 1));
        contactIdentityKey.Bytes.CopyTo(input.AsSpan(DomainLabel.Length + 1 + Ed25519PublicKey.Length));
        return new CopyOriginId(Digests.Blake2b256(input));
    }

    public static CopyOriginId ForGroup(Ed25519PublicKey ownIdentityKey, GroupId groupId)
    {
        ArgumentNullException.ThrowIfNull(ownIdentityKey);
        ArgumentNullException.ThrowIfNull(groupId);

        byte[] input = new byte[DomainLabel.Length + 1 + Ed25519PublicKey.Length + GroupId.Length];
        DomainLabel.CopyTo(input);
        input[DomainLabel.Length] = GroupKind;
        ownIdentityKey.Bytes.CopyTo(input.AsSpan(DomainLabel.Length + 1));
        groupId.Bytes.CopyTo(input.AsSpan(DomainLabel.Length + 1 + Ed25519PublicKey.Length));
        return new CopyOriginId(Digests.Blake2b256(input));
    }

    public static bool TryCreate(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out CopyOriginId? id)
    {
        if (bytes.Length != Length)
        {
            id = null;
            return false;
        }

        id = new CopyOriginId(bytes.ToArray());
        return true;
    }

    public bool Equals(CopyOriginId? other) => other is not null && bytes.AsSpan().SequenceEqual(other.bytes);

    public override bool Equals(object? obj) => obj is CopyOriginId other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }
}
