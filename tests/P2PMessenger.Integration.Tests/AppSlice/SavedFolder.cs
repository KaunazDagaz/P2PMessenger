using System.Globalization;

namespace P2PMessenger.Integration.Tests.AppSlice;

internal sealed class SavedFolder : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), string.Create(CultureInfo.InvariantCulture, $"p2pmessenger-delivery-test-{Guid.NewGuid():N}"));

    public string DatabasePath(string app) => Path.Combine(folder, app, "messenger.db");

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
