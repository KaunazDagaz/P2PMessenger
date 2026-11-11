namespace P2PMessenger.Spikes.AndroidHead.Head;

internal static class AndroidServices
{
    public static SpikeServices Create(Application application)
    {
        var permissions = new AndroidPermissions(application);
        var background = new AndroidBackgroundService(application);
        return new SpikeServices(
            new AndroidDeviceInfo(application),
            permissions,
            background,
            new AndroidQrScanner(application, permissions),
            new AndroidTorRuntime(application, background));
    }
}
