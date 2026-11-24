using System.Net;
using System.Net.Sockets;
using P2PMessenger.Application.Internet;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Application.Tests.Internet;

[Trait("Category", "Network")]
[Trait("Story", "US-09")]
public sealed class RealTorRouteTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("p2pm-real-route-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact(Timeout = 60_000)]
    public async Task Messenger_WithTor_StartsTorBehindAPrivateTargetAndStopsItOnDispose()
    {
        var executable = Environment.GetEnvironmentVariable("P2PM_TOR");
        Assert.SkipUnless(executable is { Length: > 0 } && File.Exists(executable), "Set P2PM_TOR to a tor executable to run this test.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var setup = TorSetup.For(Path.GetFullPath(executable!), Path.Combine(folder, "tor"));
        var identity = TestKeys.NewIdentity("Alice");
        var controlPortFile = Path.Combine(setup.DataFolder, TorRuntime.ControlPortFileName);
        string controlPort;

        var messenger = await Messenger.StartAsync(
            identity,
            MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback, ListenPort = 0, Connection = TestMessengers.Fast, Tor = setup },
            cancellationToken);
        try
        {
            Assert.Equal(OperatingSystem.IsWindows() ? OnionTargetKind.LoopbackPort : OnionTargetKind.UnixSocket, messenger.InternetRoute.TargetKind);
            Assert.Contains(messenger.InternetRoute.Status.State, new[] { InternetRouteState.Starting, InternetRouteState.Unavailable, InternetRouteState.Connected });
            await TestMessengers.WaitUntilAsync(() => File.Exists(Path.Combine(setup.DataFolder, TorRuntime.CookieFileName)) && File.Exists(controlPortFile), cancellationToken);
            controlPort = File.ReadAllText(controlPortFile).Trim()["PORT=".Length..];
        }
        finally
        {
            await messenger.DisposeAsync();
            identity.Dispose();
        }

        Assert.False(File.Exists(setup.SocketPath));
        await TestMessengers.WaitUntilAsync(() => !Accepts(IPEndPoint.Parse(controlPort)), cancellationToken);
    }

    [Fact(Timeout = 90_000)]
    public async Task SwitchOffAndOn_StopsTorAndStartsItAgain()
    {
        var executable = Environment.GetEnvironmentVariable("P2PM_TOR");
        Assert.SkipUnless(executable is { Length: > 0 } && File.Exists(executable), "Set P2PM_TOR to a tor executable to run this test.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var setup = TorSetup.For(Path.GetFullPath(executable!), Path.Combine(folder, "tor"));
        var controlPortFile = Path.Combine(setup.DataFolder, TorRuntime.ControlPortFileName);
        var identity = TestKeys.NewIdentity("Alice");
        await using var messenger = await Messenger.StartAsync(
            identity,
            MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback, ListenPort = 0, Connection = TestMessengers.Fast, Tor = setup },
            cancellationToken);
        await TestMessengers.WaitUntilAsync(() => File.Exists(controlPortFile), cancellationToken);
        var first = File.ReadAllText(controlPortFile).Trim()["PORT=".Length..];

        await messenger.InternetRoute.SetEnabledAsync(false, cancellationToken);

        Assert.Equal(InternetRouteStatus.Off, messenger.InternetRoute.Status);
        Assert.False(Directory.Exists(setup.DataFolder));
        await TestMessengers.WaitUntilAsync(() => !Accepts(IPEndPoint.Parse(first)), cancellationToken);

        await messenger.InternetRoute.SetEnabledAsync(true, cancellationToken);

        await TestMessengers.WaitUntilAsync(() => File.Exists(controlPortFile) && File.Exists(Path.Combine(setup.DataFolder, TorRuntime.CookieFileName)), cancellationToken);
        var second = File.ReadAllText(controlPortFile).Trim()["PORT=".Length..];
        await TestMessengers.WaitUntilAsync(() => Accepts(IPEndPoint.Parse(second)), cancellationToken);
        Assert.True(messenger.InternetRoute.IsOn);
        identity.Dispose();
    }

    private static bool Accepts(IPEndPoint endpoint)
    {
        using var client = new TcpClient();
        try
        {
            client.Connect(endpoint);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
