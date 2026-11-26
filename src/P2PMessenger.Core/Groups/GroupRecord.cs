using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Core.Groups;

public sealed class GroupRecord : IEquatable<GroupRecord>
{
    public const byte Version = 1;
    public const int MinMembers = 2;
    public const int MaxMembers = 160;
    public const int HashLength = 32;
    public const int MaxEpoch = int.MaxValue;

    internal const int GroupIdOffset = 1;
    internal const int EpochOffset = GroupIdOffset + GroupId.Length;
    internal const int KeyCheckOffset = EpochOffset + sizeof(uint);
    internal const int NameLengthOffset = KeyCheckOffset + GroupKey.CheckLength;
    internal const int NameOffset = NameLengthOffset + 1;
    internal const int SignatureLength = Ed25519KeyPair.SignatureLength;
    internal const int MaxMemberLength = Ed25519PublicKey.Length + 1 + DisplayName.MaxUtf8Length;
    internal const int MinLength = NameOffset + 1 + 1 + (MinMembers * (Ed25519PublicKey.Length + 2)) + Ed25519PublicKey.Length + SignatureLength;
    internal const int MaxLength = NameOffset + DisplayName.MaxUtf8Length + 1 + (MaxMembers * MaxMemberLength) + Ed25519PublicKey.Length + SignatureLength;

    private readonly byte[] bytes;
    private readonly byte[] hash;

    private GroupRecord(byte[] bytes, GroupId groupId, int epoch, DisplayName name, IReadOnlyList<GroupMember> members, Ed25519PublicKey signer)
    {
        this.bytes = bytes;
        GroupId = groupId;
        Epoch = epoch;
        Name = name;
        Members = members;
        Signer = signer;
        hash = HashOf(bytes.AsSpan(0, bytes.Length - SignatureLength));
    }

    public GroupId GroupId { get; }

    public int Epoch { get; }

    public DisplayName Name { get; }

    public IReadOnlyList<GroupMember> Members { get; }

    public Ed25519PublicKey Signer { get; }

    public ReadOnlySpan<byte> KeyCheck => bytes.AsSpan(KeyCheckOffset, GroupKey.CheckLength);

    public ReadOnlySpan<byte> Hash => hash;

    public int Length => bytes.Length;

    internal static ReadOnlySpan<byte> SignatureContext => "P2PM/group-record/v1"u8;

    private static ReadOnlySpan<byte> HashLabel => "P2PM/group-record-hash/v1"u8;

    public static GroupRecord Create(GroupId groupId, int epoch, DisplayName name, GroupKey key, IEnumerable<GroupMember> members, KeySet signer)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentOutOfRangeException.ThrowIfLessThan(epoch, 1);

        List<GroupMember> sorted = [.. members];
        sorted.Sort(static (left, right) => Compare(left.IdentityKey, right.IdentityKey));
        if (sorted.Count is < MinMembers or > MaxMembers)
        {
            throw new ArgumentException($"A group has {MinMembers} to {MaxMembers} members.", nameof(members));
        }

        for (int index = 1; index < sorted.Count; index++)
        {
            if (sorted[index - 1].IdentityKey.Equals(sorted[index].IdentityKey))
            {
                throw new ArgumentException("A person can be a member of a group only once.", nameof(members));
            }
        }

        if (!sorted.Exists(member => member.IdentityKey.Equals(signer.IdentityKey)))
        {
            throw new ArgumentException("The person who signs a group record must be one of its members.", nameof(signer));
        }

        ReadOnlySpan<byte> groupName = name.Utf8Bytes;
        int length = NameOffset + groupName.Length + 1 + Ed25519PublicKey.Length + SignatureLength;
        foreach (GroupMember member in sorted)
        {
            length += Ed25519PublicKey.Length + 1 + member.Name.Utf8Bytes.Length;
        }

