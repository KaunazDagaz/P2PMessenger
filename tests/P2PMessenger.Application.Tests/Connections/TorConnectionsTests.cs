using P2PMessenger.Application.Connections;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Application.Tests.Connections;

[Trait("Story", "US-09")]
public sealed class TorConnectionsTests
{
    [Theory]
    [InlineData(SocksFailure.None, TorDialStatus.None)]
    [InlineData(SocksFailure.OnionDescriptorNotFound, TorDialStatus.NotFound)]
    [InlineData(SocksFailure.OnionIntroductionFailed, TorDialStatus.NoAnswer)]
    [InlineData(SocksFailure.OnionIntroductionTimedOut, TorDialStatus.NoAnswer)]
    [InlineData(SocksFailure.OnionRendezvousFailed, TorDialStatus.NoAnswer)]
    [InlineData(SocksFailure.HostUnreachable, TorDialStatus.NoAnswer)]
    [InlineData(SocksFailure.ConnectionRefused, TorDialStatus.NoAnswer)]
    [InlineData(SocksFailure.TtlExpired, TorDialStatus.NoAnswer)]
    [InlineData(SocksFailure.ProxyUnreachable, TorDialStatus.Failed)]
    [InlineData(SocksFailure.ProxyClosed, TorDialStatus.Failed)]
    [InlineData(SocksFailure.ProtocolError, TorDialStatus.Failed)]
    [InlineData(SocksFailure.GeneralFailure, TorDialStatus.Failed)]
    [InlineData(SocksFailure.OnionDescriptorInvalid, TorDialStatus.Failed)]
    [InlineData(SocksFailure.OnionAddressInvalid, TorDialStatus.Failed)]
    [InlineData(SocksFailure.UnknownReply, TorDialStatus.Failed)]
    public void StatusOf_ReplyOfTor_GivesTheAttemptState(SocksFailure failure, TorDialStatus expected)
    {
        Assert.Equal(expected, TorConnections.StatusOf(failure));
    }
}
