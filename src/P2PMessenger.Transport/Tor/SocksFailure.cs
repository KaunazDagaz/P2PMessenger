namespace P2PMessenger.Transport.Tor;

public enum SocksFailure
{
    None = 0,
    ProxyUnreachable = 1,
    ProxyClosed = 2,
    ProtocolError = 3,
    GeneralFailure = 4,
    NotAllowed = 5,
    NetworkUnreachable = 6,
    HostUnreachable = 7,
    ConnectionRefused = 8,
    TtlExpired = 9,
    CommandNotSupported = 10,
    AddressTypeNotSupported = 11,
    OnionDescriptorNotFound = 12,
    OnionDescriptorInvalid = 13,
    OnionIntroductionFailed = 14,
    OnionRendezvousFailed = 15,
    OnionClientAuthorizationMissing = 16,
    OnionClientAuthorizationWrong = 17,
    OnionAddressInvalid = 18,
    OnionIntroductionTimedOut = 19,
    UnknownReply = 20,
}
