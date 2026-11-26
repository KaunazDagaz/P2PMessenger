using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Groups;

public static class GroupNotices
{
    private static ReadOnlySpan<byte> InvitationLabel => "P2PM/group-invitation-id/v1"u8;

    private static ReadOnlySpan<byte> JoinLabel => "P2PM/group-join-id/v1"u8;

    public static MessageId InvitationId(GroupRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return Derive(InvitationLabel, record.Hash, []);
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
