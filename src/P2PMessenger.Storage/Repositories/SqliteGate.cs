using Microsoft.Data.Sqlite;

namespace P2PMessenger.Storage.Repositories;

internal sealed class SqliteGate : IAsyncDisposable
{
    private const string TruncateJournal = "PRAGMA wal_checkpoint(TRUNCATE);";

    private readonly SqliteConnection connection;
    private readonly SemaphoreSlim gate = new(1, 1);

    public SqliteGate(SqliteConnection connection)
    {
        this.connection = connection;
    }

    public async Task<T> ReadAsync<T>(Func<SqliteConnection, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await read(connection, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<T> WriteAsync<T>(Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<T>> write, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            T result = await write(connection, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task TruncateJournalAsync(CancellationToken cancellationToken) =>
        ReadAsync(
            async (open, token) =>
            {
                await Sql.ExecuteAsync(open, null, TruncateJournal, token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }
}
