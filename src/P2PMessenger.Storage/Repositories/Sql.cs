using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace P2PMessenger.Storage.Repositories;

internal static class Sql
{
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Every caller passes a constant statement of this assembly; values go in as parameters.")]
    public static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string text, params ReadOnlySpan<(string Name, object Value)> parameters)
    {
        SqliteCommand command = connection.CreateCommand();
        try
        {
            command.Transaction = transaction;
            command.CommandText = text;
            foreach ((string name, object value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return command;
        }
        catch
        {
            command.Dispose();
            throw;
        }
    }

    public static async Task<long?> LongAsync(SqliteConnection connection, SqliteTransaction? transaction, string text, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = Command(connection, transaction, text, parameters);
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string text, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = Command(connection, transaction, text, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
