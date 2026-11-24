using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using P2PMessenger.Transport.Admission;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Tor;

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

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Looking for contacts on the local network on UDP port {Port}")]
    public static partial void DiscoveryStarted(this ILogger logger, int port);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "UDP port {Port} could not be opened ({Error}); announcements are still sent, but none are heard")]
    public static partial void DiscoveryUnavailable(this ILogger logger, int port, SocketError error);

    [LoggerMessage(EventId = 13, Level = LogLevel.Debug, Message = "The local network changed: {Count} local interfaces, other networks: {HasOtherNetwork}")]
    public static partial void NetworkChanged(this ILogger logger, int count, bool hasOtherNetwork);

    [LoggerMessage(EventId = 14, Level = LogLevel.Debug, Message = "An announcement could not be sent: {Error}")]
    public static partial void AnnouncementNotSent(this ILogger logger, SocketError error);

    [LoggerMessage(EventId = 15, Level = LogLevel.Debug, Message = "No contact heard for a while: sweeping {Hosts} addresses (subnets of at most {MaxHosts} hosts)")]
    public static partial void SweepStarted(this ILogger logger, int hosts, int maxHosts);

    [LoggerMessage(EventId = 16, Level = LogLevel.Debug, Message = "The sweep sent {Sent} datagrams in {Milliseconds} ms")]
    public static partial void SweepFinished(this ILogger logger, int sent, long milliseconds);

    [LoggerMessage(EventId = 17, Level = LogLevel.Debug, Message = "{Count} announcements were not screened because of the screening limit")]
    public static partial void ScreeningLimited(this ILogger logger, long count);

    [LoggerMessage(EventId = 18, Level = LogLevel.Error, Message = "Discovery: unexpected {ExceptionType}")]
    public static partial void DiscoveryUnexpected(this ILogger logger, string exceptionType);

    [LoggerMessage(EventId = 19, Level = LogLevel.Information, Message = "Starting tor, attempt {Attempt}")]
    public static partial void TorStarting(this ILogger logger, int attempt);

    [LoggerMessage(EventId = 20, Level = LogLevel.Warning, Message = "tor could not be started or controlled: {Failure}")]
    public static partial void TorStartFailed(this ILogger logger, TorStartFailure failure);

    [LoggerMessage(EventId = 21, Level = LogLevel.Information, Message = "tor is controlled after {Milliseconds} ms; the onion service was added")]
    public static partial void TorControlled(this ILogger logger, long milliseconds);

    [LoggerMessage(EventId = 22, Level = LogLevel.Information, Message = "Internet route: {State}, {Progress} %, problem {Problem}, onion service published: {Published}")]
    public static partial void TorStatusChanged(this ILogger logger, TorState state, int progress, TorProblem problem, bool published);

    [LoggerMessage(EventId = 23, Level = LogLevel.Warning, Message = "tor stopped (exit code {ExitCode}); it is started again in {Seconds} s")]
    public static partial void TorStopped(this ILogger logger, int? exitCode, double seconds);

    [LoggerMessage(EventId = 24, Level = LogLevel.Information, Message = "Listening for onion connections on a {Kind}")]
    public static partial void OnionListening(this ILogger logger, OnionTargetKind kind);

    [LoggerMessage(EventId = 25, Level = LogLevel.Warning, Message = "The onion socket could not be opened ({Error}); a loopback port is used instead")]
    public static partial void OnionSocketUnavailable(this ILogger logger, SocketError error);
}
