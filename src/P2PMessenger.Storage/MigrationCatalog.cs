using System.Globalization;
using System.Reflection;
using System.Text;

namespace P2PMessenger.Storage;

public static class MigrationCatalog
{
    private const string ResourcePrefix = "P2PMessenger.Storage.Migrations.";
    private const string ResourceSuffix = ".sql";

    public static IReadOnlyList<Migration> Load()
    {
        var assembly = typeof(MigrationCatalog).Assembly;
        var migrations = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Select(name => Read(assembly, name))
            .OrderBy(migration => migration.Version)
            .ToList();

        for (var index = 0; index < migrations.Count; index++)
        {
            var expected = index + 1;
            if (migrations[index].Version != expected)
            {
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Migration {migrations[index].Name} has version {migrations[index].Version}, but version {expected} was expected: migrations are numbered from 1 without gaps."));
            }
        }

        return migrations;
    }

    private static Migration Read(Assembly assembly, string resourceName)
    {
        var name = resourceName[ResourcePrefix.Length..^ResourceSuffix.Length];
        if (name.Length < 6 || name[4] != '_' || !int.TryParse(name.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
        {
            throw new InvalidOperationException($"The migration resource {resourceName} is not named NNNN_name.sql.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The migration resource {resourceName} cannot be read.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return new Migration(version, name, reader.ReadToEnd());
    }
}
