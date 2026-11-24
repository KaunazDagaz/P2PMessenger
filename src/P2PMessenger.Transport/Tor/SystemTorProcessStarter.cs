namespace P2PMessenger.Transport.Tor;

internal sealed class SystemTorProcessStarter : ITorProcessStarter
{
    public static SystemTorProcessStarter Instance { get; } = new();

    public ITorProcess Start(TorLaunch launch) => SystemTorProcess.Start(launch);
}
