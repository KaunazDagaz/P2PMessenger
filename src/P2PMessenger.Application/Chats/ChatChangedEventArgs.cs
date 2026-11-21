using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Application.Chats;

public sealed class ChatChangedEventArgs : EventArgs
{
    public ChatChangedEventArgs(Ed25519PublicKey contactKey, ChatMessage? added)
        : this(contactKey, added, null)
    {
    }

    public ChatChangedEventArgs(Ed25519PublicKey contactKey, ChatMessage? added, ChatMessage? updated)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        ContactKey = contactKey;
        Added = added;
        Updated = updated;
    }

    public Ed25519PublicKey ContactKey { get; }

    public ChatMessage? Added { get; }

    public ChatMessage? Updated { get; }
}
