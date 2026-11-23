using System.Net.Sockets;
using P2PMessenger.Application.Connections;

namespace P2PMessenger.Application.Tests.Connections;

public sealed class ListenPortsTests
{
    [Theory]
    [Trait("Story", "US-06")]
    [InlineData(SocketError.AccessDenied)]
    [InlineData(SocketError.AddressAlreadyInUse)]
    public void StartOnRandomPort_PortReservedOrInUse_TriesAnotherPort(SocketError refusal)
    {
        var tried = new List<int>();
        var next = 50000;

        var (listener, result) = ListenPorts.StartOnRandomPort(
            () => next++,
            port =>
            {
                tried.Add(port);
                return port < 50002 ? throw new SocketException((int)refusal) : new object();
            },
            32);

        Assert.NotNull(listener);
        Assert.Equal(ListenResult.Listening, result);
        Assert.Equal([50000, 50001, 50002], tried);
    }

    [Theory]
    [InlineData(SocketError.AccessDenied, ListenResult.Failed)]
    [InlineData(SocketError.AddressAlreadyInUse, ListenResult.PortInUse)]
    public void StartOnRandomPort_EveryPortRefused_GivesUpAfterTheLastAttempt(SocketError refusal, ListenResult expected)
    {
        var attempts = 0;

        var (listener, result) = ListenPorts.StartOnRandomPort<object>(
            () => 60000,
            _ =>
            {
                attempts++;
                throw new SocketException((int)refusal);
            },
            32);

        Assert.Null(listener);
        Assert.Equal(expected, result);
        Assert.Equal(32, attempts);
    }

    [Fact]
    public void StartOnRandomPort_AnotherSocketError_StopsAtOnce()
    {
        var attempts = 0;

        var (listener, result) = ListenPorts.StartOnRandomPort<object>(
            () => 60000,
            _ =>
            {
                attempts++;
                throw new SocketException((int)SocketError.AddressNotAvailable);
            },
            32);

        Assert.Null(listener);
        Assert.Equal(ListenResult.Failed, result);
        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData(SocketError.AddressAlreadyInUse, ListenResult.PortInUse, true)]
    [InlineData(SocketError.AccessDenied, ListenResult.Failed, true)]
    [InlineData(SocketError.AddressNotAvailable, ListenResult.Failed, false)]
    public void ResultOfAndIsUnavailable_ClassifyTheSocketError(SocketError error, ListenResult result, bool unavailable)
    {
        Assert.Equal(result, ListenPorts.ResultOf(error));
        Assert.Equal(unavailable, ListenPorts.IsUnavailable(error));
    }
}
