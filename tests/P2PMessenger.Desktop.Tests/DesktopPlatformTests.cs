using P2PMessenger.Application.LocalData;
using P2PMessenger.Desktop.KeyStores;

namespace P2PMessenger.Desktop.Tests;

public sealed class DesktopPlatformTests
{
    [Fact]
    public void DataFolder_NoOption_IsTheAppFolderInTheUsersLocalData()
    {
        var folder = DesktopPlatform.DataFolder([]);

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        Assert.Equal(Path.Combine(local, "P2PMessenger"), folder);
    }

    [Theory]
    [InlineData("--data", "second-app")]
    [InlineData("--data=second-app")]
    public void DataFolder_DataOption_IsTheGivenFolder(params string[] arguments)
    {
        var folder = DesktopPlatform.DataFolder(arguments);

        Assert.Equal(Path.GetFullPath("second-app"), folder);
    }

    [Theory]
    [InlineData("--data")]
    [InlineData("--data", " ")]
    [InlineData("--data=")]
    [InlineData("--other", "x")]
    public void DataFolder_OptionWithoutAFolder_IsTheDefaultFolder(params string[] arguments)
    {
        Assert.Equal(DesktopPlatform.DefaultDataFolder(), DesktopPlatform.DataFolder(arguments));
    }

    [Fact]
    public void KeyProtector_OfThisDesktop_IsItsPlatformKeyStore()
    {
        var protector = DesktopPlatform.CreateKeyProtector();

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<DpapiKeyProtector>(protector);
            Assert.Equal(KeyProtection.WindowsDpapi, protector.Protection);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.IsType<SecretServiceKeyProtector>(protector);
            Assert.Equal(KeyProtection.LinuxSecretService, protector.Protection);
        }
        else
        {
            Assert.Null(protector);
        }
    }

    [Fact]
    public void FromArguments_GivesTheFolderAndTheKeyStore()
    {
        var platform = DesktopPlatform.FromArguments(["--data", "third-app"]);

        Assert.Equal(Path.GetFullPath("third-app"), platform.DataFolder);
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), platform.KeyProtector is not null);
        Assert.True(platform.CanReceivePcCopy);
        Assert.False(platform.CanSendPcCopy);
    }

    [Fact]
    public void FromArguments_GivesALogFolderInsideTheDataFolder()
    {
        var platform = DesktopPlatform.FromArguments(["--data", "fifth-app"]);

        Assert.Equal(Path.Combine(Path.GetFullPath("fifth-app"), "logs"), platform.LogFolder);
    }
}
