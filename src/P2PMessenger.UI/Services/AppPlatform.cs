using P2PMessenger.Application.LocalData;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.UI.Services;

public sealed class AppPlatform
{
    public AppPlatform(string? dataFolder, IKeyProtector? keyProtector)
        : this(dataFolder, keyProtector, null)
    {
    }

    public AppPlatform(string? dataFolder, IKeyProtector? keyProtector, Action<bool>? passphraseSetChanged)
        : this(dataFolder, keyProtector, passphraseSetChanged, null)
    {
    }

    public AppPlatform(string? dataFolder, IKeyProtector? keyProtector, Action<bool>? passphraseSetChanged, ILocalInterfaceSource? localInterfaces)
    {
        DataFolder = dataFolder;
        KeyProtector = keyProtector;
        PassphraseSetChanged = passphraseSetChanged;
        LocalInterfaces = localInterfaces;
    }

    public static AppPlatform WithoutKeyStore { get; } = new(null, null);

    public string? DataFolder { get; }

    public IKeyProtector? KeyProtector { get; }

    public Action<bool>? PassphraseSetChanged { get; }

    public ILocalInterfaceSource? LocalInterfaces { get; }

    public ILocalData CreateLocalData() =>
        DataFolder is { Length: > 0 } folder
            ? new DatabaseLocalData(Path.Combine(folder, DatabaseLocalData.FileName), KeyProtector)
            : new InMemoryLocalData(LocalDataMode.MemoryOnlyNoKeyStore);
}
