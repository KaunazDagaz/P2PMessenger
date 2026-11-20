using P2PMessenger.Application.LocalData;

namespace P2PMessenger.UI.Services;

public sealed class AppPlatform
{
    public AppPlatform(string? dataFolder, IKeyProtector? keyProtector)
    {
        DataFolder = dataFolder;
        KeyProtector = keyProtector;
    }

    public static AppPlatform WithoutKeyStore { get; } = new(null, null);

    public string? DataFolder { get; }

    public IKeyProtector? KeyProtector { get; }

    public ILocalData CreateLocalData() =>
        DataFolder is { Length: > 0 } folder && KeyProtector is { } protector
            ? new DatabaseLocalData(Path.Combine(folder, DatabaseLocalData.FileName), protector)
            : new InMemoryLocalData(LocalDataMode.MemoryOnlyNoKeyStore);
}
