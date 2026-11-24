using P2PMessenger.Application.Background;

namespace P2PMessenger.Android.Background;

public sealed class BackgroundState
{
    public const string FileName = "p2pm-background";
    public const string WantedKey = "service.wanted";

    private readonly ISettingsStore flags;

    public BackgroundState(ISettingsStore flags)
    {
        ArgumentNullException.ThrowIfNull(flags);

        this.flags = flags;
    }

    public bool IsWanted
    {
        get => flags.ReadFlag(WantedKey, false);
        set => flags.WriteFlag(WantedKey, value);
    }
}
