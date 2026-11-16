using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace P2PMessenger.Integration.Tests.Connections;

internal static class Raw
{
    public const int StillOpen = -1;

    public static async Task<TcpClient> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            await client.ConnectAsync(endpoint, cancellationToken);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public static async Task<int> CountBytesUntilClosedAsync(NetworkStream stream, TimeSpan limit, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);
        var buffer = new byte[1024];
        var total = 0;
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token);
                if (read == 0)
                {
                    return total;
                }

                total += read;
            }
        }
        catch (IOException)
        {
            return total;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StillOpen;
        }
    }

    public static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task<bool> EventuallyAsync(Func<bool> condition, TimeSpan limit, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > limit)
            {
                return false;
            }

            await Task.Delay(20, cancellationToken);
        }

        return true;
    }
}

internal sealed class RawServer : IDisposable
{
    private readonly TcpListener listener;

    public RawServer()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Endpoint = (IPEndPoint)listener.LocalEndpoint;
    }

    public IPEndPoint Endpoint { get; }

    public async Task<TcpClient> AcceptAsync(CancellationToken cancellationToken) =>
        await listener.AcceptTcpClientAsync(cancellationToken);

    public void Dispose() => listener.Dispose();
}
