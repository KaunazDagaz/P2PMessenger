using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace P2PMessenger.Transport.Tests.Tor;

internal sealed class FakeTor : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentQueue<string> commands = new();
    private readonly Lock gate = new();
    private readonly TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Stream? client;
    private Task serving = Task.CompletedTask;

    public FakeTor(string cookieFile, byte[] cookie)
    {
        CookieFile = cookieFile;
        Cookie = cookie;
        listener.Start();
    }

    public string CookieFile { get; }

    public string? NamedCookieFile { get; set; }

    public byte[] Cookie { get; }

    public string Methods { get; set; } = "COOKIE,SAFECOOKIE";

    public string BootstrapPhase { get; set; } = "NOTICE BOOTSTRAP PROGRESS=0 TAG=starting SUMMARY=\"Starting\"";

    public string? ServiceId { get; set; }

    public string? RefuseCommand { get; set; }

    public bool Silent { get; set; }

    public bool ExitOnShutdown { get; set; } = true;

    public IPEndPoint Endpoint => (IPEndPoint)listener.LocalEndpoint;

    public IReadOnlyCollection<string> Commands => commands;

    public Task Connected => connected.Task;

    public Task Exited => exited.Task;

    public bool HasExited => exited.Task.IsCompleted;

    public void Serve() => serving = ServeAsync(stopping.Token);

    public async Task SendAsync(string line)
    {
        await connected.Task;
        Stream stream;
        lock (gate)
        {
            stream = client!;
        }

        await WriteAsync(stream, line + "\r\n");
    }

    public void Exit()
    {
        exited.TrySetResult();
        stopping.Cancel();
        listener.Stop();
        lock (gate)
        {
            client?.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Exit();
        try
        {
            await serving;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
        }

        listener.Dispose();
        stopping.Dispose();
    }

    private static Task WriteAsync(Stream stream, string text) => stream.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptSocketAsync(cancellationToken);
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        lock (gate)
        {
            client = stream;
        }

        connected.TrySetResult();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        while (!cancellationToken.IsCancellationRequested)
        {
            var command = await reader.ReadLineAsync(cancellationToken);
            if (command is null)
            {
                Exit();
                return;
            }

            commands.Enqueue(command);
            if (Silent)
            {
                continue;
            }

            var verb = command.Split(' ', 2)[0];
            if (verb == RefuseCommand)
            {
                await WriteAsync(stream, "552 Refused\r\n");
                continue;
            }

            switch (verb)
            {
                case "PROTOCOLINFO":
                    var named = (NamedCookieFile ?? CookieFile).Replace("\\", "\\\\", StringComparison.Ordinal);
                    await WriteAsync(stream, $"250-PROTOCOLINFO 1\r\n250-AUTH METHODS={Methods} COOKIEFILE=\"{named}\"\r\n250-VERSION Tor=\"0.4.9.16\"\r\n250 OK\r\n");
                    break;
                case "AUTHENTICATE":
                    var given = command.Split(' ', 2)[1];
                    var ok = CryptographicOperations.FixedTimeEquals(Convert.FromHexString(given), Cookie);
                    await WriteAsync(stream, ok ? "250 OK\r\n" : "515 Authentication failed\r\n");
                    break;
                case "GETINFO" when command.EndsWith("status/bootstrap-phase", StringComparison.Ordinal):
                    await WriteAsync(stream, $"250-status/bootstrap-phase={BootstrapPhase}\r\n250 OK\r\n");
                    break;
                case "GETINFO" when command.EndsWith("net/listeners/socks", StringComparison.Ordinal):
                    await WriteAsync(stream, "250-net/listeners/socks=\"127.0.0.1:39050\"\r\n250 OK\r\n");
                    break;
                case "ADD_ONION":
                    await WriteAsync(stream, ServiceId is null ? "250 OK\r\n" : string.Create(CultureInfo.InvariantCulture, $"250-ServiceID={ServiceId}\r\n250 OK\r\n"));
                    break;
                case "SIGNAL" when command == "SIGNAL SHUTDOWN":
                    await WriteAsync(stream, "250 OK\r\n");
                    if (ExitOnShutdown)
                    {
                        Exit();
                        return;
                    }

                    break;
                default:
                    await WriteAsync(stream, "250 OK\r\n");
                    break;
            }
        }
    }
}
