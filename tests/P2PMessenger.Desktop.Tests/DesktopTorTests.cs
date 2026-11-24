namespace P2PMessenger.Desktop.Tests;

[Trait("Story", "US-09")]
public sealed class DesktopTorTests
{
    [Fact]
    public void Tor_DataFolder_IsATorFolderInsideIt()
    {
        var data = Path.Combine(Path.GetTempPath(), "p2pm-desktop-data");

        var setup = DesktopPlatform.Tor(data, AppContext.BaseDirectory);

        Assert.Equal(Path.Combine(data, "tor"), setup.DataFolder);
        Assert.Equal(Path.Combine(data, "tor", "onion.sock"), setup.SocketPath);
    }

    [Fact]
    public void Tor_Executable_IsInTheTorFolderNextToTheApp()
    {
        var setup = DesktopPlatform.Tor(Path.GetTempPath(), AppContext.BaseDirectory);

        var expected = Path.Combine(Path.GetFullPath(AppContext.BaseDirectory), "tor", OperatingSystem.IsWindows() ? "tor.exe" : "tor");
        Assert.Equal(expected, setup.ExecutablePath);
        Assert.Equal(File.Exists(expected), setup.IsInBuild);
    }

    [Fact]
    public void Tor_NoDataFolder_UsesAFolderOfThisProcessInTheTemporaryFolder()
    {
        var setup = DesktopPlatform.Tor(null, AppContext.BaseDirectory);

        Assert.StartsWith(Path.GetTempPath(), setup.DataFolder, StringComparison.Ordinal);
        Assert.EndsWith(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), setup.DataFolder, StringComparison.Ordinal);
    }

    [Fact]
    public void FromArguments_GivesTheTorSetupOfTheDataFolder()
    {
        var platform = DesktopPlatform.FromArguments(["--data", "fourth-app"]);

        Assert.NotNull(platform.Tor);
        Assert.Equal(Path.Combine(Path.GetFullPath("fourth-app"), "tor"), platform.Tor.DataFolder);
    }
}
