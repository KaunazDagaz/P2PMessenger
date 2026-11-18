using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Application.Chats;

public sealed class ChatChangedEventArgs : EventArgs
{
    public ChatChangedEventArgs(Ed25519PublicKey contactKey, ChatMessage? added)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        ContactKey = contactKey;
        Added = added;
    }

    public Ed25519PublicKey ContactKey { get; }

    public ChatMessage? Added { get; }
}
