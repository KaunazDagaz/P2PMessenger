using System.Globalization;
using System.Net;
using System.Text;

namespace P2PMessenger.Transport.Tor;

public sealed class OnionTarget
{
    public const int MaxSocketPathLength = 100;

    private OnionTarget(string? socketPath, int port)
    {
        SocketPath = socketPath;
        Port = port;
    }

    public string? SocketPath { get; }

    public int Port { get; }

    public bool IsUnixSocket => SocketPath is not null;

    public static OnionTarget UnixSocket(string path)
    {
        if (!IsUsableSocketPath(path))
        {
            throw new ArgumentException("The socket path is not absolute, is too long or has characters that tor cannot take.", nameof(path));
        }

        return new OnionTarget(path, 0);
    }

    public static OnionTarget Loopback(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, IPEndPoint.MinPort + 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);
        return new OnionTarget(null, port);
    }

    public static bool IsUsableSocketPath(string? path) =>
        path is { Length: > 1 }
        && path[0] == '/'
        && Encoding.UTF8.GetByteCount(path) <= MaxSocketPathLength
        && path.All(static character => character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '/' or '.' or '_' or '-');

    internal string ToPortArgument(int virtualPort) =>
        SocketPath is { } path
            ? string.Create(CultureInfo.InvariantCulture, $"{virtualPort},unix:{path}")
            : string.Create(CultureInfo.InvariantCulture, $"{virtualPort},127.0.0.1:{Port}");
}
