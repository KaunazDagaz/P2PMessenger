namespace P2PMessenger.Application.Background;

public sealed class BackgroundSettings
{
    public const string PreviewsKey = "notifications.previews";
    public const string OfferedKey = "background.offered";

    private readonly ISettingsStore store;

    public BackgroundSettings(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        this.store = store;
    }

    public bool ShowPreviews
    {
        get => store.ReadFlag(PreviewsKey, false);
        set => store.WriteFlag(PreviewsKey, value);
    }

    public bool WasOffered
    {
        get => store.ReadFlag(OfferedKey, false);
        set => store.WriteFlag(OfferedKey, value);
    }

    public void Reset() => store.Clear();
}
