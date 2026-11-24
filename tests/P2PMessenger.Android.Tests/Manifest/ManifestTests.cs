using System.Xml.Linq;

namespace P2PMessenger.Android.Tests.Manifest;

public sealed class ManifestTests
{
    private static readonly XNamespace AndroidNamespace = "http://schemas.android.com/apk/res/android";
    private static readonly XNamespace ToolsNamespace = "http://schemas.android.com/tools";

    private static readonly string[] Domains =
        ["root", "file", "database", "sharedpref", "external", "device_root", "device_file", "device_database", "device_sharedpref"];

    [Fact]
    [Trait("Story", "US-08")]
    [Trait("Story", "US-18")]
    [Trait("Story", "US-19")]
    public void Manifest_Permissions_AreOnlyTheOnesTheAppUses()
    {
        var permissions = Manifest().Elements("uses-permission").Select(element => (string?)element.Attribute(AndroidNamespace + "name"));

        Assert.Equal(
            [
                "android.permission.INTERNET",
                "android.permission.CAMERA",
                "android.permission.FOREGROUND_SERVICE",
                "android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE",
                "android.permission.CHANGE_WIFI_MULTICAST_STATE",
                "android.permission.POST_NOTIFICATIONS",
                "android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS",
                "android.permission.RECEIVE_BOOT_COMPLETED",
            ],
            permissions);
        string text = File.ReadAllText(HeadFile("Properties", "AndroidManifest.xml"));
        Assert.DoesNotContain("ACCESS_LOCAL_NETWORK", text, StringComparison.Ordinal);
        Assert.DoesNotContain("EXACT_ALARM", text, StringComparison.Ordinal);
        Assert.DoesNotContain("WAKE_LOCK", text, StringComparison.Ordinal);
        Assert.DoesNotContain("WakeUpReceiver", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SPECIAL_USE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dataSync", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Story", "US-18")]
    public void Manifest_Service_IsConnectedDeviceAndNotExported()
    {
        var service = Assert.Single(Manifest().Elements("application").Elements("service"));

        Assert.Equal("io.github.kaunazdagaz.p2pmessenger.MessengerService", (string?)service.Attribute(AndroidNamespace + "name"));
        Assert.Equal("connectedDevice", (string?)service.Attribute(AndroidNamespace + "foregroundServiceType"));
        Assert.Equal("false", (string?)service.Attribute(AndroidNamespace + "exported"));
        Assert.Empty(service.Elements("intent-filter"));
    }

    [Fact]
    [Trait("Story", "US-18")]
    public void Manifest_BootReceiver_HearsOnlyBootAndUpdateAndIsNotExported()
    {
        var receiver = Receiver("io.github.kaunazdagaz.p2pmessenger.BootReceiver");

        Assert.Equal("false", (string?)receiver.Attribute(AndroidNamespace + "exported"));
        var filter = Assert.Single(receiver.Elements("intent-filter"));
        Assert.Equal(
            ["android.intent.action.BOOT_COMPLETED", "android.intent.action.MY_PACKAGE_REPLACED"],
            filter.Elements("action").Select(element => (string?)element.Attribute(AndroidNamespace + "name")));
        Assert.Empty(filter.Elements("data"));
        Assert.Empty(filter.Elements("category"));
    }

    [Fact]
    [Trait("Story", "US-18")]
    public void Manifest_QuitActivity_IsNotExportedAndStaysOutOfTheRecentApps()
    {
        var activity = Assert.Single(Manifest().Elements("application").Elements("activity"));

        Assert.Equal("io.github.kaunazdagaz.p2pmessenger.QuitActivity", (string?)activity.Attribute(AndroidNamespace + "name"));
        Assert.Equal("false", (string?)activity.Attribute(AndroidNamespace + "exported"));
        Assert.Equal("true", (string?)activity.Attribute(AndroidNamespace + "excludeFromRecents"));
        Assert.Empty(activity.Elements("intent-filter"));
    }

