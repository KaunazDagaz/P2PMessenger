using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Unicode;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Groups;

public sealed class GroupText : IEquatable<GroupText>
{
    public const int MaxUtf8Length = GroupMessage.MaxBodyLength - GroupMessage.FixedEnvelopeLength - FixedLength;

    internal const int FixedLength = WrittenAtOffset + sizeof(long);

    private const int SequenceOffset = MessageId.Length;
    private const int WrittenAtOffset = SequenceOffset + sizeof(long);

    private readonly byte[] utf8;

    private GroupText(MessageId id, long sequence, long writtenAtMilliseconds, string text, byte[] utf8)
    {
        Id = id;
        Sequence = sequence;
        WrittenAt = DateTimeOffset.FromUnixTimeMilliseconds(writtenAtMilliseconds);
        Text = text;
        this.utf8 = utf8;
    }

    public MessageId Id { get; }

    public long Sequence { get; }

    public DateTimeOffset WrittenAt { get; }

    public string Text { get; }

    internal int Length => FixedLength + utf8.Length;

    public static bool TryCreate(MessageId id, long sequence, DateTimeOffset writtenAt, string? text, [NotNullWhen(true)] out GroupText? groupText)
    {
        ArgumentNullException.ThrowIfNull(id);
        groupText = null;

        long milliseconds = writtenAt.ToUnixTimeMilliseconds();
        if (sequence < 1 || milliseconds is < 0 or > TextMessage.MaxWrittenAtMilliseconds || text is null || !TryEncode(text, out byte[]? utf8))
        {
            return false;
        }

        groupText = new GroupText(id, sequence, milliseconds, text, utf8);
        return true;
    }

    public bool Equals(GroupText? other) =>
        other is not null
        && Id.Equals(other.Id)
        && Sequence == other.Sequence
        && WrittenAt.Equals(other.WrittenAt)
        && string.Equals(Text, other.Text, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is GroupText other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, Sequence, WrittenAt, Text.GetHashCode(StringComparison.Ordinal));

    internal static bool TryDecode(ReadOnlySpan<byte> plaintext, [NotNullWhen(true)] out GroupText? groupText)
    {
        groupText = null;
        if (plaintext.Length <= FixedLength || plaintext.Length > FixedLength + MaxUtf8Length)
        {
            return false;
        }

        long sequence = BinaryPrimitives.ReadInt64BigEndian(plaintext[SequenceOffset..WrittenAtOffset]);
        long milliseconds = BinaryPrimitives.ReadInt64BigEndian(plaintext[WrittenAtOffset..FixedLength]);
        ReadOnlySpan<byte> utf8 = plaintext[FixedLength..];
        if (sequence < 1
            || milliseconds is < 0 or > TextMessage.MaxWrittenAtMilliseconds
            || !Utf8.IsValid(utf8)
            || !MessageId.TryCreate(plaintext[..MessageId.Length], out MessageId? id))
        {
            return false;
        }

        groupText = new GroupText(id, sequence, milliseconds, Encoding.UTF8.GetString(utf8), utf8.ToArray());
        return true;
    }

    internal void Write(Span<byte> destination)
    {
        Id.Bytes.CopyTo(destination);
        BinaryPrimitives.WriteInt64BigEndian(destination[SequenceOffset..WrittenAtOffset], Sequence);
        BinaryPrimitives.WriteInt64BigEndian(destination[WrittenAtOffset..FixedLength], WrittenAt.ToUnixTimeMilliseconds());
        utf8.CopyTo(destination[FixedLength..]);
    }

    private static bool TryEncode(string text, [NotNullWhen(true)] out byte[]? utf8)
    {
        utf8 = null;
        if (text.Length is 0 or > MaxUtf8Length)
        {
            return false;
        }

        byte[] buffer = new byte[Encoding.UTF8.GetMaxByteCount(text.Length)];
        OperationStatus status = Utf8.FromUtf16(text, buffer, out _, out int written, replaceInvalidSequences: false);
        if (status != OperationStatus.Done || written > MaxUtf8Length)
        {
            return false;
        }

        utf8 = buffer[..written];
        return true;
    }
}
