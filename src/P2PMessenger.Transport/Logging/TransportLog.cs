using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using P2PMessenger.Transport.Admission;
using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Transport.Logging;

internal static partial class TransportLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Listening for connections on port {Port}")]
    public static partial void Listening(this ILogger logger, int port);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Accepting a connection failed with {Error}")]
    public static partial void AcceptFailed(this ILogger logger, SocketError error);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Inbound connection {Number} was not admitted: {Refusal}")]
    public static partial void NotAdmitted(this ILogger logger, long number, AdmissionRefusal refusal);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Inbound connection {Number} was refused: {Refusal}")]
    public static partial void Refused(this ILogger logger, long number, HandshakeRefusal refusal);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "Outbound connection {Number} failed: {Failure}")]
    public static partial void DialFailed(this ILogger logger, long number, ConnectFailure failure);

    [LoggerMessage(EventId = 6, Level = LogLevel.Debug, Message = "Connection {Number} ({Direction}) established in {Milliseconds} ms")]
    public static partial void Established(this ILogger logger, long number, ConnectionDirection direction, long milliseconds);

    [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "Connection {Number} was not kept, because another session with the same contact is preferred")]
    public static partial void NotKept(this ILogger logger, long number);

    [LoggerMessage(EventId = 8, Level = LogLevel.Debug, Message = "Connection {Number} closed: {Reason}")]
    public static partial void Closed(this ILogger logger, long number, ConnectionCloseReason reason);

    [LoggerMessage(EventId = 9, Level = LogLevel.Error, Message = "Connection {Number}: the observer threw {ExceptionType}")]
    public static partial void ObserverFailed(this ILogger logger, long number, string exceptionType);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "Connection {Number}: unexpected {ExceptionType}")]
    public static partial void Unexpected(this ILogger logger, long number, string exceptionType);
}
