using P2PMessenger.Core.Messages;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Repositories;

public sealed class SeenMessageRepository : ISeenMessageIds
{
    private const string SelectSeen = "SELECT EXISTS (SELECT 1 FROM seen_message WHERE message_id_mac = $message_id_mac);";
    private const string InsertSeen = "INSERT INTO seen_message (message_id_mac) VALUES ($message_id_mac) ON CONFLICT (message_id_mac) DO NOTHING;";

    private readonly SqliteGate gate;
    private readonly RecordKeys keys;

    internal SeenMessageRepository(SqliteGate gate, RecordKeys keys)
    {
        this.gate = gate;
        this.keys = keys;
    }

    public async ValueTask<bool> ContainsAsync(MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        byte[] lookup = keys.Lookup(LookupDomain.MessageId, id.Bytes);
        long? found = await gate.ReadAsync(
            (connection, token) => Sql.LongAsync(connection, null, SelectSeen, token, ("$message_id_mac", lookup)),
            cancellationToken).ConfigureAwait(false);
        return found == 1;
    }

    public async ValueTask AddAsync(MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        byte[] lookup = keys.Lookup(LookupDomain.MessageId, id.Bytes);
        await gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                await Sql.ExecuteAsync(connection, transaction, InsertSeen, token, ("$message_id_mac", lookup)).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }
}
