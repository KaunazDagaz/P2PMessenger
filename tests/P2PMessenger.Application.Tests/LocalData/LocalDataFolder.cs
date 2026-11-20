using System.Globalization;

namespace P2PMessenger.Application.Tests.LocalData;

internal sealed class LocalDataFolder : IDisposable
{
    public LocalDataFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), string.Create(CultureInfo.InvariantCulture, $"p2pmessenger-local-data-test-{Guid.NewGuid():N}"));
        DatabasePath = Path.Combine(Root, "data", "messenger.db");
    }

    public string Root { get; }

    public string DatabasePath { get; }

    public IReadOnlyList<string> Files() =>
        Directory.Exists(Root) ? [.. Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)] : [];

    public string CopyTo(LocalDataFolder target)
    {
        var from = Path.GetDirectoryName(DatabasePath)!;
        var to = Path.GetDirectoryName(target.DatabasePath)!;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        return target.DatabasePath;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
