using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Transport.Tor;

internal sealed class TorControlClient : IAsyncDisposable
{
    public const int CookieLength = 32;

    private static readonly byte[] LineEnd = "\r\n"u8.ToArray();

    private readonly Stream stream;
    private readonly ControlReplyReader reader;
    private readonly Action<ControlReply> onEvent;
    private readonly TimeSpan commandTimeout;
    private readonly SemaphoreSlim commandGate = new(1, 1);
    private readonly CancellationTokenSource closing = new();
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock pendingGate = new();
    private TaskCompletionSource<ControlReply>? pending;
    private Task readLoop = Task.CompletedTask;
    private volatile bool broken;
    private int disposed;

    private TorControlClient(Stream stream, Action<ControlReply> onEvent, TimeSpan commandTimeout)
    {
        this.stream = stream;
        this.onEvent = onEvent;
        this.commandTimeout = commandTimeout;
        reader = new ControlReplyReader(stream);
    }

    public Task Closed => closed.Task;

    public static TorControlClient Over(Stream stream, Action<ControlReply> onEvent, TimeSpan commandTimeout)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(onEvent);
        TorControlClient client = new(stream, onEvent, commandTimeout);
        client.readLoop = client.ReadLoopAsync();
        return client;
    }

    public static async Task<TorControlClient> ConnectAsync(IPEndPoint endpoint, Action<ControlReply> onEvent, TimeSpan commandTimeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!IPAddress.IsLoopback(endpoint.Address))
        {
            throw new TorControlException("The control port must be on this device.");
        }

        Socket socket = new(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return Over(new NetworkStream(socket, ownsSocket: true), onEvent, commandTimeout);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool SamePath(string first, string second)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), comparison);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public async Task AuthenticateAsync(string cookieFile, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(cookieFile);

        ControlReply info = await SendCheckedAsync("PROTOCOLINFO 1", cancellationToken).ConfigureAwait(false);
        bool offersCookie = false;
        string? namedFile = null;
        foreach (ControlReplyLine line in info.Lines)
        {
            if (!line.Text.StartsWith("AUTH ", StringComparison.Ordinal)
                || !ControlSyntax.TryParseArguments(line.Text, out _, out Dictionary<string, string>? keywords))
            {
                continue;
            }

            if (keywords.TryGetValue("METHODS", out string? methods))
            {
                offersCookie = methods.Split(',').Contains("COOKIE", StringComparer.Ordinal);
            }

            namedFile = keywords.GetValueOrDefault("COOKIEFILE");
        }

        if (!offersCookie)
        {
            throw new TorControlException("The control port does not offer cookie authentication.");
        }

        if (namedFile is null || !SamePath(namedFile, cookieFile))
        {
            throw new TorControlException("The control port names another cookie file, so it is not the tor this app started.");
        }

        byte[] cookie = await ReadCookieAsync(cookieFile, cancellationToken).ConfigureAwait(false);
        byte[] command = new byte[13 + (2 * CookieLength)];
        try
        {
            "AUTHENTICATE "u8.CopyTo(command);
            if (!Convert.TryToHexString(cookie, command.AsSpan(13), out int written) || written != 2 * CookieLength)
            {
                throw new TorControlException("The control cookie could not be encoded.");
            }

            await SendCheckedAsync(command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cookie);
            CryptographicOperations.ZeroMemory(command);
        }
    }

    public Task TakeOwnershipAsync(CancellationToken cancellationToken) =>
        SendCheckedAsync("TAKEOWNERSHIP", cancellationToken);

    public Task SetEventsAsync(IEnumerable<string> names, CancellationToken cancellationToken) =>
        SendCheckedAsync("SETEVENTS " + string.Join(' ', names), cancellationToken);

    public async Task<string?> GetInfoAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ControlReply reply = await SendCheckedAsync("GETINFO " + key, cancellationToken).ConfigureAwait(false);
        string prefix = key + "=";
        foreach (ControlReplyLine line in reply.Lines)
        {
            if (line.Text.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line.Data ?? line.Text[prefix.Length..];
            }
        }

        return null;
    }

    public async Task<string> AddOnionAsync(ReadOnlyMemory<byte> expandedKey, int virtualPort, OnionTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (expandedKey.Length != OnionServiceKey.ExpandedLength)
        {
            throw new ArgumentException("The onion service key has the wrong length.", nameof(expandedKey));
        }

        byte[] prefix = "ADD_ONION ED25519-V3:"u8.ToArray();
        byte[] suffix = Encoding.ASCII.GetBytes(" Port=" + target.ToPortArgument(virtualPort));
        int encodedLength = Base64.GetMaxEncodedToUtf8Length(expandedKey.Length);
        byte[] command = new byte[prefix.Length + encodedLength + suffix.Length];
        ControlReply reply;
        try
        {
            prefix.CopyTo(command, 0);
            if (Base64.EncodeToUtf8(expandedKey.Span, command.AsSpan(prefix.Length, encodedLength), out _, out int written) != OperationStatus.Done
                || written != encodedLength)
            {
                throw new TorControlException("The onion service key could not be encoded.");
            }

            suffix.CopyTo(command, prefix.Length + encodedLength);
            reply = await SendCheckedAsync(command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(command);
        }

        foreach (ControlReplyLine line in reply.Lines)
        {
            if (line.Text.StartsWith("ServiceID=", StringComparison.Ordinal))
            {
                string serviceId = line.Text["ServiceID=".Length..];
                if (TorEvents.IsServiceId(serviceId))
                {
                    return serviceId;
                }
            }
        }

        throw new TorControlException("ADD_ONION returned no valid ServiceID.");
    }

    public Task ShutdownAsync(CancellationToken cancellationToken) =>
        SendCheckedAsync("SIGNAL SHUTDOWN", cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await closing.CancelAsync().ConfigureAwait(false);
        await stream.DisposeAsync().ConfigureAwait(false);
        try
        {
            await readLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        closing.Dispose();
        commandGate.Dispose();
    }

    private static async Task<byte[]> ReadCookieAsync(string cookieFile, CancellationToken cancellationToken)
    {
        byte[] cookie;
        try
        {
            FileInfo info = new(cookieFile);
            if (!info.Exists || info.Length != CookieLength)
            {
                throw new TorControlException("The control cookie is missing or has the wrong length.");
            }

            cookie = await File.ReadAllBytesAsync(cookieFile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new TorControlException("The control cookie could not be read.", exception);
        }

        if (cookie.Length != CookieLength)
        {
            CryptographicOperations.ZeroMemory(cookie);
            throw new TorControlException("The control cookie has the wrong length.");
        }

        return cookie;
    }

    private Task<ControlReply> SendCheckedAsync(string command, CancellationToken cancellationToken) =>
        SendCheckedAsync(Encoding.ASCII.GetBytes(command), cancellationToken);

    private async Task<ControlReply> SendCheckedAsync(byte[] command, CancellationToken cancellationToken)
    {
        ControlReply reply = await SendAsync(command, cancellationToken).ConfigureAwait(false);
        if (!reply.IsSuccess)
        {
            throw new TorControlException(string.Create(CultureInfo.InvariantCulture, $"tor refused a control command with status {reply.Status}."));
        }

        return reply;
    }

    private async Task<ControlReply> SendAsync(byte[] command, CancellationToken cancellationToken)
    {
        if (Array.IndexOf(command, (byte)'\r') >= 0 || Array.IndexOf(command, (byte)'\n') >= 0)
        {
            throw new ArgumentException("A control command must be a single line.", nameof(command));
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (broken)
        {
            throw new TorControlException("The control connection is no longer usable.");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, closing.Token);
        timeout.CancelAfter(commandTimeout);
        await commandGate.WaitAsync(timeout.Token).ConfigureAwait(false);
        TaskCompletionSource<ControlReply> waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            lock (pendingGate)
            {
                if (broken)
                {
                    throw new TorControlException("The control connection is no longer usable.");
                }

                pending = waiting;
            }

            await stream.WriteAsync(command, timeout.Token).ConfigureAwait(false);
            await stream.WriteAsync(LineEnd, timeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
            return await waiting.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            MarkBroken();
            throw new TorControlException("tor did not answer a control command in time.");
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            MarkBroken();
            throw new TorControlException("The control connection failed.", exception);
        }
        catch
        {
            MarkBroken();
            throw;
        }
        finally
        {
            lock (pendingGate)
            {
                if (ReferenceEquals(pending, waiting))
                {
                    pending = null;
                }
            }

            commandGate.Release();
        }
    }

    private void MarkBroken()
    {
        broken = true;
    }

    private async Task ReadLoopAsync()
    {
        Exception? failure = null;
        try
        {
            while (true)
            {
                ControlReply? reply = await reader.ReadAsync(closing.Token).ConfigureAwait(false);
                if (reply is null)
                {
                    break;
                }

                if (reply.IsEvent)
                {
                    onEvent(reply);
                    continue;
                }

                TaskCompletionSource<ControlReply>? waiting;
                lock (pendingGate)
                {
                    waiting = pending;
                    pending = null;
                }

                if (waiting is null)
                {
                    throw new TorControlException("tor sent a reply that no command asked for.");
                }

                waiting.TrySetResult(reply);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = exception;
        }
        finally
        {
            TaskCompletionSource<ControlReply>? waiting;
            lock (pendingGate)
            {
                broken = true;
                waiting = pending;
                pending = null;
            }

            waiting?.TrySetException(new TorControlException("The control connection closed.", failure ?? new EndOfStreamException()));
            closed.TrySetResult();
        }
    }
}
