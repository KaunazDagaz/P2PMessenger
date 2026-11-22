using P2PMessenger.Application.LocalData;

namespace P2PMessenger.UI.Services;

public sealed class AppPlatform
{
    public AppPlatform(string? dataFolder, IKeyProtector? keyProtector)
        : this(dataFolder, keyProtector, null)
    {
    }

    public AppPlatform(string? dataFolder, IKeyProtector? keyProtector, Action<bool>? passphraseSetChanged)
    {
        DataFolder = dataFolder;
        KeyProtector = keyProtector;
        PassphraseSetChanged = passphraseSetChanged;
    }

    public static AppPlatform WithoutKeyStore { get; } = new(null, null);

    public string? DataFolder { get; }

    public IKeyProtector? KeyProtector { get; }

    public Action<bool>? PassphraseSetChanged { get; }

    public ILocalData CreateLocalData() =>
        DataFolder is { Length: > 0 } folder
            ? new DatabaseLocalData(Path.Combine(folder, DatabaseLocalData.FileName), KeyProtector)
            : new InMemoryLocalData(LocalDataMode.MemoryOnlyNoKeyStore);
}
