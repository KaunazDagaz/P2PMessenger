using Microsoft.Extensions.Logging.Abstractions;
using P2PMessenger.Core.Identity;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Transport.Tests.Tor;

[Trait("Category", "Network")]
[Trait("Story", "US-09")]
public sealed class RealTorTests : IDisposable
{
    public const string ExecutableVariable = "P2PM_TOR";

    private readonly string folder = Directory.CreateTempSubdirectory("p2pm-real-tor-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact(Timeout = 60_000)]
    public async Task OwnOnionService_TorWithoutNetwork_AcceptsTheKeyAndKeepsNoServiceKeyOnDisk()
    {
        var executable = Environment.GetEnvironmentVariable(ExecutableVariable);
        Assert.SkipUnless(executable is { Length: > 0 } && File.Exists(executable), "Set P2PM_TOR to a tor executable to run this test.");
        var cancellationToken = TestContext.Current.CancellationToken;
        using var keys = KeySet.Create();
        var socket = Path.Combine(folder, "s", "onion.sock");
        var target = OperatingSystem.IsWindows() ? OnionTarget.Loopback(45999) : OnionTarget.UnixSocket(socket);
        var options = new TorOptions
        {
            ExecutablePath = Path.GetFullPath(executable!),
            DataFolder = Path.Combine(folder, "tor"),
            DisableNetwork = true,
        };
        var runtime = TorRuntime.Start(options, keys, target, NullLogger.Instance, TimeProvider.System, SystemTorProcessStarter.Instance);
        try
        {
            Assert.True(await Eventually.TrueAsync(() => runtime.ControlledCount == 1, cancellationToken, TimeSpan.FromSeconds(30)));
            Assert.Equal(TorStatus.Starting(0), runtime.Status);
        }
        finally
        {
            await runtime.DisposeAsync();
        }

        var keysFolder = Path.Combine(options.DataFolder, "keys");
        var files = Directory.Exists(keysFolder) ? Directory.GetFiles(keysFolder, "*", SearchOption.AllDirectories) : [];
        Assert.DoesNotContain(files, static file => Path.GetFileName(file).Contains("hs_ed25519", StringComparison.Ordinal));
        Assert.Empty(Directory.GetDirectories(options.DataFolder, "hs*", SearchOption.AllDirectories));
    }
}
