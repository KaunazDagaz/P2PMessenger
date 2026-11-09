using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace P2PMessenger.Spikes.Tor;

public sealed class TorControlClient : IAsyncDisposable
{
    private const int CookieLength = 32;
    private const int MaxLineLength = 64 * 1024;

    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly StreamReader _reader;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly Lock _subscribersLock = new();
    private readonly List<Action<TorReply>> _subscribers = [];
    private readonly Channel<TorReply> _replies = Channel.CreateUnbounded<TorReply>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _readLoop;
    private volatile bool _broken;
    private int _disposed;

    private TorControlClient(Socket socket)
    {
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: false);
        _reader = new StreamReader(_stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: false);
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public static async Task<TorControlClient> ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endPoint);

        var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
            return new TorControlClient(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public IDisposable Subscribe(Action<TorReply> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_subscribersLock)
        {
            _subscribers.Add(handler);
        }

        return new Subscription(this, handler);
    }

    public async Task<TorReply> SendAsync(string command, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);
        if (command.AsSpan().ContainsAny('\r', '\n'))
        {
            throw new ArgumentException("A control command must be a single line.", nameof(command));
        }

        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_broken)
        {
            throw new TorControlException("The control connection is no longer usable after an earlier command was cancelled or failed.");
        }

        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var bytes = Encoding.ASCII.GetBytes(command + "\r\n");
        try
        {
            await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            return await _replies.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException exception)
        {
            _broken = true;
            throw new TorControlException("Tor closed the control connection.", exception.InnerException ?? exception);
        }
        catch
        {
            _broken = true;
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            _commandLock.Release();
        }
    }

    public async Task<TorReply> SendCheckedAsync(string command, string label, CancellationToken cancellationToken)
    {
        var reply = await SendAsync(command, cancellationToken).ConfigureAwait(false);
        if (!reply.IsSuccess)
        {
            throw new TorControlException($"{label} failed: {reply.Status} {reply.FirstLine}", reply.Status);
        }

        return reply;
    }

    public async Task<TorProtocolInfo> GetProtocolInfoAsync(CancellationToken cancellationToken)
    {
        var reply = await SendCheckedAsync("PROTOCOLINFO 1", "PROTOCOLINFO", cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> methods = [];
        string? cookieFile = null;
        string? version = null;

        foreach (var line in reply.Lines)
        {
            if (line.Text.StartsWith("AUTH ", StringComparison.Ordinal))
            {
                var arguments = ControlSyntax.ParseKeywordArguments(line.Text["AUTH ".Length..]);
                if (arguments.TryGetValue("METHODS", out var methodList))
                {
                    methods = methodList.Split(',', StringSplitOptions.RemoveEmptyEntries);
                }

                arguments.TryGetValue("COOKIEFILE", out cookieFile);
            }
            else if (line.Text.StartsWith("VERSION ", StringComparison.Ordinal))
            {
                var arguments = ControlSyntax.ParseKeywordArguments(line.Text["VERSION ".Length..]);
                arguments.TryGetValue("Tor", out version);
            }
        }

        return new TorProtocolInfo(methods, cookieFile, version);
    }

    public async Task<TorProtocolInfo> AuthenticateWithCookieAsync(string cookieFile, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(cookieFile);

        var info = await GetProtocolInfoAsync(cancellationToken).ConfigureAwait(false);
        if (!info.Methods.Contains("COOKIE", StringComparer.Ordinal))
        {
            throw new TorControlException("Tor does not offer cookie authentication on this control port.");
        }

        if (info.CookieFile is null || !string.Equals(Path.GetFullPath(info.CookieFile), Path.GetFullPath(cookieFile), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new TorControlException("The control port names a different cookie file than the one this app configured, so it may not be the tor this app started.");
        }

        var cookie = await File.ReadAllBytesAsync(cookieFile, cancellationToken).ConfigureAwait(false);
        try
        {
            if (cookie.Length != CookieLength)
            {
                throw new TorControlException($"The control cookie has {cookie.Length} bytes instead of {CookieLength}.");
            }

            await SendCheckedAsync("AUTHENTICATE " + Convert.ToHexString(cookie), "AUTHENTICATE", cancellationToken).ConfigureAwait(false);
            return info;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cookie);
        }
    }

    public Task TakeOwnershipAsync(CancellationToken cancellationToken) =>
        SendCheckedAsync("TAKEOWNERSHIP", "TAKEOWNERSHIP", cancellationToken);

    public Task SetEventsAsync(IEnumerable<string> eventNames, CancellationToken cancellationToken) =>
        SendCheckedAsync("SETEVENTS " + string.Join(' ', eventNames), "SETEVENTS", cancellationToken);

    public Task SignalAsync(string signal, CancellationToken cancellationToken) =>
        SendCheckedAsync("SIGNAL " + signal, "SIGNAL " + signal, cancellationToken);

    public async Task<IReadOnlyDictionary<string, string>> GetInfoAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        var reply = await SendCheckedAsync("GETINFO " + string.Join(' ', keys), "GETINFO", cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in reply.Lines)
        {
            var separator = line.Text.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            result[line.Text[..separator]] = line.Data ?? line.Text[(separator + 1)..];
        }

        return result;
    }

    public async Task<string> AddOnionAsync(OnionServiceKey key, int virtualPort, IPEndPoint target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfLessThan(virtualPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(virtualPort, 65535);

        var command = string.Create(
            CultureInfo.InvariantCulture,
            $"ADD_ONION {key.ToAddOnionKeyArgument()} Port={virtualPort},{target}");
        var reply = await SendCheckedAsync(command, "ADD_ONION", cancellationToken).ConfigureAwait(false);

        foreach (var line in reply.Lines)
        {
            if (line.Text.StartsWith("ServiceID=", StringComparison.Ordinal))
            {
                return line.Text["ServiceID=".Length..];
            }
        }

        throw new TorControlException("ADD_ONION returned no ServiceID.");
    }

    public Task DeleteOnionAsync(string serviceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceId);
        return SendCheckedAsync("DEL_ONION " + serviceId, "DEL_ONION", cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }

        _socket.Dispose();
        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _reader.Dispose();
        await _stream.DisposeAsync().ConfigureAwait(false);
        _shutdown.Dispose();
        _commandLock.Dispose();
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            var lines = new List<TorReplyLine>();
            while (true)
            {
                var line = await _reader.ReadLineAsync(_shutdown.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (line.Length < 4 || line.Length > MaxLineLength || !int.TryParse(line.AsSpan(0, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var status))
                {
                    throw new TorControlException("Tor sent a malformed control reply line.");
                }

                var separator = line[3];
                string? data = null;
                if (separator == '+')
                {
                    data = await ReadDataBlockAsync().ConfigureAwait(false);
                }
                else if (separator is not '-' and not ' ')
                {
                    throw new TorControlException("Tor sent a control reply line with an unknown separator.");
                }

                lines.Add(new TorReplyLine(line[4..], data));
                if (separator != ' ')
                {
                    continue;
                }

                var reply = new TorReply(status, lines);
                lines = [];
                if (reply.IsEvent)
                {
                    Publish(reply);
                }
                else
                {
                    await _replies.Writer.WriteAsync(reply, _shutdown.Token).ConfigureAwait(false);
                }
            }

            _replies.Writer.TryComplete(new TorControlException("Tor closed the control connection."));
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException or TorControlException)
        {
            _replies.Writer.TryComplete(exception);
        }
    }

    private async Task<string> ReadDataBlockAsync()
    {
        var builder = new StringBuilder();
        while (true)
        {
            var line = await _reader.ReadLineAsync(_shutdown.Token).ConfigureAwait(false)
                ?? throw new TorControlException("Tor closed the control connection inside a data reply.");

            if (line == ".")
            {
                return builder.ToString();
            }

            if (builder.Length + line.Length > MaxLineLength)
            {
                throw new TorControlException("Tor sent a data reply above the size limit.");
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(line.StartsWith('.') ? line[1..] : line);
        }
    }

    private void Publish(TorReply reply)
    {
        Action<TorReply>[] handlers;
        lock (_subscribersLock)
        {
            handlers = [.. _subscribers];
        }

        foreach (var handler in handlers)
        {
            handler(reply);
        }
    }

    private void Unsubscribe(Action<TorReply> handler)
    {
        lock (_subscribersLock)
        {
            _subscribers.Remove(handler);
        }
    }

    private sealed class Subscription(TorControlClient owner, Action<TorReply> handler) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Unsubscribe(handler);
            }
        }
    }
}

public sealed record TorProtocolInfo(IReadOnlyList<string> Methods, string? CookieFile, string? TorVersion);
