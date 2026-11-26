using P2PMessenger.Core.Copy;

namespace P2PMessenger.Application.Copy;

public sealed class CopiedChatChangedEventArgs : EventArgs
{
    public CopiedChatChangedEventArgs(CopyOriginId origin, CopiedChatChange change, CopiedConversation? conversation)
    {
        ArgumentNullException.ThrowIfNull(origin);
        Origin = origin;
        Change = change;
        Conversation = conversation;
    }

    public CopyOriginId Origin { get; }

    public CopiedChatChange Change { get; }

    public CopiedConversation? Conversation { get; }
}
