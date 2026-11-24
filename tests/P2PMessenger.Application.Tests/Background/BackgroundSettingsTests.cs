using P2PMessenger.Application.Background;

namespace P2PMessenger.Application.Tests.Background;

public sealed class BackgroundSettingsTests
{
    [Fact]
    [Trait("Story", "US-19")]
    public void ShowPreviews_ByDefault_IsOff()
    {
        var settings = new BackgroundSettings(new MemorySettings());

        Assert.False(settings.ShowPreviews);
        Assert.False(settings.WasOffered);
    }

    [Fact]
    [Trait("Story", "US-19")]
    public void ShowPreviews_TurnedOn_IsStored()
    {
        var store = new MemorySettings();
        var settings = new BackgroundSettings(store) { ShowPreviews = true, WasOffered = true };

        Assert.True(new BackgroundSettings(store).ShowPreviews);
        Assert.True(store.ReadFlag(BackgroundSettings.PreviewsKey, false));
        Assert.True(store.ReadFlag(BackgroundSettings.OfferedKey, false));
        Assert.True(settings.WasOffered);
    }

    [Fact]
    [Trait("Story", "US-21")]
    public void Reset_AfterChanges_ReturnsToTheDefaults()
    {
        var store = new MemorySettings();
        var settings = new BackgroundSettings(store) { ShowPreviews = true, WasOffered = true };

        settings.Reset();

        Assert.False(settings.ShowPreviews);
        Assert.False(settings.WasOffered);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Constructor_NoStore_Throws() => Assert.Throws<ArgumentNullException>(() => new BackgroundSettings(null!));
}
