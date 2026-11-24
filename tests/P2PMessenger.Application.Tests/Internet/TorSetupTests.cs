using P2PMessenger.Application.Internet;

namespace P2PMessenger.Application.Tests.Internet;

public sealed class TorSetupTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("p2pm-tor-setup-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void IsInBuild_ExecutableThatExists_IsTrue()
    {
        var executable = Path.Combine(folder, "tor");
        File.WriteAllText(executable, string.Empty);

        Assert.True(TorSetup.For(executable, Path.Combine(folder, "data")).IsInBuild);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tor")]
    public void IsInBuild_NoFullExecutablePath_IsFalse(string? executable)
    {
        Assert.False(TorSetup.For(executable, folder).IsInBuild);
    }

    [Fact]
    public void IsInBuild_MissingExecutable_IsFalse()
    {
        Assert.False(TorSetup.For(Path.Combine(folder, "missing", "tor"), folder).IsInBuild);
    }

    [Fact]
    public void SocketPath_IsInTheTorDataFolder()
    {
        var setup = TorSetup.For(null, Path.Combine(folder, "tor"));

        Assert.Equal(Path.Combine(folder, "tor", TorSetup.SocketFileName), setup.SocketPath);
        Assert.True(setup.UseUnixSocket);
    }

    [Fact]
    public void DeleteData_RemovesTheWholeTorFolder()
    {
        var data = Path.Combine(folder, "tor");
        Directory.CreateDirectory(Path.Combine(data, "keys"));
        File.WriteAllText(Path.Combine(data, "state"), "guards");
        var setup = TorSetup.For(null, data);

        Assert.True(setup.DeleteData());

        Assert.False(Directory.Exists(data));
        Assert.True(setup.DeleteData());
    }
}
