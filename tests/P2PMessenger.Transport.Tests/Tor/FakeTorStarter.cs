using System.Collections.Concurrent;
using System.Security.Cryptography;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Transport.Tests.Tor;

internal sealed class FakeTorStarter : ITorProcessStarter, IAsyncDisposable
{
    private readonly ConcurrentQueue<FakeTor> started = new();
    private readonly SemaphoreSlim starts = new(0);

    public Action<FakeTor>? Configure { get; set; }

    public bool FailToStart { get; set; }

    public bool WriteNoControlPort { get; set; }

    public bool ExitAtOnce { get; set; }

    public TorLaunch? LastLaunch { get; private set; }

    public int StartCount => started.Count;

    public ITorProcess Start(TorLaunch launch)
    {
        LastLaunch = launch;
        if (FailToStart)
        {
            throw new TorControlException("The fake tor does not start.");
        }

        var cookie = RandomNumberGenerator.GetBytes(TorControlClient.CookieLength);
        File.WriteAllBytes(launch.CookieFile, cookie);
        var tor = new FakeTor(launch.CookieFile, cookie);
        Configure?.Invoke(tor);
        tor.Serve();
        if (ExitAtOnce)
        {
            tor.Exit();
        }
        else if (!WriteNoControlPort)
        {
            File.WriteAllText(launch.ControlPortFile, "PORT=" + tor.Endpoint + "\n");
        }

        started.Enqueue(tor);
        starts.Release();
        return new Process(tor);
    }

    public async Task<FakeTor> NextStartAsync(CancellationToken cancellationToken)
    {
        await starts.WaitAsync(cancellationToken);
        return started.ToArray()[^1];
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var tor in started)
        {
            await tor.DisposeAsync();
        }

        starts.Dispose();
    }

    private sealed class Process(FakeTor tor) : ITorProcess
    {
        public bool HasExited => tor.HasExited;

        public int? ExitCode => tor.HasExited ? 0 : null;

        public Task WaitForExitAsync(CancellationToken cancellationToken) => tor.Exited.WaitAsync(cancellationToken);

        public void Kill() => tor.Exit();

        public void Dispose()
        {
        }
    }
}
