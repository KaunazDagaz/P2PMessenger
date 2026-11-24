using P2PMessenger.Desktop.Settings;

namespace P2PMessenger.Desktop.Tests;

public sealed class FileSettingsStoreTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("p2pm-settings-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void ReadFlag_NoFile_GivesTheFallback()
    {
        var store = new FileSettingsStore(folder);

        Assert.True(store.ReadFlag("internet.tor", true));
        Assert.False(store.ReadFlag("internet.tor", false));
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void WriteFlag_IsReadBackByANewStoreOnTheSameFolder()
    {
        new FileSettingsStore(folder).WriteFlag("internet.tor", false);
        new FileSettingsStore(folder).WriteFlag("other.flag", true);

        var store = new FileSettingsStore(folder);
        Assert.False(store.ReadFlag("internet.tor", true));
        Assert.True(store.ReadFlag("other.flag", false));
        Assert.Equal(["internet.tor=false", "other.flag=true"], File.ReadAllLines(store.FilePath));
        Assert.False(File.Exists(store.FilePath + ".new"));
    }

    [Fact]
    public void Clear_DeletesTheFile()
    {
        var store = new FileSettingsStore(folder);
        store.WriteFlag("internet.tor", false);

        store.Clear();

        Assert.False(File.Exists(store.FilePath));
        Assert.True(store.ReadFlag("internet.tor", true));
        store.Clear();
    }

    [Fact]
    public void ReadFlag_DamagedLines_AreIgnored()
    {
        var store = new FileSettingsStore(folder);
        File.WriteAllLines(store.FilePath, ["internet.tor=maybe", "=false", "no separator", "other.flag=False"]);

        Assert.True(store.ReadFlag("internet.tor", true));
        Assert.False(store.ReadFlag("other.flag", true));
    }

    [Fact]
    public void ReadFlag_FileAboveTheLimit_IsIgnored()
    {
        var store = new FileSettingsStore(folder);
        File.WriteAllText(store.FilePath, "internet.tor=false\n" + new string('x', 5000));

        Assert.True(store.ReadFlag("internet.tor", true));
    }

    [Theory]
    [InlineData("a=b")]
    [InlineData("a b")]
    [InlineData("")]
    public void WriteFlag_KeyThatCannotBeStored_Throws(string key)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FileSettingsStore(folder).WriteFlag(key, true));
    }

    [Fact]
    public void FromArguments_DataFolder_KeepsTheSettingsThere()
    {
        var platform = DesktopPlatform.FromArguments(["--data", folder]);

        var store = Assert.IsType<FileSettingsStore>(platform.Settings);
        Assert.Equal(Path.Combine(Path.GetFullPath(folder), FileSettingsStore.FileName), store.FilePath);
    }
}
