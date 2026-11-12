using System.Globalization;
using Microsoft.Data.Sqlite;

namespace P2PMessenger.Storage.Tests;

internal sealed class TemporaryDatabase : IDisposable
{
    public TemporaryDatabase()
    {
        FilePath = Path.Combine(Path.GetTempPath(), string.Create(CultureInfo.InvariantCulture, $"p2pmessenger-storage-test-{Guid.NewGuid():N}.db"));
        ConnectionString = SqliteDatabase.ConnectionStringFor(FilePath);
        ConnectionString.Pooling = false;
    }

    public string FilePath { get; }

    public SqliteConnectionStringBuilder ConnectionString { get; }

    public Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken) =>
        SqliteDatabase.OpenAsync(ConnectionString, cancellationToken);

    public async Task<SqliteConnection> OpenMigratedAsync(CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        try
        {
            await MigrationRunner.MigrateAsync(connection, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public void Dispose()
    {
        string[] files = [FilePath, FilePath + "-wal", FilePath + "-shm"];
        foreach (var file in files)
        {
            File.Delete(file);
        }

        GC.SuppressFinalize(this);
    }
}
