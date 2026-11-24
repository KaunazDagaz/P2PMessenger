using System.Net;
using P2PMessenger.Application.Internet;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Application.Tests.Internet;

[Trait("Story", "US-10")]
public sealed class InternetRouteTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("p2pm-route-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public async Task Messenger_WithoutTor_SaysTorIsNotInTheBuild()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);

        Assert.Equal(InternetRouteStatus.NotInBuild, messenger.InternetRoute.Status);
        Assert.Null(messenger.InternetRoute.TargetKind);
        messenger.Identity.Dispose();
    }

    [Fact]
    public async Task Messenger_TorExecutableMissing_SaysTorIsNotInTheBuildAndStartsNoListener()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setup = TorSetup.For(Path.Combine(folder, "no-tor"), Path.Combine(folder, "tor"));

        await using var messenger = await Messenger.StartAsync(
            TestKeys.NewIdentity("Alice"),
            MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback, ListenPort = 0, Connection = TestMessengers.Fast, Tor = setup },
            cancellationToken);

        Assert.Equal(InternetRouteState.NotInBuild, messenger.InternetRoute.Status.State);
        Assert.Null(messenger.InternetRoute.TargetKind);
        Assert.False(File.Exists(setup.SocketPath));
        messenger.Identity.Dispose();
    }

    [Fact]
    public async Task Messenger_TorTurnedOffInTheSetup_IsOffWithNoListener()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setup = BogusTor() with { Enabled = false };

        await using var messenger = await StartAsync(setup, cancellationToken);

        Assert.Equal(InternetRouteStatus.Off, messenger.InternetRoute.Status);
        Assert.True(messenger.InternetRoute.IsInBuild);
        Assert.False(messenger.InternetRoute.IsOn);
        Assert.Null(messenger.InternetRoute.TargetKind);
        Assert.False(File.Exists(setup.SocketPath));
        messenger.Identity.Dispose();
    }

    [Fact]
    public async Task SetEnabled_OnThenOff_StartsTorBehindTheListenerThenStopsItAndDeletesItsFolder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setup = BogusTor() with { Enabled = false, RestartDelay = TimeSpan.FromSeconds(30) };
        await using var messenger = await StartAsync(setup, cancellationToken);
        var changes = 0;
        messenger.InternetRoute.Changed += (_, _) => Interlocked.Increment(ref changes);

        await messenger.InternetRoute.SetEnabledAsync(true, cancellationToken);

        Assert.True(messenger.InternetRoute.IsOn);
        Assert.Equal(OperatingSystem.IsWindows() ? OnionTargetKind.LoopbackPort : OnionTargetKind.UnixSocket, messenger.InternetRoute.TargetKind);
        await TestMessengers.WaitUntilAsync(() => messenger.InternetRoute.Status.Problem == InternetRouteProblem.TorFailed, cancellationToken);
        Assert.True(Directory.Exists(setup.DataFolder));

        await messenger.InternetRoute.SetEnabledAsync(false, cancellationToken);

        Assert.Equal(InternetRouteStatus.Off, messenger.InternetRoute.Status);
        Assert.False(messenger.InternetRoute.IsOn);
        Assert.Null(messenger.InternetRoute.TargetKind);
        Assert.False(Directory.Exists(setup.DataFolder));
        Assert.True(changes >= 2);
        messenger.Identity.Dispose();
    }

    [Fact]
    public async Task SetEnabled_TorNotInTheBuild_StaysNotInTheBuild()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await StartAsync(TorSetup.For(Path.Combine(folder, "no-tor"), Path.Combine(folder, "tor")), cancellationToken);

        await messenger.InternetRoute.SetEnabledAsync(true, cancellationToken);

        Assert.Equal(InternetRouteStatus.NotInBuild, messenger.InternetRoute.Status);
        Assert.False(messenger.InternetRoute.IsOn);
        Assert.False(messenger.InternetRoute.IsInBuild);
        messenger.Identity.Dispose();
    }

    private TorSetup BogusTor()
    {
        var executable = Path.Combine(Directory.CreateDirectory(Path.Combine(folder, "bin")).FullName, OperatingSystem.IsWindows() ? "tor.exe" : "tor");
        File.WriteAllText(executable, "not a program");
        return TorSetup.For(executable, Path.Combine(folder, "tor"));
    }

    private static Task<Messenger> StartAsync(TorSetup setup, CancellationToken cancellationToken) =>
        Messenger.StartAsync(
            TestKeys.NewIdentity("Alice"),
            MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback, ListenPort = 0, Connection = TestMessengers.Fast, Tor = setup },
            cancellationToken);
}
