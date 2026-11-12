using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace P2PMessenger.Storage;

public static class MigrationRunner
{
    public static Task<int> MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken) =>
        MigrateAsync(connection, MigrationCatalog.Load(), cancellationToken);

    public static async Task<int> MigrateAsync(SqliteConnection connection, IReadOnlyList<Migration> migrations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(migrations);

        var version = await ReadVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        var latest = migrations.Count == 0 ? 0 : migrations.Max(item => item.Version);
        if (version > latest)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The database has schema version {version}, newer than version {latest}, the newest this app knows."));
        }

        foreach (var migration in migrations.Where(pending => pending.Version > version).OrderBy(pending => pending.Version))
        {
            await ApplyAsync(connection, migration, cancellationToken).ConfigureAwait(false);
        }

        return latest;
    }

    public static async Task<int> ReadVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var version = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(version, CultureInfo.InvariantCulture);
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The command text is a migration script embedded in this assembly, never user input.")]
    private static async Task ApplyAsync(SqliteConnection connection, Migration migration, CancellationToken cancellationToken)
    {
        using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        using (var script = connection.CreateCommand())
        {
            script.Transaction = transaction;
            script.CommandText = migration.Script;
            await script.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        using (var setVersion = connection.CreateCommand())
        {
            setVersion.Transaction = transaction;
            setVersion.CommandText = string.Create(CultureInfo.InvariantCulture, $"PRAGMA user_version = {migration.Version};");
            await setVersion.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
