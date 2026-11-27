using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace P2PMessenger.Desktop.Tests.Packaging;

public sealed partial class ReleasePackagingTests
{
    private static readonly string[] RequiredWorkflowActions =
    [
        "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1",
        "actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73",
        "actions/setup-java@d1374f8ac74474200d16ef8ecee22c7ae7a51880",
        "actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9",
        "actions/download-artifact@9000827ccba6bdab643e8b6fd33ac0654aef8333",
    ];

    private static readonly string[] ProhibitedInternalMarkers =
    [
        "AGENTS.md",
        "CLAUDE.md",
        "docs/",
        "ADR ",
        "US-",
        "NFR-",
    ];

    [Fact]
    public void DesktopProject_ConfiguresRuntimeIdentifiersIconAndTorFilter()
    {
        XElement project = XDocument.Load(RepoFile("src", "P2PMessenger.Desktop", "P2PMessenger.Desktop.csproj")).Root!;
        string rids = project.Elements("PropertyGroup").Elements("RuntimeIdentifiers").Single().Value;
        string icon = project.Elements("PropertyGroup").Elements("ApplicationIcon").Single().Value;
        XElement torNone = project.Elements("ItemGroup")
            .Elements("None")
            .Single(static element => string.Equals((string?)element.Attribute("LinkBase"), "tor", StringComparison.Ordinal));
        string exclude = (string?)torNone.Attribute("Exclude") ?? string.Empty;

        Assert.Equal(["win-x64", "linux-x64"], rids.Split(';'));
        Assert.Contains("p2pmessenger.ico", icon, StringComparison.Ordinal);
        Assert.Contains("pluggable_transports/**", exclude, StringComparison.Ordinal);
        Assert.Contains("tor-gencert*", exclude, StringComparison.Ordinal);
    }

    [Fact]
    public void AppIcon_ReplacesAvaloniaPlaceholder()
    {
        Assert.True(File.Exists(RepoFile("src", "P2PMessenger.UI", "Assets", "p2pmessenger.ico")));
        Assert.False(File.Exists(RepoFile("src", "P2PMessenger.UI", "Assets", "avalonia-logo.ico")));
        string windowXaml = File.ReadAllText(RepoFile("src", "P2PMessenger.UI", "Views", "MainWindow.axaml"));
        Assert.Contains("Icon=\"/Assets/p2pmessenger.ico\"", windowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("avalonia-logo.ico", windowXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ThirdPartyNotices_CoversAllNonTestPackagesAndStaysPublic()
    {
        string noticesPath = RepoFile("THIRD-PARTY-NOTICES.txt");
        Assert.True(File.Exists(noticesPath));
        string notices = File.ReadAllText(noticesPath);

        IEnumerable<string> srcPackages = Directory.EnumerateFiles(RepoFile("src"), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(static projectFile => XDocument.Load(projectFile).Root!
                .Elements("ItemGroup")
                .Elements("PackageReference")
                .Select(static element => (string?)element.Attribute("Include"))
                .OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (string package in srcPackages)
        {
            Assert.Contains(package, notices, StringComparison.Ordinal);
        }

        Assert.Contains("Tor Expert Bundle", notices, StringComparison.Ordinal);
        Assert.Contains("Inno Setup", notices, StringComparison.Ordinal);
        Assert.Contains("Visual C++ Redistributable", notices, StringComparison.Ordinal);

        foreach (string marker in ProhibitedInternalMarkers)
        {
            Assert.DoesNotContain(marker, notices, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WindowsInstallerScript_ChecksVcRedistAndHasNoComments()
    {
        string path = RepoFile("tools", "windows-installer.iss");
        Assert.True(File.Exists(path));
        string text = File.ReadAllText(path);

        Assert.Contains("NeedsVcRedist", text, StringComparison.Ordinal);
        Assert.Contains("vc_redist.x64.exe", text, StringComparison.Ordinal);
        Assert.Contains("P2PMessenger.Desktop.exe", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)(?:^|\s)//", text);
        Assert.DoesNotContain("/*", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^\s*;", text);
    }

    [Fact]
    public void ReleaseWorkflow_PinsActionsSetsPermissionsAndHasNoComments()
    {
        string path = RepoFile(".github", "workflows", "release.yml");
        Assert.True(File.Exists(path));
        string text = File.ReadAllText(path);
        string normalized = text.Replace('\\', '/');

        Assert.Contains("permissions:\r\n  contents: read", text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("tools/get-tor.ps1", normalized, StringComparison.Ordinal);
        Assert.Contains("tools/verify-release.ps1", normalized, StringComparison.Ordinal);
        Assert.Contains("tools/windows-installer.iss", normalized, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", text, StringComparison.Ordinal);
        Assert.Contains("signing-certificate-sha256.txt", text, StringComparison.Ordinal);
        Assert.Contains("environment: production", text, StringComparison.Ordinal);

        foreach (string expected in RequiredWorkflowActions)
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }

        MatchCollection usesMatches = UsesPattern().Matches(text);
        Assert.NotEmpty(usesMatches);
        foreach (Match match in usesMatches)
        {
            Assert.Matches(@"^[0-9a-f]{40}$", match.Groups[1].Value);
        }

        Assert.DoesNotMatch(@"(?m)^\s*#", text);
    }

    [GeneratedRegex(@"uses:\s+[^\s@]+@([0-9a-fA-F]+)")]
    private static partial Regex UsesPattern();

    private static string RepoFile(params string[] parts)
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "P2PMessenger.slnx")))
            {
                return Path.Combine([folder.FullName, .. parts]);
            }
        }

        throw new InvalidOperationException($"No folder above {AppContext.BaseDirectory} holds P2PMessenger.slnx.");
    }
}
