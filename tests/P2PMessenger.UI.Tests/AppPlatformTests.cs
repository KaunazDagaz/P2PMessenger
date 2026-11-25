using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.Tests;

public sealed class AppPlatformTests
{
    [Fact]
    public void LogFolder_PlatformWithADataFolder_IsTheLogsFolderInsideIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), "p2pm-platform");

        var platform = new AppPlatform(folder, null);

        Assert.Equal(Path.Combine(folder, "logs"), platform.LogFolder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LogFolder_PlatformWithoutADataFolder_IsNone(string? dataFolder) =>
        Assert.Null(new AppPlatform(dataFolder, null).LogFolder);

    [Fact]
    public void LogFolder_PlatformWithoutKeyStore_IsNone() =>
        Assert.Null(AppPlatform.WithoutKeyStore.LogFolder);
}
