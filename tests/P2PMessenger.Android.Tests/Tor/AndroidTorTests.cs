using P2PMessenger.Android.Tor;

namespace P2PMessenger.Android.Tests.Tor;

[Trait("Story", "US-09")]
public sealed class AndroidTorTests
{
    private const string UnixPathsOnly = "Android paths follow the Unix rules, which .NET applies on Linux and not on Windows.";

    [Fact]
    public void Setup_AppFolders_RunsLibTorFromTheNativeLibraryFolderWithItsDataInThePrivateFolder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), UnixPathsOnly);
        var setup = AndroidTor.Setup("/data/app/~~x/io.github.kaunazdagaz.p2pmessenger-1/lib/arm64", "/data/user/0/io.github.kaunazdagaz.p2pmessenger/no_backup");

        Assert.NotNull(setup);
        Assert.Equal("/data/app/~~x/io.github.kaunazdagaz.p2pmessenger-1/lib/arm64/libTor.so", setup.ExecutablePath);
        Assert.Equal("/data/user/0/io.github.kaunazdagaz.p2pmessenger/no_backup/tor", setup.DataFolder);
        Assert.Equal("/data/user/0/io.github.kaunazdagaz.p2pmessenger/no_backup/tor/onion.sock", setup.SocketPath);
        Assert.True(setup.UseUnixSocket);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lib/arm64")]
    public void Setup_NoNativeLibraryFolder_HasNoExecutable(string? nativeLibraryFolder)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), UnixPathsOnly);
        var setup = AndroidTor.Setup(nativeLibraryFolder, "/data/user/0/app/no_backup");

        Assert.NotNull(setup);
        Assert.Null(setup.ExecutablePath);
        Assert.False(setup.IsInBuild);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no_backup")]
    public void Setup_NoPrivateFolder_IsNone(string? privateFolder)
    {
        Assert.Null(AndroidTor.Setup("/data/app/lib/arm64", privateFolder));
    }

    [Fact]
    public void Setup_SocketPathOnARealPhone_IsShortEnoughForTor()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), UnixPathsOnly);
        var setup = AndroidTor.Setup("/data/app/lib/arm64", "/data/user/0/io.github.kaunazdagaz.p2pmessenger/no_backup");

        Assert.True(Transport.Tor.OnionTarget.IsUsableSocketPath(setup!.SocketPath));
    }
}
