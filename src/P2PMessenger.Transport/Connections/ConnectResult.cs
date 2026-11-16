using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Transport.Connections;

public readonly record struct ConnectResult(ContactConnection? Connection, ConnectFailure Failure)
{
    [MemberNotNullWhen(true, nameof(Connection))]
    public bool IsConnected => Connection is not null;

    internal static ConnectResult Connected(ContactConnection connection) => new(connection, ConnectFailure.None);

    internal static ConnectResult Failed(ConnectFailure failure) => new(null, failure);
}
