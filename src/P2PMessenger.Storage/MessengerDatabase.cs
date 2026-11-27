using Microsoft.Data.Sqlite;
using P2PMessenger.Storage.Protection;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Storage;

public sealed class MessengerDatabase : IAsyncDisposable
{
    private const int NotADatabaseError = 26;
    private const int CorruptError = 11;

    private const string SelectDataKey = "SELECT protection, wrapped_key, kdf_salt, kdf_memory_kib, kdf_passes FROM data_key WHERE id = 1;";

    private const string InsertDataKey =
        "INSERT INTO data_key (id, protection, wrapped_key, kdf_salt, kdf_memory_kib, kdf_passes) "
        + "VALUES (1, $protection, $wrapped_key, $kdf_salt, $kdf_memory_kib, $kdf_passes);";

    private const string UpdateDataKey =
        "UPDATE data_key SET protection = $protection, wrapped_key = $wrapped_key, kdf_salt = $kdf_salt, "
        + "kdf_memory_kib = $kdf_memory_kib, kdf_passes = $kdf_passes WHERE id = 1;";

    private const string EraseEverything =
        "DELETE FROM copied_message; DELETE FROM copied_conversation; DELETE FROM group_acknowledgement; DELETE FROM message_delivery; DELETE FROM message; "
        + "DELETE FROM conversation; DELETE FROM group_leave_proof; DELETE FROM group_key; DELETE FROM group_member; DELETE FROM chat_group; DELETE FROM contact; "
        + "DELETE FROM identity; DELETE FROM person; DELETE FROM seen_message; DELETE FROM data_key;";

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
                return await reader.ReadAsync(token).ConfigureAwait(false) ? DataKeyOf(reader) : null;
            },
            cancellationToken);

    public async Task<bool> HoldsDataAsync(CancellationToken cancellationToken) =>
        await gate.ReadAsync((connection, token) => Sql.LongAsync(connection, null, SelectAnyData, token), cancellationToken).ConfigureAwait(false) == 1;

    public Task SaveDataKeyAsync(WrappedDataKey dataKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataKey);

        return gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                await Sql.ExecuteAsync(connection, transaction, InsertDataKey, token, DataKeyParameters(dataKey)).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    public async Task ReplaceDataKeyAsync(WrappedDataKey dataKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataKey);

        await gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                if (await Sql.ExecuteAsync(connection, transaction, UpdateDataKey, token, DataKeyParameters(dataKey)).ConfigureAwait(false) != 1)
                {
                    throw new StoredDataException("The data key row is missing.");
                }

                return true;
            },
            cancellationToken).ConfigureAwait(false);

        await gate.TruncateJournalAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EraseAsync(CancellationToken cancellationToken)
    {
        await gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                await Sql.ExecuteAsync(connection, transaction, EraseEverything, token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);

        await gate.TruncateJournalAsync(cancellationToken).ConfigureAwait(false);
    }

    public StoredData Unlock(ReadOnlySpan<byte> dataKey) => new(gate, RecordKeys.Derive(dataKey));

    public ValueTask DisposeAsync() => gate.DisposeAsync();

    private static WrappedDataKey DataKeyOf(SqliteDataReader reader)
    {
        DataKeyProtection protection = reader.GetInt64(0) switch
        {
            1 => DataKeyProtection.Passphrase,
            2 => DataKeyProtection.WindowsDpapi,
            3 => DataKeyProtection.AndroidKeystore,
            4 => DataKeyProtection.LinuxSecretService,
            _ => throw new StoredDataException("The data key row names an unknown protection."),
        };

        byte[] wrappedKey = reader.GetFieldValue<byte[]>(1);
        if (protection != DataKeyProtection.Passphrase)
        {
            return new WrappedDataKey(protection, wrappedKey);
        }

        byte[] salt = reader.GetFieldValue<byte[]>(2);
        PassphraseKdf kdf = new(reader.GetInt64(3), reader.GetInt64(4));
        if (salt.Length != PassphraseDerivation.SaltLength || !kdf.IsAcceptable || !PassphraseKeyWrap.IsWellFormed(wrappedKey))
        {
            throw new StoredDataException("The passphrase settings of the data key are outside the accepted range.");
        }

        return new WrappedDataKey(protection, wrappedKey, new PassphraseDerivation(salt, kdf));
    }

    private static (string Name, object Value)[] DataKeyParameters(WrappedDataKey dataKey)
    {
        PassphraseDerivation? derivation = dataKey.Derivation;
        return
        [
            ("$protection", (long)dataKey.Protection),
            ("$wrapped_key", dataKey.WrappedKey.ToArray()),
            ("$kdf_salt", derivation is null ? DBNull.Value : derivation.Salt.ToArray()),
            ("$kdf_memory_kib", derivation is null ? DBNull.Value : derivation.Kdf.MemoryKib),
            ("$kdf_passes", derivation is null ? DBNull.Value : derivation.Kdf.Passes),
        ];
    }
}
