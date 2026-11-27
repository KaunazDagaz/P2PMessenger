namespace P2PMessenger.Core.Messages;

public enum SessionMessageType
{
    None = 0,
    Text = 1,
    Acknowledgement = 2,
    KeepAlive = 3,
    GroupInvitation = 4,
    GroupJoin = 5,
    GroupMessage = 6,
    GroupSummary = 7,
    GroupAcknowledgement = 8,
    GroupUpdate = 9,
    GroupLeave = 10,
    GroupRemoved = 11,
}
