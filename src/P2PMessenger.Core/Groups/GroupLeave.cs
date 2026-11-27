using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Groups;

public sealed class GroupLeave : SessionMessage, IEquatable<GroupLeave>
{
    internal const int EpochOffset = GroupId.Length;
    internal const int MemberOffset = EpochOffset + sizeof(uint);
    internal const int HasSuccessorOffset = MemberOffset + Ed25519PublicKey.Length;
    internal const int SuccessorOffset = HasSuccessorOffset + 1;
    internal const int SignatureLength = Ed25519KeyPair.SignatureLength;

    public const int MinLength = SuccessorOffset + SignatureLength;
    public const int MaxLength = SuccessorOffset + Ed25519PublicKey.Length + SignatureLength;

    private readonly byte[] encoded;

    private GroupLeave(byte[] encoded, GroupId groupId, int epoch, Ed25519PublicKey memberKey, Ed25519PublicKey? successorKey)
    {
        this.encoded = encoded;
        GroupId = groupId;
        Epoch = epoch;
        MemberKey = memberKey;
        SuccessorKey = successorKey;
        NoticeId = GroupNotices.LeaveId(groupId, epoch, memberKey, successorKey);
    }

    public GroupId GroupId { get; }

    public int Epoch { get; }

    public Ed25519PublicKey MemberKey { get; }

    public Ed25519PublicKey? SuccessorKey { get; }

    public bool HasSuccessor => SuccessorKey is not null;

    public MessageId NoticeId { get; }

    public override SessionMessageType Type => SessionMessageType.GroupLeave;

    internal override int BodyLength => encoded.Length;

    private static ReadOnlySpan<byte> SignatureContext => "P2PM/group-leave/v1"u8;

    public static GroupLeave Create(GroupId groupId, int epoch, KeySet member, Ed25519PublicKey? successorKey = null)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentOutOfRangeException.ThrowIfLessThan(epoch, 1);
        if (successorKey is not null && successorKey.Equals(member.IdentityKey))
        {
            throw new ArgumentException("A leaving member cannot name themselves as successor.", nameof(successorKey));
        }

        int signedLength = successorKey is null ? SuccessorOffset : SuccessorOffset + Ed25519PublicKey.Length;
        byte[] bytes = new byte[signedLength + SignatureLength];
        groupId.Bytes.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(EpochOffset), (uint)epoch);
        member.IdentityKey.Bytes.CopyTo(bytes.AsSpan(MemberOffset));
        if (successorKey is null)
        {
            bytes[HasSuccessorOffset] = 0;
        }
        else
        {
            bytes[HasSuccessorOffset] = 1;
            successorKey.Bytes.CopyTo(bytes.AsSpan(SuccessorOffset));
        }

        member.IdentityKeyPair.Sign(SignedMessage(bytes.AsSpan(0, signedLength)), bytes.AsSpan(signedLength));
        return new GroupLeave(bytes, groupId, epoch, member.IdentityKey, successorKey);
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out GroupLeave? leave)
    {
        leave = null;
        if (bytes.Length < MinLength)
        {
            return false;
        }

        byte hasSuccessor = bytes[HasSuccessorOffset];
        int expectedLength = hasSuccessor switch
        {
            0 => MinLength,
            1 => MaxLength,
            _ => -1,
        };
        if (bytes.Length != expectedLength)
        {
            return false;
        }

        uint epoch = BinaryPrimitives.ReadUInt32BigEndian(bytes[EpochOffset..]);
        if (epoch is 0 or > GroupRecord.MaxEpoch
            || !GroupId.TryCreate(bytes[..GroupId.Length], out GroupId? groupId)
            || !Ed25519PublicKey.TryCreate(bytes.Slice(MemberOffset, Ed25519PublicKey.Length), out Ed25519PublicKey? memberKey))
        {
            return false;
        }

        Ed25519PublicKey? successorKey = null;
        if (hasSuccessor == 1)
        {
            if (!Ed25519PublicKey.TryCreate(bytes.Slice(SuccessorOffset, Ed25519PublicKey.Length), out successorKey)
                || successorKey.Equals(memberKey))
            {
                return false;
            }
        }

        leave = new GroupLeave(bytes.ToArray(), groupId, (int)epoch, memberKey, successorKey);
        return true;
    }

    public bool HasValidSignature()
    {
        int signedLength = encoded.Length - SignatureLength;
        return MemberKey.Verify(SignedMessage(encoded.AsSpan(0, signedLength)), encoded.AsSpan(signedLength));
    }

    public byte[] ToBytes() => encoded.ToArray();

    public bool Equals(GroupLeave? other) =>
        other is not null && encoded.AsSpan().SequenceEqual(other.encoded);

    public override bool Equals(object? obj) => obj is GroupLeave other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(encoded);
        return hash.ToHashCode();
    }

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out GroupLeave? leave) =>
        TryParse(body, out leave);

    internal override void WriteBody(Span<byte> body) => encoded.CopyTo(body);

    private static byte[] SignedMessage(ReadOnlySpan<byte> body)
    {
        byte[] message = new byte[SignatureContext.Length + body.Length];
        SignatureContext.CopyTo(message);
        body.CopyTo(message.AsSpan(SignatureContext.Length));
        return message;
    }
}
