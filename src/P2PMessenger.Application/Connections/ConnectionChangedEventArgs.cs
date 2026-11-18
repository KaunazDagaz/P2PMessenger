using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Application.Connections;

public sealed class ConnectionChangedEventArgs : EventArgs
{
    public ConnectionChangedEventArgs(Ed25519PublicKey? contactKey)
    {
        ContactKey = contactKey;
    }

    public Ed25519PublicKey? ContactKey { get; }
}
