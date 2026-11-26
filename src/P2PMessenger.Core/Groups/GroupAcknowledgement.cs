using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Groups;

public sealed class GroupAcknowledgement : SessionMessage, IEquatable<GroupAcknowledgement>
{
    internal const int EpochOffset = GroupId.Length;
    internal const int MessageIdOffset = EpochOffset + sizeof(uint);
    internal const int RecipientOffset = MessageIdOffset + MessageId.Length;
    internal const int SignedBodyLength = RecipientOffset + Ed25519PublicKey.Length;
    internal const int SignatureLength = Ed25519KeyPair.SignatureLength;

    public const int Length = SignedBodyLength + SignatureLength;

    private readonly byte[] encoded;

    private GroupAcknowledgement(byte[] encoded, GroupId groupId, int epoch, MessageId messageId, Ed25519PublicKey recipientKey)
    {
        this.encoded = encoded;
        GroupId = groupId;
        Epoch = epoch;
        MessageId = messageId;
        RecipientKey = recipientKey;
        NoticeId = GroupNotices.AcknowledgementId(groupId, epoch, messageId, recipientKey);
    }

    public GroupId GroupId { get; }

    public int Epoch { get; }

    public MessageId MessageId { get; }

    public Ed25519PublicKey RecipientKey { get; }

    public MessageId NoticeId { get; }

    public override SessionMessageType Type => SessionMessageType.GroupAcknowledgement;

    internal override int BodyLength => Length;

    private static ReadOnlySpan<byte> SignatureContext => "P2PM/group-ack/v1"u8;

    public static GroupAcknowledgement Create(GroupId groupId, int epoch, MessageId messageId, KeySet recipient)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(messageId);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentOutOfRangeException.ThrowIfLessThan(epoch, 1);

        byte[] bytes = new byte[Length];
        groupId.Bytes.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(EpochOffset), (uint)epoch);
        messageId.Bytes.CopyTo(bytes.AsSpan(MessageIdOffset));
        recipient.IdentityKey.Bytes.CopyTo(bytes.AsSpan(RecipientOffset));
        recipient.IdentityKeyPair.Sign(SignedMessage(bytes.AsSpan(0, SignedBodyLength)), bytes.AsSpan(SignedBodyLength));
        return new GroupAcknowledgement(bytes, groupId, epoch, messageId, recipient.IdentityKey);
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out GroupAcknowledgement? acknowledgement)
    {
        acknowledgement = null;
        if (bytes.Length != Length)
        {
            return false;
        }

        uint epoch = BinaryPrimitives.ReadUInt32BigEndian(bytes[EpochOffset..]);
        if (epoch is 0 or > GroupRecord.MaxEpoch
            || !GroupId.TryCreate(bytes[..GroupId.Length], out GroupId? groupId)
            || !MessageId.TryCreate(bytes.Slice(MessageIdOffset, MessageId.Length), out MessageId? messageId)
            || !Ed25519PublicKey.TryCreate(bytes.Slice(RecipientOffset, Ed25519PublicKey.Length), out Ed25519PublicKey? recipientKey))
        {
            return false;
        }

        acknowledgement = new GroupAcknowledgement(bytes.ToArray(), groupId, (int)epoch, messageId, recipientKey);
        return true;
    }

    public bool HasValidSignature() =>
        RecipientKey.Verify(SignedMessage(encoded.AsSpan(0, SignedBodyLength)), encoded.AsSpan(SignedBodyLength));

    public byte[] ToBytes() => encoded.ToArray();

    public bool Equals(GroupAcknowledgement? other) =>
        other is not null && encoded.AsSpan().SequenceEqual(other.encoded);

    public override bool Equals(object? obj) => obj is GroupAcknowledgement other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(encoded);
        return hash.ToHashCode();
    }

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out GroupAcknowledgement? acknowledgement) =>
        TryParse(body, out acknowledgement);

    internal override void WriteBody(Span<byte> body) => encoded.CopyTo(body);

    private static byte[] SignedMessage(ReadOnlySpan<byte> body)
    {
        byte[] message = new byte[SignatureContext.Length + body.Length];
        SignatureContext.CopyTo(message);
        body.CopyTo(message.AsSpan(SignatureContext.Length));
        return message;
    }
}