    [Theory]
    [InlineData("MessengerService")]
    [InlineData("BootReceiver")]
    [InlineData("QuitActivity")]
    public void Components_JavaNames_MatchTheirClasses(string name)
    {
        string source = File.ReadAllText(HeadFile("Background", name + ".cs"));

        Assert.Contains($"[Register(\"io.github.kaunazdagaz.p2pmessenger.{name}\")]", source, StringComparison.Ordinal);
        Assert.Contains($"io.github.kaunazdagaz.p2pmessenger.{name}", File.ReadAllText(HeadFile("Properties", "AndroidManifest.xml")), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("NFR", "NFR-11")]
    public void Manifest_Backup_IsOffAndPointsToTheExtractionRules()
    {
        var application = Assert.Single(Manifest().Elements("application"));

        Assert.Equal("false", (string?)application.Attribute(AndroidNamespace + "allowBackup"));
        Assert.Equal("@xml/data_extraction_rules", (string?)application.Attribute(AndroidNamespace + "dataExtractionRules"));
    }

    [Fact]
    [Trait("Story", "US-09")]
    public void Manifest_NativeLibraries_AreExtractedSoTorCanBeStartedFromThem()
    {
        var application = Assert.Single(Manifest().Elements("application"));

        Assert.Equal("true", (string?)application.Attribute(AndroidNamespace + "extractNativeLibs"));
    }

    [Fact]
    [Trait("Story", "US-09")]
    public void Project_TorBundles_AreNativeLibrariesForTheirAbisOnlyWhenDownloaded()
    {
        var project = XDocument.Load(HeadFile("P2PMessenger.Android.csproj")).Root!;
        var libraries = project.Elements("ItemGroup").Elements("AndroidNativeLibrary").ToList();

        Assert.Equal(["arm64-v8a", "armeabi-v7a", "x86_64"], libraries.Select(static element => (string?)element.Attribute("Abi")));
        Assert.All(libraries, static element =>
        {
            var include = (string?)element.Attribute("Include");
            Assert.EndsWith("/tor/libTor.so", include, StringComparison.Ordinal);
            Assert.Equal($"Exists('{include}')", (string?)element.Attribute("Condition"));
        });
    }

    [Fact]
    [Trait("NFR", "NFR-18")]
    public void Project_Architectures_IncludeThirtyTwoBitArmForOlderPhones()
    {
        var project = XDocument.Load(HeadFile("P2PMessenger.Android.csproj")).Root!;
        var identifiers = Assert.Single(project.Elements("PropertyGroup").Elements("RuntimeIdentifiers"));

        Assert.Equal(["android-arm", "android-arm64", "android-x64"], identifiers.Value.Split(';'));
    }

    [Theory]
    [Trait("NFR", "NFR-11")]
    [InlineData("cloud-backup")]
    [InlineData("device-transfer")]
    public void ExtractionRules_EveryDomain_IsExcluded(string section)
    {
        var rules = XDocument.Load(HeadFile("Resources", "xml", "data_extraction_rules.xml")).Root!;
        var part = Assert.Single(rules.Elements(section));

        Assert.Empty(part.Elements("include"));
        Assert.Equal(
            Domains.Order(StringComparer.Ordinal),
            part.Elements("exclude").Where(element => (string?)element.Attribute("path") == ".").Select(element => (string?)element.Attribute("domain")).Order(StringComparer.Ordinal));
        Assert.Empty(rules.Elements("cross-platform-transfer"));
    }

    [Fact]
    public void Manifest_ProfileInstallReceiver_IsRemoved()
    {
        var receiver = Receiver("androidx.profileinstaller.ProfileInstallReceiver");

        Assert.Equal("androidx.profileinstaller.ProfileInstallReceiver", (string?)receiver.Attribute(AndroidNamespace + "name"));
        Assert.Equal("remove", (string?)receiver.Attribute(ToolsNamespace + "node"));
    }

    private static XElement Manifest() => XDocument.Load(HeadFile("Properties", "AndroidManifest.xml")).Root!;

    private static XElement Receiver(string name) =>
        Assert.Single(Manifest().Elements("application").Elements("receiver"), element => (string?)element.Attribute(AndroidNamespace + "name") == name);

    private static string HeadFile(params string[] parts)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "P2PMessenger.slnx")))
            {
                return Path.Combine([folder.FullName, "src", "P2PMessenger.Android", .. parts]);
            }
        }

        throw new InvalidOperationException($"No folder above {AppContext.BaseDirectory} holds P2PMessenger.slnx; run the tests from a repository checkout.");
    }
}
