using Microsoft.Data.Sqlite;
using P2PMessenger.Storage.Protection;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Storage;

public sealed class MessengerDatabase : IAsyncDisposable
{
    private const int NotADatabaseError = 26;
    private const int CorruptError = 11;

    private const string SelectDataKey = "SELECT protection, wrapped_key FROM data_key WHERE id = 1;";
    private const string InsertDataKey = "INSERT INTO data_key (id, protection, wrapped_key) VALUES (1, $protection, $wrapped_key);";

    private const string SelectAnyData =
        "SELECT EXISTS (SELECT 1 FROM person) OR EXISTS (SELECT 1 FROM seen_message) "
        + "OR EXISTS (SELECT 1 FROM chat_group) OR EXISTS (SELECT 1 FROM copied_conversation);";

    private readonly SqliteGate gate;

    private MessengerDatabase(SqliteConnection connection)
    {
        gate = new SqliteGate(connection);
    }

    public static async Task<DatabaseOpenResult> OpenAsync(string path, CancellationToken cancellationToken)
    {
        SqliteConnectionStringBuilder connectionString = SqliteDatabase.ConnectionStringFor(path);
        connectionString.Pooling = false;

        SqliteConnection? connection = null;
        try
        {
            connection = await SqliteDatabase.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<Migration> migrations = MigrationCatalog.Load();
            int version = await MigrationRunner.ReadVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version > migrations.Count)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return new DatabaseOpenResult(null, DatabaseOpenOutcome.NewerThanApp);
            }

            await MigrationRunner.MigrateAsync(connection, migrations, cancellationToken).ConfigureAwait(false);
            return new DatabaseOpenResult(new MessengerDatabase(connection), DatabaseOpenOutcome.Opened);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is NotADatabaseError or CorruptError)
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            return new DatabaseOpenResult(null, DatabaseOpenOutcome.NotADatabase);
        }
        catch
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    public Task<WrappedDataKey?> ReadDataKeyAsync(CancellationToken cancellationToken) =>
        gate.ReadAsync<WrappedDataKey?>(
            async (connection, token) =>
            {
                using SqliteCommand command = Sql.Command(connection, null, SelectDataKey);
                using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    return null;
                }

                DataKeyProtection protection = reader.GetInt64(0) switch
                {
                    1 => DataKeyProtection.Passphrase,
                    2 => DataKeyProtection.WindowsDpapi,
                    3 => DataKeyProtection.AndroidKeystore,
                    4 => DataKeyProtection.LinuxSecretService,
                    _ => throw new StoredDataException("The data key row names an unknown protection."),
                };

                return new WrappedDataKey(protection, reader.GetFieldValue<byte[]>(1));
            },
            cancellationToken);

    public async Task<bool> HoldsDataAsync(CancellationToken cancellationToken) =>
        await gate.ReadAsync((connection, token) => Sql.LongAsync(connection, null, SelectAnyData, token), cancellationToken).ConfigureAwait(false) == 1;

    public Task SaveDataKeyAsync(WrappedDataKey dataKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        if (dataKey.Protection == DataKeyProtection.Passphrase)
        {
            throw new ArgumentException("A data key wrapped by a passphrase needs its key-derivation parameters, which this method does not store.", nameof(dataKey));
        }

        return gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                await Sql.ExecuteAsync(
                    connection,
                    transaction,
                    InsertDataKey,
                    token,
                    ("$protection", (long)dataKey.Protection),
                    ("$wrapped_key", dataKey.WrappedKey.ToArray())).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    public StoredData Unlock(ReadOnlySpan<byte> dataKey) => new(gate, RecordKeys.Derive(dataKey));

    public ValueTask DisposeAsync() => gate.DisposeAsync();
}
