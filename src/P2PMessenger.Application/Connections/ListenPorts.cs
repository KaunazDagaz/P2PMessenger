using System.Net.Sockets;

namespace P2PMessenger.Application.Connections;

internal static class ListenPorts
{
    public static (T? Listener, ListenResult Result) StartOnRandomPort<T>(Func<int> nextPort, Func<int, T> start, int attempts)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(nextPort);
        ArgumentNullException.ThrowIfNull(start);

        ListenResult last = ListenResult.Failed;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                return (start(nextPort()), ListenResult.Listening);
            }
            catch (SocketException exception)
            {
                last = ResultOf(exception.SocketErrorCode);
                if (!IsUnavailable(exception.SocketErrorCode))
                {
                    break;
                }
            }
        }

        return (null, last);
    }

    public static ListenResult ResultOf(SocketError error) =>
        error == SocketError.AddressAlreadyInUse ? ListenResult.PortInUse : ListenResult.Failed;

    public static bool IsUnavailable(SocketError error) =>
        error is SocketError.AddressAlreadyInUse or SocketError.AccessDenied;
}
