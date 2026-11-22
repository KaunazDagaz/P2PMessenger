using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Application.Chats;

public sealed class ChatChangedEventArgs : EventArgs
{
    public ChatChangedEventArgs(Ed25519PublicKey contactKey, ChatMessage? added)
        : this(contactKey, added, null)
    {
    }

    public ChatChangedEventArgs(Ed25519PublicKey contactKey, ChatMessage? added, ChatMessage? updated)
        : this(contactKey, added, updated, null, false)
    {
    }

    private ChatChangedEventArgs(Ed25519PublicKey contactKey, ChatMessage? added, ChatMessage? updated, ChatMessage? removed, bool isCleared)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        ContactKey = contactKey;
        Added = added;
        Updated = updated;
        Removed = removed;
        IsCleared = isCleared;
    }

    public Ed25519PublicKey ContactKey { get; }

    public ChatMessage? Added { get; }

    public ChatMessage? Updated { get; }

    public ChatMessage? Removed { get; }

    public bool IsCleared { get; }

    public static ChatChangedEventArgs ForRemoved(ChatMessage removed)
    {
        ArgumentNullException.ThrowIfNull(removed);

        return new ChatChangedEventArgs(removed.ContactKey, null, null, removed, false);
    }

    public static ChatChangedEventArgs ForCleared(Ed25519PublicKey contactKey) => new(contactKey, null, null, null, true);
}
