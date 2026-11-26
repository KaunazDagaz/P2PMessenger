using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Core.Copy;

public sealed class CopyConversationStart : CopyMessage, IEquatable<CopyConversationStart>
{
    public const int MaxMessages = 100_000;

    private const int TitleLengthOffset = CopyOriginId.Length;
    private const int TitleOffset = TitleLengthOffset + 1;
    private const int FixedLength = TitleOffset + sizeof(int);

    private CopyConversationStart(CopyOriginId origin, DisplayName title, int messageCount)
    {
        Origin = origin;
        Title = title;
        MessageCount = messageCount;
    }

    public CopyOriginId Origin { get; }

    public DisplayName Title { get; }

    public int MessageCount { get; }

    public override CopyMessageType Type => CopyMessageType.ConversationStart;

    internal override int BodyLength => FixedLength + Title.Utf8Bytes.Length;

    public static bool TryCreate(CopyOriginId origin, DisplayName title, int messageCount, [NotNullWhen(true)] out CopyConversationStart? message)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(title);
        if (messageCount is < 0 or > MaxMessages)
        {
            message = null;
            return false;
        }

        message = new CopyConversationStart(origin, title, messageCount);
        return true;
    }

    public bool Equals(CopyConversationStart? other) =>
        other is not null && Origin.Equals(other.Origin) && Title.Equals(other.Title) && MessageCount == other.MessageCount;

    public override bool Equals(object? obj) => obj is CopyConversationStart other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Origin, Title, MessageCount);

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out CopyConversationStart? message)
    {
        message = null;
        if (body.Length < FixedLength + 1 || body.Length > FixedLength + DisplayName.MaxUtf8Length)
        {
            return false;
        }

        int titleLength = body[TitleLengthOffset];
        if (titleLength is 0 or > DisplayName.MaxUtf8Length || body.Length != FixedLength + titleLength)
        {
            return false;
        }

        int messageCount = BinaryPrimitives.ReadInt32BigEndian(body.Slice(TitleOffset + titleLength, sizeof(int)));
        if (messageCount is < 0 or > MaxMessages
            || !CopyOriginId.TryCreate(body[..CopyOriginId.Length], out CopyOriginId? origin)
            || !DisplayName.TryParse(body.Slice(TitleOffset, titleLength), out DisplayName? title))
        {
            return false;
        }

        message = new CopyConversationStart(origin, title, messageCount);
        return true;
    }

    internal override void WriteBody(Span<byte> body)
    {
        Origin.Bytes.CopyTo(body);
        ReadOnlySpan<byte> titleBytes = Title.Utf8Bytes;
        body[TitleLengthOffset] = (byte)titleBytes.Length;
        titleBytes.CopyTo(body[TitleOffset..]);
        BinaryPrimitives.WriteInt32BigEndian(body[(TitleOffset + titleBytes.Length)..], MessageCount);
    }
}
