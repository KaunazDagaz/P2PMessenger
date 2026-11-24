namespace P2PMessenger.Transport.Tor;

internal interface ITorProcessStarter
{
    ITorProcess Start(TorLaunch launch);
}
