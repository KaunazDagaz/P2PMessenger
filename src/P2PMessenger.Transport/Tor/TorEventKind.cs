namespace P2PMessenger.Transport.Tor;

internal enum TorEventKind
{
    Bootstrap,
    CircuitsEstablished,
    CircuitsNotEstablished,
    ClockSkew,
    ClockJumped,
    DirectoriesUnreachable,
    NetworkUp,
    NetworkDown,
    DescriptorUploaded,
    DescriptorFailed,
}
