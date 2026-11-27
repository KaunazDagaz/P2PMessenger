using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Groups;

public sealed class GroupRemoved : SessionMessage, IEquatable<GroupRemoved>
{
    internal const int EpochOffset = GroupId.Length;
    internal const int RemovedMemberOffset = EpochOffset + sizeof(uint);
    internal const int SignerOffset = RemovedMemberOffset + Ed25519PublicKey.Length;
    internal const int SignedBodyLength = SignerOffset + Ed25519PublicKey.Length;
    internal const int SignatureLength = Ed25519KeyPair.SignatureLength;

    public const int Length = SignedBodyLength + SignatureLength;

    private readonly byte[] encoded;

    private GroupRemoved(byte[] encoded, GroupId groupId, int newEpoch, Ed25519PublicKey removedMemberKey, Ed25519PublicKey signerKey)
    {
        this.encoded = encoded;
        GroupId = groupId;
        NewEpoch = newEpoch;
        RemovedMemberKey = removedMemberKey;
        SignerKey = signerKey;
        NoticeId = GroupNotices.RemovedId(groupId, newEpoch, removedMemberKey, signerKey);
    }

    public GroupId GroupId { get; }

    public int NewEpoch { get; }

    public Ed25519PublicKey RemovedMemberKey { get; }

    public Ed25519PublicKey SignerKey { get; }

    public MessageId NoticeId { get; }

    public override SessionMessageType Type => SessionMessageType.GroupRemoved;

    internal override int BodyLength => Length;

    private static ReadOnlySpan<byte> SignatureContext => "P2PM/group-removed/v1"u8;

    public static GroupRemoved Create(GroupId groupId, int newEpoch, Ed25519PublicKey removedMemberKey, KeySet signer)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(removedMemberKey);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentOutOfRangeException.ThrowIfLessThan(newEpoch, 2);
        if (removedMemberKey.Equals(signer.IdentityKey))
        {
            throw new ArgumentException("A signer cannot remove themselves.", nameof(removedMemberKey));
        }

        byte[] bytes = new byte[Length];
        groupId.Bytes.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(EpochOffset), (uint)newEpoch);
        removedMemberKey.Bytes.CopyTo(bytes.AsSpan(RemovedMemberOffset));
        signer.IdentityKey.Bytes.CopyTo(bytes.AsSpan(SignerOffset));
        signer.IdentityKeyPair.Sign(SignedMessage(bytes.AsSpan(0, SignedBodyLength)), bytes.AsSpan(SignedBodyLength));
        return new GroupRemoved(bytes, groupId, newEpoch, removedMemberKey, signer.IdentityKey);
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out GroupRemoved? removed)
    {
        removed = null;
        if (bytes.Length != Length)
        {
            return false;
        }

        uint newEpoch = BinaryPrimitives.ReadUInt32BigEndian(bytes[EpochOffset..]);
        if (newEpoch is < 2 or > GroupRecord.MaxEpoch
            || !GroupId.TryCreate(bytes[..GroupId.Length], out GroupId? groupId)
            || !Ed25519PublicKey.TryCreate(bytes.Slice(RemovedMemberOffset, Ed25519PublicKey.Length), out Ed25519PublicKey? removedMemberKey)
            || !Ed25519PublicKey.TryCreate(bytes.Slice(SignerOffset, Ed25519PublicKey.Length), out Ed25519PublicKey? signerKey)
            || removedMemberKey.Equals(signerKey))
        {
            return false;
        }

        removed = new GroupRemoved(bytes.ToArray(), groupId, (int)newEpoch, removedMemberKey, signerKey);
        return true;
    }

    public bool HasValidSignature() =>
        SignerKey.Verify(SignedMessage(encoded.AsSpan(0, SignedBodyLength)), encoded.AsSpan(SignedBodyLength));

    public byte[] ToBytes() => encoded.ToArray();

    public bool Equals(GroupRemoved? other) =>
        other is not null && encoded.AsSpan().SequenceEqual(other.encoded);

    public override bool Equals(object? obj) => obj is GroupRemoved other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(encoded);
        return hash.ToHashCode();
    }

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out GroupRemoved? removed) =>
        TryParse(body, out removed);

    internal override void WriteBody(Span<byte> body) => encoded.CopyTo(body);

    private static byte[] SignedMessage(ReadOnlySpan<byte> body)
    {
        byte[] message = new byte[SignatureContext.Length + body.Length];
        SignatureContext.CopyTo(message);
        body.CopyTo(message.AsSpan(SignatureContext.Length));
        return message;
    }
}
