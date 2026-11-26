using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Groups;

public sealed class GroupMessage : SessionMessage, IEquatable<GroupMessage>
{
    public const int MaxBodyLength = MessageCodec.MaxContentLength - MessageCodec.TypeHeaderLength;

    internal const int EpochOffset = GroupId.Length;
    internal const int AuthorOffset = EpochOffset + sizeof(uint);
    internal const int NonceOffset = AuthorOffset + Ed25519PublicKey.Length;
    internal const int CiphertextOffset = NonceOffset + GroupKey.NonceLength;
    internal const int SignatureLength = Ed25519KeyPair.SignatureLength;
    internal const int FixedEnvelopeLength = CiphertextOffset + GroupKey.TagLength + SignatureLength;

    private readonly byte[] envelope;

    private GroupMessage(byte[] envelope, GroupId groupId, int epoch, Ed25519PublicKey authorKey)
    {
        this.envelope = envelope;
        GroupId = groupId;
        Epoch = epoch;
        AuthorKey = authorKey;
    }

    public GroupId GroupId { get; }

    public int Epoch { get; }

    public Ed25519PublicKey AuthorKey { get; }

    public int Length => envelope.Length;

    public override SessionMessageType Type => SessionMessageType.GroupMessage;

    internal override int BodyLength => envelope.Length;

    private static ReadOnlySpan<byte> TextLabel => "P2PM/group-text/v1"u8;

    private static ReadOnlySpan<byte> SignatureContext => "P2PM/group-message/v1"u8;

    public static GroupMessage Seal(GroupKey key, GroupId groupId, int epoch, KeySet author, GroupText text)
    {
        Span<byte> nonce = stackalloc byte[GroupKey.NonceLength];
        RandomNumberGenerator.Fill(nonce);
        return Seal(key, groupId, epoch, author, text, nonce);
    }

    internal static GroupMessage Seal(GroupKey key, GroupId groupId, int epoch, KeySet author, GroupText text, ReadOnlySpan<byte> nonce)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(epoch, 1);
        if (nonce.Length != GroupKey.NonceLength)
        {
            throw new ArgumentException("A nonce has 24 bytes.", nameof(nonce));
        }

        int plaintextLength = text.Length;
        byte[] envelope = new byte[FixedEnvelopeLength + plaintextLength];
        groupId.Bytes.CopyTo(envelope);
        BinaryPrimitives.WriteUInt32BigEndian(envelope.AsSpan(EpochOffset), (uint)epoch);
        author.IdentityKey.Bytes.CopyTo(envelope.AsSpan(AuthorOffset));
        nonce.CopyTo(envelope.AsSpan(NonceOffset));

        byte[] plaintext = new byte[plaintextLength];
        try
        {
            text.Write(plaintext);
            key.Encrypt(
                envelope.AsSpan(NonceOffset, GroupKey.NonceLength),
                AssociatedData(envelope),
                plaintext,
                envelope.AsSpan(CiphertextOffset, plaintextLength + GroupKey.TagLength));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        int signedLength = envelope.Length - SignatureLength;
        author.IdentityKeyPair.Sign(SignedMessage(envelope.AsSpan(0, signedLength)), envelope.AsSpan(signedLength));
        return new GroupMessage(envelope, groupId, epoch, author.IdentityKey);
    }

    public static bool TryParse(ReadOnlySpan<byte> envelope, [NotNullWhen(true)] out GroupMessage? message)
    {
        message = null;
        if (envelope.Length <= FixedEnvelopeLength + GroupText.FixedLength || envelope.Length > MaxBodyLength)
        {
            return false;
        }

        uint epoch = BinaryPrimitives.ReadUInt32BigEndian(envelope[EpochOffset..]);
        if (epoch is 0 or > GroupRecord.MaxEpoch
            || !GroupId.TryCreate(envelope[..GroupId.Length], out GroupId? groupId)
            || !Ed25519PublicKey.TryCreate(envelope.Slice(AuthorOffset, Ed25519PublicKey.Length), out Ed25519PublicKey? authorKey))
        {
            return false;
        }

        message = new GroupMessage(envelope.ToArray(), groupId, (int)epoch, authorKey);
        return true;
    }

    public bool HasValidSignature()
    {
        int signedLength = envelope.Length - SignatureLength;
        return AuthorKey.Verify(SignedMessage(envelope.AsSpan(0, signedLength)), envelope.AsSpan(signedLength));
    }

    public bool TryOpen(GroupKey key, [NotNullWhen(true)] out GroupText? text)
    {
        ArgumentNullException.ThrowIfNull(key);
        text = null;

        if (!HasValidSignature())
        {
            return false;
        }

        int ciphertextLength = envelope.Length - CiphertextOffset - SignatureLength;
        byte[] plaintext = new byte[ciphertextLength - GroupKey.TagLength];
        try
        {
            return key.TryDecrypt(
                    envelope.AsSpan(NonceOffset, GroupKey.NonceLength),
                    AssociatedData(envelope),
                    envelope.AsSpan(CiphertextOffset, ciphertextLength),
                    plaintext)
                && GroupText.TryDecode(plaintext, out text);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public byte[] ToBytes() => envelope.ToArray();

    public bool Equals(GroupMessage? other) => other is not null && envelope.AsSpan().SequenceEqual(other.envelope);

    public override bool Equals(object? obj) => obj is GroupMessage other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(envelope);
        return hash.ToHashCode();
    }

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out GroupMessage? message) => TryParse(body, out message);

    internal override void WriteBody(Span<byte> body) => envelope.CopyTo(body);

    private static byte[] AssociatedData(ReadOnlySpan<byte> envelope)
    {
        byte[] data = new byte[TextLabel.Length + NonceOffset];
        TextLabel.CopyTo(data);
        envelope[..NonceOffset].CopyTo(data.AsSpan(TextLabel.Length));
        return data;
    }

    private static byte[] SignedMessage(ReadOnlySpan<byte> body)
    {
        byte[] message = new byte[SignatureContext.Length + body.Length];
        SignatureContext.CopyTo(message);
        body.CopyTo(message.AsSpan(SignatureContext.Length));
        return message;
    }
}
