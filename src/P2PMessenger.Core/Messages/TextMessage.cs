using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Unicode;

namespace P2PMessenger.Core.Messages;

public sealed class TextMessage : SessionMessage, IEquatable<TextMessage>
{
    public const int MaxUtf8Length = MessageCodec.MaxContentLength - MessageCodec.TypeHeaderLength - FixedBodyLength;
    public const long MaxWrittenAtMilliseconds = 253_402_300_799_999;

    private const int SequenceOffset = MessageId.Length;
    private const int WrittenAtOffset = SequenceOffset + sizeof(long);
    private const int FixedBodyLength = WrittenAtOffset + sizeof(long);

    private readonly byte[] _utf8;

    private TextMessage(MessageId id, long sequence, long writtenAtMilliseconds, string text, byte[] utf8)
    {
        Id = id;
        Sequence = sequence;
        WrittenAt = DateTimeOffset.FromUnixTimeMilliseconds(writtenAtMilliseconds);
        Text = text;
        _utf8 = utf8;
    }

    public MessageId Id { get; }

    public long Sequence { get; }

    public DateTimeOffset WrittenAt { get; }

    public string Text { get; }

    public override SessionMessageType Type => SessionMessageType.Text;

    internal override int BodyLength => FixedBodyLength + _utf8.Length;

    public static bool TryCreate(MessageId id, long sequence, DateTimeOffset writtenAt, string? text, [NotNullWhen(true)] out TextMessage? message)
    {
        ArgumentNullException.ThrowIfNull(id);
        message = null;

        long milliseconds = writtenAt.ToUnixTimeMilliseconds();
        if (sequence < 1 || milliseconds is < 0 or > MaxWrittenAtMilliseconds || text is null || !TryEncode(text, out byte[]? utf8))
        {
            return false;
        }

        message = new TextMessage(id, sequence, milliseconds, text, utf8);
        return true;
    }

    public bool Equals(TextMessage? other) =>
        other is not null
        && Id.Equals(other.Id)
        && Sequence == other.Sequence
        && WrittenAt.Equals(other.WrittenAt)
        && string.Equals(Text, other.Text, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is TextMessage other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, Sequence, WrittenAt, Text.GetHashCode(StringComparison.Ordinal));

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out TextMessage? message)
    {
        message = null;
        if (body.Length <= FixedBodyLength || body.Length > FixedBodyLength + MaxUtf8Length)
        {
            return false;
        }

        long sequence = BinaryPrimitives.ReadInt64BigEndian(body[SequenceOffset..WrittenAtOffset]);
        long milliseconds = BinaryPrimitives.ReadInt64BigEndian(body[WrittenAtOffset..FixedBodyLength]);
        ReadOnlySpan<byte> utf8 = body[FixedBodyLength..];
        if (sequence < 1
            || milliseconds is < 0 or > MaxWrittenAtMilliseconds
            || !Utf8.IsValid(utf8)
            || !MessageId.TryCreate(body[..MessageId.Length], out MessageId? id))
        {
            return false;
        }

        message = new TextMessage(id, sequence, milliseconds, Encoding.UTF8.GetString(utf8), utf8.ToArray());
        return true;
    }

    internal override void WriteBody(Span<byte> body)
    {
        Id.Bytes.CopyTo(body);
        BinaryPrimitives.WriteInt64BigEndian(body[SequenceOffset..WrittenAtOffset], Sequence);
        BinaryPrimitives.WriteInt64BigEndian(body[WrittenAtOffset..FixedBodyLength], WrittenAt.ToUnixTimeMilliseconds());
        _utf8.CopyTo(body[FixedBodyLength..]);
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
