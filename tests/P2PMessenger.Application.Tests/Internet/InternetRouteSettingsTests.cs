using P2PMessenger.Application.Background;
using P2PMessenger.Application.Internet;

namespace P2PMessenger.Application.Tests.Internet;

[Trait("Story", "US-09")]
public sealed class InternetRouteSettingsTests
{
    [Fact]
    public void UseTor_NothingStored_IsOn()
    {
        Assert.True(new InternetRouteSettings(new InMemorySettingsStore()).UseTor);
    }

    [Fact]
    public void UseTor_TurnedOff_IsRememberedByTheStore()
    {
        var store = new InMemorySettingsStore();

        new InternetRouteSettings(store).UseTor = false;

        Assert.False(new InternetRouteSettings(store).UseTor);
        Assert.False(store.ReadFlag(InternetRouteSettings.UseTorKey, true));
    }

    [Fact]
    public void Reset_TurnsTorBackOn()
    {
        var store = new InMemorySettingsStore();
        var settings = new InternetRouteSettings(store) { UseTor = false };

        settings.Reset();

        Assert.True(settings.UseTor);
    }
}