        byte[] bytes = new byte[length];
        bytes[0] = Version;
        groupId.Bytes.CopyTo(bytes.AsSpan(GroupIdOffset));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(EpochOffset), (uint)epoch);
        key.Check.CopyTo(bytes.AsSpan(KeyCheckOffset));
        bytes[NameLengthOffset] = (byte)groupName.Length;
        groupName.CopyTo(bytes.AsSpan(NameOffset));

        int offset = NameOffset + groupName.Length;
        bytes[offset++] = (byte)sorted.Count;
        foreach (GroupMember member in sorted)
        {
            member.IdentityKey.Bytes.CopyTo(bytes.AsSpan(offset));
            offset += Ed25519PublicKey.Length;
            ReadOnlySpan<byte> memberName = member.Name.Utf8Bytes;
            bytes[offset++] = (byte)memberName.Length;
            memberName.CopyTo(bytes.AsSpan(offset));
            offset += memberName.Length;
        }

        signer.IdentityKey.Bytes.CopyTo(bytes.AsSpan(offset));
        offset += Ed25519PublicKey.Length;
        signer.IdentityKeyPair.Sign(SignedMessage(bytes.AsSpan(0, offset)), bytes.AsSpan(offset));

        return new GroupRecord(bytes, groupId, epoch, name, sorted.AsReadOnly(), signer.IdentityKey);
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out GroupRecord? record)
    {
        record = null;
        if (bytes.Length is < MinLength or > MaxLength || bytes[0] != Version)
        {
            return false;
        }

        uint epochValue = BinaryPrimitives.ReadUInt32BigEndian(bytes[EpochOffset..]);
        int nameLength = bytes[NameLengthOffset];
        if (epochValue is 0 or > MaxEpoch
            || nameLength is 0 or > DisplayName.MaxUtf8Length
            || NameOffset + nameLength + 1 > bytes.Length
            || !DisplayName.TryParse(bytes.Slice(NameOffset, nameLength), out DisplayName? name)
            || !GroupId.TryCreate(bytes.Slice(GroupIdOffset, GroupId.Length), out GroupId? groupId))
        {
            return false;
        }

        int offset = NameOffset + nameLength;
        int count = bytes[offset++];
        if (count is < MinMembers or > MaxMembers)
        {
            return false;
        }

        List<GroupMember> members = new(count);
        for (int index = 0; index < count; index++)
        {
            if (offset + Ed25519PublicKey.Length + 1 > bytes.Length
                || !Ed25519PublicKey.TryCreate(bytes.Slice(offset, Ed25519PublicKey.Length), out Ed25519PublicKey? memberKey))
            {
                return false;
            }

            offset += Ed25519PublicKey.Length;
            int memberNameLength = bytes[offset++];
            if (memberNameLength is 0 or > DisplayName.MaxUtf8Length
                || offset + memberNameLength > bytes.Length
                || !DisplayName.TryParse(bytes.Slice(offset, memberNameLength), out DisplayName? memberName)
                || (members.Count > 0 && Compare(members[^1].IdentityKey, memberKey) >= 0))
            {
                return false;
            }

            offset += memberNameLength;
            members.Add(new GroupMember(memberKey, memberName));
        }

        if (bytes.Length != offset + Ed25519PublicKey.Length + SignatureLength
            || !Ed25519PublicKey.TryCreate(bytes.Slice(offset, Ed25519PublicKey.Length), out Ed25519PublicKey? signer)
            || !members.Exists(member => member.IdentityKey.Equals(signer)))
        {
            return false;
        }

        offset += Ed25519PublicKey.Length;
        if (!signer.Verify(SignedMessage(bytes[..offset]), bytes[offset..]))
        {
            return false;
        }

        record = new GroupRecord(bytes.ToArray(), groupId, (int)epochValue, name, members.AsReadOnly(), signer);
        return true;
    }

    public bool HasMember(Ed25519PublicKey identityKey)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        foreach (GroupMember member in Members)
        {
            if (member.IdentityKey.Equals(identityKey))
            {
                return true;
            }
        }

        return false;
    }

    public bool Fits(GroupKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return CryptographicOperations.FixedTimeEquals(KeyCheck, key.Check);
    }

    public byte[] ToBytes() => bytes.ToArray();

    public bool Equals(GroupRecord? other) => other is not null && bytes.AsSpan().SequenceEqual(other.bytes);

    public override bool Equals(object? obj) => obj is GroupRecord other && Equals(other);

    public override int GetHashCode()
    {
        var code = new HashCode();
        code.AddBytes(hash);
        return code.ToHashCode();
    }

    internal static byte[] HashOf(ReadOnlySpan<byte> body)
    {
        byte[] input = new byte[HashLabel.Length + body.Length];
        HashLabel.CopyTo(input);
        body.CopyTo(input.AsSpan(HashLabel.Length));
        return Digests.Blake2b256(input);
    }

    internal static int Compare(Ed25519PublicKey left, Ed25519PublicKey right) => left.Bytes.SequenceCompareTo(right.Bytes);

    private static byte[] SignedMessage(ReadOnlySpan<byte> body)
    {
        byte[] message = new byte[SignatureContext.Length + body.Length];
        SignatureContext.CopyTo(message);
        body.CopyTo(message.AsSpan(SignatureContext.Length));
        return message;
    }
}
