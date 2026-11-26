using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Unicode;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Copy;

public sealed class CopyConversationMessage : CopyMessage, IEquatable<CopyConversationMessage>
{
    public const int MaxUtf8Length = TextMessage.MaxUtf8Length;
    public const long MaxWrittenAtMilliseconds = TextMessage.MaxWrittenAtMilliseconds;

    private const int WrittenByOwnerOffset = sizeof(int);
    private const int AuthorNameLengthOffset = WrittenByOwnerOffset + 1;
    private const int AuthorNameOffset = AuthorNameLengthOffset + 1;
    private const int MinBodyLength = AuthorNameOffset + sizeof(long) + 1;

    private readonly byte[] utf8;

    private CopyConversationMessage(int position, bool writtenByOwner, DisplayName? authorName, long writtenAtMilliseconds, string text, byte[] utf8)
    {
        Position = position;
        WrittenByOwner = writtenByOwner;
        AuthorName = authorName;
        WrittenAt = DateTimeOffset.FromUnixTimeMilliseconds(writtenAtMilliseconds);
        Text = text;
        this.utf8 = utf8;
    }

    public int Position { get; }

    public bool WrittenByOwner { get; }

    public DisplayName? AuthorName { get; }

    public DateTimeOffset WrittenAt { get; }

    public string Text { get; }

    public int ContentBytes => utf8.Length + (AuthorName?.Utf8Bytes.Length ?? 0);

    public override CopyMessageType Type => CopyMessageType.ConversationMessage;

    internal override int BodyLength => AuthorNameOffset + (AuthorName?.Utf8Bytes.Length ?? 0) + sizeof(long) + utf8.Length;

    public static int MeasureContentBytes(DisplayName? authorName, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return (authorName?.Utf8Bytes.Length ?? 0) + Encoding.UTF8.GetByteCount(text);
    }

    public static bool TryCreate(
        int position,
        bool writtenByOwner,
        DisplayName? authorName,
        DateTimeOffset writtenAt,
        string? text,
        [NotNullWhen(true)] out CopyConversationMessage? message)
    {
        message = null;
        if (position is < 1 or > CopyConversationStart.MaxMessages)
        {
            return false;
        }

        if (writtenByOwner ? authorName is not null : authorName is null)
        {
            return false;
        }

        long milliseconds = writtenAt.ToUnixTimeMilliseconds();
        if (milliseconds is < 0 or > MaxWrittenAtMilliseconds || text is null || !TryEncode(text, out byte[]? encoded))
        {
            return false;
        }

        message = new CopyConversationMessage(position, writtenByOwner, authorName, milliseconds, text, encoded);
        return true;
    }

    public bool Equals(CopyConversationMessage? other) =>
        other is not null
        && Position == other.Position
        && WrittenByOwner == other.WrittenByOwner
        && EqualityComparer<DisplayName?>.Default.Equals(AuthorName, other.AuthorName)
        && WrittenAt.Equals(other.WrittenAt)
        && string.Equals(Text, other.Text, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is CopyConversationMessage other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Position, WrittenByOwner, AuthorName, WrittenAt, Text.GetHashCode(StringComparison.Ordinal));

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out CopyConversationMessage? message)
    {
        message = null;
        if (body.Length < MinBodyLength)
        {
            return false;
        }

        int position = BinaryPrimitives.ReadInt32BigEndian(body[..WrittenByOwnerOffset]);
        byte ownerFlag = body[WrittenByOwnerOffset];
        int authorLength = body[AuthorNameLengthOffset];
        if (position is < 1 or > CopyConversationStart.MaxMessages || ownerFlag is > 1)
        {
            return false;
        }

        bool writtenByOwner = ownerFlag == 1;
        if (writtenByOwner ? authorLength != 0 : authorLength is 0 or > DisplayName.MaxUtf8Length)
        {
            return false;
        }

        int writtenAtOffset = AuthorNameOffset + authorLength;
        int textOffset = writtenAtOffset + sizeof(long);
        if (body.Length <= textOffset || body.Length - textOffset > MaxUtf8Length)
        {
            return false;
        }

        DisplayName? authorName = null;
        if (!writtenByOwner && !DisplayName.TryParse(body.Slice(AuthorNameOffset, authorLength), out authorName))
        {
            return false;
        }

        long milliseconds = BinaryPrimitives.ReadInt64BigEndian(body.Slice(writtenAtOffset, sizeof(long)));
        ReadOnlySpan<byte> textBytes = body[textOffset..];
        if (milliseconds is < 0 or > MaxWrittenAtMilliseconds || !Utf8.IsValid(textBytes))
        {
            return false;
        }

        message = new CopyConversationMessage(position, writtenByOwner, authorName, milliseconds, Encoding.UTF8.GetString(textBytes), textBytes.ToArray());
        return true;
    }

    internal override void WriteBody(Span<byte> body)
    {
        BinaryPrimitives.WriteInt32BigEndian(body[..WrittenByOwnerOffset], Position);
        body[WrittenByOwnerOffset] = WrittenByOwner ? (byte)1 : (byte)0;
        int offset = AuthorNameOffset;
        if (AuthorName is not null)
        {
            ReadOnlySpan<byte> authorBytes = AuthorName.Utf8Bytes;
            body[AuthorNameLengthOffset] = (byte)authorBytes.Length;
            authorBytes.CopyTo(body[offset..]);
            offset += authorBytes.Length;
        }
        else
        {
            body[AuthorNameLengthOffset] = 0;
        }

        BinaryPrimitives.WriteInt64BigEndian(body.Slice(offset, sizeof(long)), WrittenAt.ToUnixTimeMilliseconds());
        utf8.CopyTo(body[(offset + sizeof(long))..]);
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
