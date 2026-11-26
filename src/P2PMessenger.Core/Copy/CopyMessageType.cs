namespace P2PMessenger.Core.Copy;

public enum CopyMessageType : byte
{
    Ready = 1,
    KeepAlive = 2,
    TransferStart = 3,
    ConversationStart = 4,
    ConversationMessage = 5,
    ConversationEnd = 6,
    ConversationSaved = 7,
    TransferDone = 8,
    TransferComplete = 9,
}
