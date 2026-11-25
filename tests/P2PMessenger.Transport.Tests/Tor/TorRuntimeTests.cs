using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using P2PMessenger.Core.Identity;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Transport.Tests.Tor;

[Trait("Story", "US-09")]
public sealed class TorRuntimeTests : IAsyncDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("p2pm-tor-runtime-").FullName;
    private readonly KeySet keys = KeySet.Create();
    private readonly FakeTorStarter starter = new();

    public async ValueTask DisposeAsync()
    {
        await starter.DisposeAsync();
        keys.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public async Task Start_TorAnswers_AuthenticatesOwnsSubscribesAndAddsTheOwnOnionService()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = tor => tor.ServiceId = keys.OnionAddress.Label;
        await using var runtime = Start();
        var tor = await starter.NextStartAsync(cancellationToken);

        Assert.True(await Eventually.TrueAsync(() => tor.Commands.Any(static command => command.StartsWith("ADD_ONION", StringComparison.Ordinal)), cancellationToken));

        var expanded = new byte[OnionServiceKey.ExpandedLength];
        Assert.True(keys.TryExportOnionServiceKey(expanded));
        Assert.Equal(
            [
                "PROTOCOLINFO 1",
                "AUTHENTICATE " + Convert.ToHexString(tor.Cookie),
                "TAKEOWNERSHIP",
                "SETEVENTS STATUS_CLIENT STATUS_GENERAL HS_DESC NETWORK_LIVENESS",
                "GETINFO status/bootstrap-phase",
                "GETINFO net/listeners/socks",
                "ADD_ONION ED25519-V3:" + Convert.ToBase64String(expanded) + " Port=45821,unix:/tmp/p2pm-test/onion.sock",
            ],
            tor.Commands);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 39050), runtime.SocksEndpoint);
        Assert.Equal(TorStatus.Starting(0), runtime.Status);
    }

    [Fact]
    public async Task Start_LaunchesTorWithItsOwnSettingsOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = tor => tor.ServiceId = keys.OnionAddress.Label;
        await using var runtime = Start();
        await starter.NextStartAsync(cancellationToken);

        var launch = starter.LastLaunch!;
        var arguments = launch.Arguments.ToList();
        Assert.Equal(Path.Combine(folder, "tor", "torrc"), arguments[arguments.IndexOf("-f") + 1]);
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(arguments[arguments.IndexOf("-f") + 1], cancellationToken));
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(arguments[arguments.IndexOf("--defaults-torrc") + 1], cancellationToken));
        Assert.Contains("--quiet", arguments);
        Assert.Equal(Path.Combine(folder, "tor"), arguments[arguments.IndexOf("--DataDirectory") + 1]);
        Assert.Equal("auto", arguments[arguments.IndexOf("--ControlPort") + 1]);
        Assert.Equal("auto ExtendedErrors", arguments[arguments.IndexOf("--SocksPort") + 1]);
        Assert.Equal("1", arguments[arguments.IndexOf("--CookieAuthentication") + 1]);
        Assert.Equal(launch.CookieFile, arguments[arguments.IndexOf("--CookieAuthFile") + 1]);
        Assert.Equal(launch.ControlPortFile, arguments[arguments.IndexOf("--ControlPortWriteToFile") + 1]);
        Assert.Equal(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), arguments[arguments.IndexOf("--__OwningControllerProcess") + 1]);
        Assert.DoesNotContain("--DisableNetwork", arguments);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(launch.DataFolder));
        }
    }

    [Fact]
    public async Task Status_BootstrapToTheEnd_IsConnectedAndPublishedOnceTheOwnDescriptorIsUploaded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = tor => tor.ServiceId = keys.OnionAddress.Label;
        await using var runtime = Start();
        var changes = 0;
        runtime.StatusChanged += (_, _) => Interlocked.Increment(ref changes);
        var tor = await AddedAsync(cancellationToken);

        await tor.SendAsync("650 STATUS_CLIENT NOTICE BOOTSTRAP PROGRESS=50 TAG=loading_descriptors SUMMARY=\"Loading\"");
        Assert.True(await Eventually.TrueAsync(() => runtime.Status == TorStatus.Starting(50), cancellationToken));
        await tor.SendAsync("650 STATUS_CLIENT NOTICE BOOTSTRAP PROGRESS=100 TAG=done SUMMARY=\"Done\"");
        Assert.True(await Eventually.TrueAsync(() => runtime.Status.State == TorState.Connected, cancellationToken));
        Assert.False(runtime.Status.OnionServicePublished);
        await tor.SendAsync("650 HS_DESC UPLOADED " + keys.OnionAddress.Label + " UNKNOWN $AAAA~relay");

        Assert.True(await Eventually.TrueAsync(() => runtime.Status.OnionServicePublished, cancellationToken));
        Assert.True(changes >= 3);
    }

    [Fact]
    public async Task Status_ClockSkew_IsAWrongClock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = tor => tor.ServiceId = keys.OnionAddress.Label;
        await using var runtime = Start();
        var tor = await AddedAsync(cancellationToken);

        await tor.SendAsync("650 STATUS_GENERAL WARN CLOCK_SKEW SKEW=-90000 SOURCE=NETWORKSTATUS:198.51.100.1:80");

        Assert.True(await Eventually.TrueAsync(() => runtime.Status.Problem == TorProblem.ClockWrong, cancellationToken));
        Assert.Equal(TorState.Unavailable, runtime.Status.State);
    }

    [Fact]
    public async Task Status_BootstrapWarningsPastTheGrace_CannotReachTor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = tor => tor.ServiceId = keys.OnionAddress.Label;
        await using var runtime = Start(static options => options with { WarningGrace = TimeSpan.FromMilliseconds(300) });
        var tor = await AddedAsync(cancellationToken);

        await tor.SendAsync("650 STATUS_CLIENT WARN BOOTSTRAP PROGRESS=5 TAG=conn SUMMARY=\"Connecting\" WARNING=\"Connection refused\" REASON=CONNECTREFUSED COUNT=1 RECOMMENDATION=ignore");

        Assert.True(await Eventually.TrueAsync(() => runtime.Status.Problem == TorProblem.CannotReachTor, cancellationToken));
    }

    [Fact]
    public async Task Start_TorReturnsAnotherServiceId_FailsAndTriesAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = static tor => tor.ServiceId = "25njqamcweflpvkl73j4szahhihoc4xt3ktcgjnpaingr5yhkenl5sid";
        await using var runtime = Start();

        Assert.True(await Eventually.TrueAsync(() => runtime.Status.Problem == TorProblem.StartFailed, cancellationToken));
        Assert.True(await Eventually.TrueAsync(() => starter.StartCount >= 2, cancellationToken));
    }

    [Fact]
    public async Task Start_TorDoesNotStart_IsStartFailedAndTriesAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.FailToStart = true;
        await using var runtime = Start();

        Assert.True(await Eventually.TrueAsync(() => runtime.Status == new TorStatus(TorState.Unavailable, 0, TorProblem.StartFailed, false), cancellationToken));
    }

    [Fact]
    public async Task Start_NoControlPort_FailsAfterTheStartTimeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.WriteNoControlPort = true;
        await using var runtime = Start(static options => options with { StartTimeout = TimeSpan.FromMilliseconds(300) });

        Assert.True(await Eventually.TrueAsync(() => runtime.Status.Problem == TorProblem.StartFailed, cancellationToken));
        var tor = await starter.NextStartAsync(cancellationToken);
        Assert.Empty(tor.Commands);
    }

    [Fact]
    public async Task Start_TorExitsAtOnce_IsStartFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.ExitAtOnce = true;
        await using var runtime = Start();

        Assert.True(await Eventually.TrueAsync(() => runtime.Status.Problem == TorProblem.StartFailed, cancellationToken));
    }

    [Fact]
    public async Task Start_TorRefusesTheOnionService_IsStartFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = static tor => tor.RefuseCommand = "ADD_ONION";
        await using var runtime = Start();

        Assert.True(await Eventually.TrueAsync(() => runtime.Status.Problem == TorProblem.StartFailed, cancellationToken));
    }

    [Fact]
    public async Task TorExits_IsStoppedAndStartsAgainWithTheOnionService()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = tor => tor.ServiceId = keys.OnionAddress.Label;
        await using var runtime = Start(static options => options with { RestartDelay = TimeSpan.FromMilliseconds(500) });
        var first = await AddedAsync(cancellationToken);

        first.Exit();

        Assert.True(await Eventually.TrueAsync(() => runtime.Status.Problem == TorProblem.Stopped, cancellationToken));
        var second = await starter.NextStartAsync(cancellationToken);
        Assert.True(await Eventually.TrueAsync(() => second.Commands.Any(static command => command.StartsWith("ADD_ONION", StringComparison.Ordinal)), cancellationToken));
        Assert.True(await Eventually.TrueAsync(() => runtime.Status == TorStatus.Starting(0), cancellationToken));
    }

    [Fact]
    public async Task NetworkChanged_ANetwork_TorDropsAndRebuildsItsConnectionsAndTheNewSocksPortIsRead()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = tor => tor.ServiceId = keys.OnionAddress.Label;
        await using var runtime = Start();
        var changes = 0;
        runtime.StatusChanged += (_, _) => Interlocked.Increment(ref changes);
        var tor = await AddedAsync(cancellationToken);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 39050), runtime.SocksEndpoint);

        runtime.NetworkChanged(hasAnyNetwork: true);

        Assert.True(await Eventually.TrueAsync(() => Equals(runtime.SocksEndpoint, new IPEndPoint(IPAddress.Loopback, 39051)), cancellationToken));
        Assert.Equal(["SETCONF DisableNetwork=1", "SETCONF DisableNetwork=0", "GETINFO net/listeners/socks"], AfterOnion(tor));
        Assert.False(tor.NetworkDisabled);
        Assert.True(changes >= 1);
        Assert.Equal(1, starter.StartCount);
        Assert.Equal(1, runtime.ControlledCount);
    }

    [Fact]
    public async Task NetworkChanged_NoNetwork_PausesTorUntilANetworkReturns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = tor => tor.ServiceId = keys.OnionAddress.Label;
        await using var runtime = Start();
        var tor = await AddedAsync(cancellationToken);

        runtime.NetworkChanged(hasAnyNetwork: false);

        Assert.True(await Eventually.TrueAsync(() => runtime.SocksEndpoint is null && tor.NetworkDisabled, cancellationToken));
        Assert.Equal(["SETCONF DisableNetwork=1"], AfterOnion(tor));

        runtime.NetworkChanged(hasAnyNetwork: false);
        runtime.NetworkChanged(hasAnyNetwork: true);

        Assert.True(await Eventually.TrueAsync(() => runtime.SocksEndpoint is not null, cancellationToken));
        Assert.Equal(["SETCONF DisableNetwork=1", "SETCONF DisableNetwork=0", "GETINFO net/listeners/socks"], AfterOnion(tor));
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 39051), runtime.SocksEndpoint);
    }

    [Fact]
    public async Task NetworkChanged_NoNetworkWhenTorStartsAgain_TheNewTorStartsPaused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = tor => tor.ServiceId = keys.OnionAddress.Label;
        await using var runtime = Start(static options => options with { RestartDelay = TimeSpan.FromMilliseconds(300) });
        var first = await AddedAsync(cancellationToken);
        runtime.NetworkChanged(hasAnyNetwork: false);
        Assert.True(await Eventually.TrueAsync(() => first.NetworkDisabled, cancellationToken));

        first.Exit();
        var second = await starter.NextStartAsync(cancellationToken);

        Assert.True(await Eventually.TrueAsync(() => second.NetworkDisabled, cancellationToken));
        Assert.Equal(["SETCONF DisableNetwork=1"], AfterOnion(second));
        Assert.True(await Eventually.TrueAsync(() => runtime.SocksEndpoint is null && runtime.ControlledCount == 2, cancellationToken));
    }

    [Fact]
    public async Task NetworkChanged_TorRefusesTheSetting_StopsTorAndStartsItAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = tor =>
        {
            tor.ServiceId = keys.OnionAddress.Label;
            tor.RefuseCommand = "SETCONF";
        };
        await using var runtime = Start(static options => options with { RestartDelay = TimeSpan.FromMilliseconds(300) });
        var first = await AddedAsync(cancellationToken);

        runtime.NetworkChanged(hasAnyNetwork: true);

        Assert.True(await Eventually.TrueAsync(() => first.HasExited, cancellationToken));
        Assert.True(await Eventually.TrueAsync(() => starter.StartCount >= 2, cancellationToken));
    }

    [Fact]
    public async Task NetworkChanged_TorStartedWithItsNetworkOff_IsIgnored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = tor => tor.ServiceId = keys.OnionAddress.Label;
        await using var runtime = Start(static options => options with { DisableNetwork = true });
        var tor = await AddedAsync(cancellationToken);

        runtime.NetworkChanged(hasAnyNetwork: false);
        runtime.NetworkChanged(hasAnyNetwork: true);
        await Task.Delay(300, cancellationToken);

        Assert.Empty(AfterOnion(tor));
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 39050), runtime.SocksEndpoint);
    }

    [Fact]
    public async Task Dispose_AsksTorToShutDown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = tor => tor.ServiceId = keys.OnionAddress.Label;
        var runtime = Start();
        var tor = await AddedAsync(cancellationToken);

        await runtime.DisposeAsync();

        Assert.Contains("SIGNAL SHUTDOWN", tor.Commands);
        Assert.True(tor.HasExited);
        Assert.Equal(1, starter.StartCount);
    }

    [Fact]
    public async Task Dispose_TorIgnoresTheShutdown_IsKilled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        starter.Configure = tor =>
        {
            tor.ServiceId = keys.OnionAddress.Label;
            tor.ExitOnShutdown = false;
        };
        var runtime = Start(static options => options with { ShutdownTimeout = TimeSpan.FromMilliseconds(300) });
        var tor = await AddedAsync(cancellationToken);

        await runtime.DisposeAsync();

        Assert.True(tor.HasExited);
    }

    [Theory]
    [InlineData("PORT=127.0.0.1:41203\n", "127.0.0.1:41203")]
    [InlineData("PORT=127.0.0.1:41203\r\n", "127.0.0.1:41203")]
    [InlineData("PORT=[::1]:41203\n", "[::1]:41203")]
    [InlineData("UNIX_PORT=/x\nPORT=127.0.0.1:9\n", "127.0.0.1:9")]
    public void TryParseControlPort_LoopbackPort_GivesTheEndpoint(string text, string expected)
    {
        Assert.True(TorRuntime.TryParseControlPort(text, out var endpoint));

        Assert.Equal(IPEndPoint.Parse(expected), endpoint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("PORT=192.0.2.1:41203\n")]
    [InlineData("PORT=127.0.0.1:0\n")]
    [InlineData("PORT=nonsense\n")]
    [InlineData("port=127.0.0.1:41203\n")]
    public void TryParseControlPort_AnythingElse_ReturnsFalse(string text)
    {
        Assert.False(TorRuntime.TryParseControlPort(text, out _));
    }

    [Theory]
    [InlineData("\"127.0.0.1:9050\"", "127.0.0.1:9050")]
    [InlineData("\"192.0.2.1:9050\" \"127.0.0.1:9150\"", "127.0.0.1:9150")]
    [InlineData("\"unix:/run/tor/socks\"", null)]
    [InlineData("\"", null)]
    [InlineData(null, null)]
    public void ParseSocksListener_GivesTheFirstLoopbackListener(string? value, string? expected)
    {
        Assert.Equal(expected is null ? null : IPEndPoint.Parse(expected), TorRuntime.ParseSocksListener(value));
    }

    [Fact]
    public void Options_Defaults_HaveNoProblem()
    {
        Assert.Null(Options().FindProblem());
    }

    [Fact]
    public void Options_RelativePathsOrNonPositiveTimes_AreProblems()
    {
        Assert.NotNull((Options() with { ExecutablePath = "tor" }).FindProblem());
        Assert.NotNull((Options() with { DataFolder = "tor" }).FindProblem());
        Assert.NotNull((Options() with { RestartDelay = TimeSpan.Zero }).FindProblem());
        Assert.Throws<ArgumentException>(() => TorRuntime.Start(Options() with { WarningGrace = TimeSpan.FromSeconds(-1) }, keys, Target(), NullLogger.Instance, TimeProvider.System, starter));
    }

    private static OnionTarget Target() => OnionTarget.UnixSocket("/tmp/p2pm-test/onion.sock");

    private TorOptions Options() => new()
    {
        ExecutablePath = Path.Combine(folder, "tor-binary"),
        DataFolder = Path.Combine(folder, "tor"),
        StartTimeout = TimeSpan.FromSeconds(5),
        CommandTimeout = TimeSpan.FromSeconds(5),
        RestartDelay = TimeSpan.FromMilliseconds(200),
        ShutdownTimeout = TimeSpan.FromSeconds(2),
    };

    private TorRuntime Start(Func<TorOptions, TorOptions>? change = null) =>
        TorRuntime.Start(change is null ? Options() : change(Options()), keys, Target(), NullLogger.Instance, TimeProvider.System, starter);

    private static List<string> AfterOnion(FakeTor tor) =>
        [.. tor.Commands.SkipWhile(static command => !command.StartsWith("ADD_ONION", StringComparison.Ordinal)).Skip(1).Where(static command => command != "SIGNAL SHUTDOWN")];

    private async Task<FakeTor> AddedAsync(CancellationToken cancellationToken)
    {
        var tor = await starter.NextStartAsync(cancellationToken);
        Assert.True(await Eventually.TrueAsync(() => tor.Commands.Any(static command => command.StartsWith("ADD_ONION", StringComparison.Ordinal)), cancellationToken));
        await Task.Delay(50, cancellationToken);
        return tor;
    }
}
