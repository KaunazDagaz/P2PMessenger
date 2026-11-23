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
    public void Manifest_Permissions_AreOnlyTheInternetAndTheCamera()
    {
        var permissions = Manifest().Elements("uses-permission").Select(element => (string?)element.Attribute(AndroidNamespace + "name"));

        Assert.Equal(["android.permission.INTERNET", "android.permission.CAMERA"], permissions);
        Assert.DoesNotContain("ACCESS_LOCAL_NETWORK", File.ReadAllText(HeadFile("Properties", "AndroidManifest.xml")), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("NFR", "NFR-11")]
    public void Manifest_Backup_IsOffAndPointsToTheExtractionRules()
    {
        var application = Assert.Single(Manifest().Elements("application"));

        Assert.Equal("false", (string?)application.Attribute(AndroidNamespace + "allowBackup"));
        Assert.Equal("@xml/data_extraction_rules", (string?)application.Attribute(AndroidNamespace + "dataExtractionRules"));
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
        var receiver = Assert.Single(Manifest().Elements("application").Elements("receiver"));

        Assert.Equal("androidx.profileinstaller.ProfileInstallReceiver", (string?)receiver.Attribute(AndroidNamespace + "name"));
        Assert.Equal("remove", (string?)receiver.Attribute(ToolsNamespace + "node"));
    }

    private static XElement Manifest() => XDocument.Load(HeadFile("Properties", "AndroidManifest.xml")).Root!;

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
