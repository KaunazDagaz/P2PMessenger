using System.Net;
using System.Net.Sockets;
using P2PMessenger.Application.Connections;
using P2PMessenger.Core.Identity;
using static P2PMessenger.Application.Tests.TestKeys;

namespace P2PMessenger.Application.Tests.Connections;

public sealed class ConnectionServiceTests
{
    [Fact]
    public async Task ListenAsync_PortTakenByAnotherProgram_ReportsPortInUseAndAnotherPortWorks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var other = new TcpListener(IPAddress.Loopback, 0);
        other.Start();
        int taken = ((IPEndPoint)other.LocalEndpoint).Port;

        var inUse = await messenger.Connections.ListenAsync(IPAddress.Loopback, taken, cancellationToken);

        Assert.Equal(ListenResult.PortInUse, inUse);
        Assert.Equal(ListenResult.PortInUse, messenger.Connections.LastListenResult);
        Assert.Equal(taken, messenger.Connections.LastRequestedPort);
        Assert.Null(messenger.Connections.ListeningPort);

        var free = await messenger.Connections.ListenAsync(IPAddress.Loopback, 0, cancellationToken);

        Assert.Equal(ListenResult.Listening, free);
        Assert.NotNull(messenger.Connections.ListeningPort);
        Assert.NotEqual(taken, messenger.Connections.ListeningPort);
        messenger.Identity.Dispose();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task ListenAsync_PortOutOfRange_IsRefused(int port)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        int? before = messenger.Connections.ListeningPort;

        var result = await messenger.Connections.ListenAsync(IPAddress.Loopback, port, cancellationToken);

        Assert.Equal(ListenResult.InvalidPort, result);
        Assert.Equal(before, messenger.Connections.ListeningPort);
        messenger.Identity.Dispose();
    }

    [Fact]
    public async Task SetAddress_KnownContact_IsKeptUntilCleared()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        await messenger.Contacts.AddAsync(messenger.Contacts.Check(Code(bob, "Bob")), cancellationToken);
        var address = new IPEndPoint(IPAddress.Parse("192.0.2.10"), 45821);

        Assert.True(messenger.Connections.SetAddress(bob.IdentityKey, address));
        Assert.True(messenger.Connections.TryGetAddress(bob.IdentityKey, out var kept));
        Assert.Equal(address, kept);

        Assert.True(messenger.Connections.SetAddress(bob.IdentityKey, null));
        Assert.False(messenger.Connections.TryGetAddress(bob.IdentityKey, out _));
        Assert.Equal(DialStatus.None, messenger.Connections.DialStatusOf(bob.IdentityKey));
        messenger.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-02")]
    public async Task SetAddress_NobodyListening_ReportsUnreachable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        await messenger.Contacts.AddAsync(messenger.Contacts.Check(Code(bob, "Bob")), cancellationToken);
        using var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var address = (IPEndPoint)closed.LocalEndpoint;
        closed.Stop();

        messenger.Connections.SetAddress(bob.IdentityKey, address);

        await TestMessengers.WaitUntilAsync(() => messenger.Connections.DialStatusOf(bob.IdentityKey) == DialStatus.Unreachable, cancellationToken);
        Assert.False(messenger.Connections.IsConnected(bob.IdentityKey));
        messenger.Identity.Dispose();
    }

    [Fact]
    public void LocalAddresses_Find_ReturnsOnlyIPv4AddressesThatAreNotLoopback()
    {
        var addresses = LocalAddresses.Find();

        Assert.All(addresses, static address =>
        {
            Assert.Equal(AddressFamily.InterNetwork, address.AddressFamily);
            Assert.False(IPAddress.IsLoopback(address));
        });
    }
}
