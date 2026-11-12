using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace P2PMessenger.Storage.Tests;

internal static class Sql
{
    public static async Task<int> ExecuteAsync(SqliteConnection connection, string commandText, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellationToken)
    {
        using var command = Create(connection, commandText, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<long> ScalarAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        using var command = Create(connection, commandText, []);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public static async Task<string?> TextAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        using var command = Create(connection, commandText, []);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    public static async Task<List<string>> StringsAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        using var command = Create(connection, commandText, []);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The tests pass constant SQL; values go in as parameters.")]
    private static SqliteCommand Create(SqliteConnection connection, string commandText, IReadOnlyList<(string Name, object Value)> parameters)
    {
        var command = connection.CreateCommand();
        try
        {
            command.CommandText = commandText;
            foreach (var (name, value) in parameters)
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
}
