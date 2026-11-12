using Microsoft.Data.Sqlite;

namespace P2PMessenger.Storage;

public static class SqliteDatabase
{
    public static SqliteConnectionStringBuilder ConnectionStringFor(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        };
    }

    public static async Task<SqliteConnection> OpenAsync(SqliteConnectionStringBuilder connectionString, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        var connection = new SqliteConnection(connectionString.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA secure_delete = ON;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
