using P2PMessenger.Application.Background;
using P2PMessenger.Application.Internet;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.UI.Services;

public sealed class AppPlatform
{
    public const string LogFolderName = "logs";
    public AppPlatform(string? dataFolder, IKeyProtector? keyProtector)
        : this(dataFolder, keyProtector, null)
    {
    }

    public AppPlatform(string? dataFolder, IKeyProtector? keyProtector, Action<bool>? passphraseSetChanged)
        : this(dataFolder, keyProtector, passphraseSetChanged, null)
    {
    }

    public AppPlatform(string? dataFolder, IKeyProtector? keyProtector, Action<bool>? passphraseSetChanged, ILocalInterfaceSource? localInterfaces)
        : this(dataFolder, keyProtector, passphraseSetChanged, localInterfaces, null)
    {
    }

    public AppPlatform(string? dataFolder, IKeyProtector? keyProtector, Action<bool>? passphraseSetChanged, ILocalInterfaceSource? localInterfaces, IQrScanner? qrScanner)
    {
        DataFolder = dataFolder;
        KeyProtector = keyProtector;
        PassphraseSetChanged = passphraseSetChanged;
        LocalInterfaces = localInterfaces;
        QrScanner = qrScanner;
    }

    public static AppPlatform WithoutKeyStore { get; } = new(null, null);

    public string? DataFolder { get; }

    public string? LogFolder => DataFolder is { Length: > 0 } folder ? Path.Combine(folder, LogFolderName) : null;

    public IKeyProtector? KeyProtector { get; }

    public Action<bool>? PassphraseSetChanged { get; }

    public ILocalInterfaceSource? LocalInterfaces { get; }

    public IQrScanner? QrScanner { get; }

    public BackgroundPorts? Background { get; init; }

    public TorSetup? Tor { get; init; }

    public ISettingsStore? Settings { get; init; }

    public bool CanSendPcCopy { get; init; }

    public bool CanReceivePcCopy { get; init; }

    public ILocalData CreateLocalData() =>
        DataFolder is { Length: > 0 } folder
            ? new DatabaseLocalData(Path.Combine(folder, DatabaseLocalData.FileName), KeyProtector)
            : new InMemoryLocalData(LocalDataMode.MemoryOnlyNoKeyStore);
}
