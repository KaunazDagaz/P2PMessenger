using P2PMessenger.Application.Background;

namespace P2PMessenger.Application.Internet;

public sealed class InternetRouteSettings
{
    public const string UseTorKey = "internet.tor";

    private readonly ISettingsStore store;

    public InternetRouteSettings(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        this.store = store;
    }

    public bool UseTor
    {
        get => store.ReadFlag(UseTorKey, true);
        set => store.WriteFlag(UseTorKey, value);
    }

    public void Reset() => store.Clear();
}
