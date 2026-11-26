using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Application.Groups;

internal sealed class GroupAcknowledgementArrivedEventArgs : EventArgs
{
    public GroupAcknowledgementArrivedEventArgs(Ed25519PublicKey senderKey, RelayedGroupAcknowledgement relayed)
    {
        SenderKey = senderKey;
        Relayed = relayed;
    }

    public Ed25519PublicKey SenderKey { get; }

    public RelayedGroupAcknowledgement Relayed { get; }
}
