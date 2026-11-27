using System.Buffers.Binary;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Groups;

public static class GroupNotices
{
    private static ReadOnlySpan<byte> InvitationLabel => "P2PM/group-invitation-id/v1"u8;

    private static ReadOnlySpan<byte> JoinLabel => "P2PM/group-join-id/v1"u8;

    private static ReadOnlySpan<byte> AcknowledgementLabel => "P2PM/group-ack-id/v1"u8;

    private static ReadOnlySpan<byte> UpdateLabel => "P2PM/group-update-id/v1"u8;

    private static ReadOnlySpan<byte> LeaveLabel => "P2PM/group-leave-id/v1"u8;

    private static ReadOnlySpan<byte> RemovedLabel => "P2PM/group-removed-id/v1"u8;

    public static MessageId InvitationId(GroupRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return Derive(InvitationLabel, record.Hash, []);
    }

    public static MessageId UpdateId(GroupRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return Derive(UpdateLabel, record.Hash, []);
    }

    public static MessageId JoinId(ReadOnlySpan<byte> recordHash, Ed25519PublicKey joiner)
    {
        ArgumentNullException.ThrowIfNull(joiner);
        if (recordHash.Length != GroupRecord.HashLength)
        {
            throw new ArgumentException("A record hash has 32 bytes.", nameof(recordHash));
        }

        return Derive(JoinLabel, recordHash, joiner.Bytes);
    }

    public static MessageId AcknowledgementId(GroupId groupId, int epoch, MessageId messageId, Ed25519PublicKey recipientKey)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(messageId);
        ArgumentNullException.ThrowIfNull(recipientKey);
        ArgumentOutOfRangeException.ThrowIfLessThan(epoch, 1);

        byte[] input = new byte[AcknowledgementLabel.Length + GroupId.Length + sizeof(uint) + MessageId.Length + Ed25519PublicKey.Length];
        int offset = 0;
        AcknowledgementLabel.CopyTo(input);
        offset += AcknowledgementLabel.Length;
        groupId.Bytes.CopyTo(input.AsSpan(offset));
        offset += GroupId.Length;
        BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(offset), (uint)epoch);
        offset += sizeof(uint);
        messageId.Bytes.CopyTo(input.AsSpan(offset));
        offset += MessageId.Length;
        recipientKey.Bytes.CopyTo(input.AsSpan(offset));
        return MessageId.TryCreate(Digests.Blake2b256(input).AsSpan(0, MessageId.Length), out MessageId? id)
            ? id
            : throw new InvalidOperationException("A derived notice ID has 16 bytes.");
    }

    public static MessageId LeaveId(GroupId groupId, int epoch, Ed25519PublicKey memberKey, Ed25519PublicKey? successorKey = null)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(memberKey);
        ArgumentOutOfRangeException.ThrowIfLessThan(epoch, 1);
        if (successorKey is not null && successorKey.Equals(memberKey))
        {
            throw new ArgumentException("A leaving member cannot name themselves as successor.", nameof(successorKey));
        }

        int extra = successorKey is null ? 1 : 1 + Ed25519PublicKey.Length;
        byte[] input = new byte[LeaveLabel.Length + GroupId.Length + sizeof(uint) + Ed25519PublicKey.Length + extra];
        int offset = 0;
        LeaveLabel.CopyTo(input);
        offset += LeaveLabel.Length;
        groupId.Bytes.CopyTo(input.AsSpan(offset));
        offset += GroupId.Length;
        BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(offset), (uint)epoch);
        offset += sizeof(uint);
        memberKey.Bytes.CopyTo(input.AsSpan(offset));
        offset += Ed25519PublicKey.Length;
        if (successorKey is null)
        {
            input[offset] = 0;
        }
        else
        {
            input[offset] = 1;
            successorKey.Bytes.CopyTo(input.AsSpan(offset + 1));
        }

        return MessageId.TryCreate(Digests.Blake2b256(input).AsSpan(0, MessageId.Length), out MessageId? id)
            ? id
            : throw new InvalidOperationException("A derived notice ID has 16 bytes.");
    }

    public static MessageId RemovedId(GroupId groupId, int newEpoch, Ed25519PublicKey removedMemberKey, Ed25519PublicKey signerKey)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(removedMemberKey);
        ArgumentNullException.ThrowIfNull(signerKey);
        ArgumentOutOfRangeException.ThrowIfLessThan(newEpoch, 2);
        if (removedMemberKey.Equals(signerKey))
        {
            throw new ArgumentException("A signer cannot remove themselves.", nameof(removedMemberKey));
        }

        byte[] input = new byte[RemovedLabel.Length + GroupId.Length + sizeof(uint) + Ed25519PublicKey.Length + Ed25519PublicKey.Length];
        int offset = 0;
        RemovedLabel.CopyTo(input);
        offset += RemovedLabel.Length;
        groupId.Bytes.CopyTo(input.AsSpan(offset));
        offset += GroupId.Length;
        BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(offset), (uint)newEpoch);
        offset += sizeof(uint);
        removedMemberKey.Bytes.CopyTo(input.AsSpan(offset));
        offset += Ed25519PublicKey.Length;
        signerKey.Bytes.CopyTo(input.AsSpan(offset));
        return MessageId.TryCreate(Digests.Blake2b256(input).AsSpan(0, MessageId.Length), out MessageId? id)
            ? id
            : throw new InvalidOperationException("A derived notice ID has 16 bytes.");
    }

    private static MessageId Derive(ReadOnlySpan<byte> label, ReadOnlySpan<byte> recordHash, ReadOnlySpan<byte> extra)
    {
        byte[] input = new byte[label.Length + recordHash.Length + extra.Length];
        label.CopyTo(input);
        recordHash.CopyTo(input.AsSpan(label.Length));
        extra.CopyTo(input.AsSpan(label.Length + recordHash.Length));
        return MessageId.TryCreate(Digests.Blake2b256(input).AsSpan(0, MessageId.Length), out MessageId? id)
            ? id
            : throw new InvalidOperationException("A derived notice ID has 16 bytes.");
    }
}
