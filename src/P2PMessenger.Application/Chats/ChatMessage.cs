using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Chats;

public sealed class ChatMessage
{
    public ChatMessage(
        Ed25519PublicKey contactKey,
        MessageDirection direction,
        MessageId id,
        long sequence,
        DateTimeOffset writtenAt,
        string text,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(contactKey);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(text);

        ContactKey = contactKey;
        Direction = direction;
        Id = id;
        Sequence = sequence;
        WrittenAt = writtenAt;
        Text = text;
        RecordedAt = recordedAt;
    }

    public Ed25519PublicKey ContactKey { get; }

    public MessageDirection Direction { get; }

    public MessageId Id { get; }

    public long Sequence { get; }

    public DateTimeOffset WrittenAt { get; }

    public string Text { get; }

    public DateTimeOffset RecordedAt { get; }

    internal static ChatMessage From(Ed25519PublicKey contactKey, MessageDirection direction, TextMessage text, DateTimeOffset recordedAt) =>
        new(contactKey, direction, text.Id, text.Sequence, text.WrittenAt, text.Text, recordedAt);
}
