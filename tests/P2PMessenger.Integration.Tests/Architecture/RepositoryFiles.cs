using System.Text.Json;
using System.Xml.Linq;

namespace P2PMessenger.Integration.Tests.Architecture;

internal static class RepositoryFiles
{
    public const string SolutionFileName = "P2PMessenger.slnx";
    public const string DesktopFilterFileName = "P2PMessenger.Desktop.slnf";

    public static DirectoryInfo Root { get; } = FindRoot();

    public static IReadOnlyList<string> SolutionProjects() =>
    [
        .. XDocument.Load(Path.Combine(Root.FullName, SolutionFileName))
            .Descendants("Project")
            .Select(project => ProjectName((string?)project.Attribute("Path") ?? string.Empty)),
    ];

    public static (string Solution, IReadOnlyList<string> Projects) DesktopFilter()
    {
        using var filter = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root.FullName, DesktopFilterFileName)));
        var solution = filter.RootElement.GetProperty("solution");
        var projects = solution.GetProperty("projects")
            .EnumerateArray()
            .Select(project => ProjectName(project.GetString() ?? string.Empty))
            .ToList();

        return (solution.GetProperty("path").GetString() ?? string.Empty, projects);
    }

    public static IReadOnlyList<string> ProjectFilesUnder(string folder) =>
    [
        .. Directory.EnumerateFiles(Path.Combine(Root.FullName, folder), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Order(StringComparer.Ordinal),
    ];

    public static ProjectFile SourceProject(string name) =>
        ProjectFile.Load(Path.Combine(Root.FullName, "src", name, name + ".csproj"));

    public static string ProjectName(string projectPath)
    {
        var fileName = projectPath.Replace('\\', '/').Split('/')[^1];
        return fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? fileName[..^".csproj".Length] : fileName;
    }

    private static bool IsBuildOutput(string path)
    {
        var segments = path.Replace('\\', '/').Split('/');
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase) || segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static DirectoryInfo FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, SolutionFileName)))
            {
                return folder;
            }
        }

        throw new InvalidOperationException($"No folder above {AppContext.BaseDirectory} holds {SolutionFileName}; run the tests from a repository checkout.");
    }
}

internal sealed class ProjectFile
{
    private ProjectFile(string? targetFramework, string? targetFrameworks, IReadOnlyList<string> projectReferences, IReadOnlyList<string> packageReferences)
    {
        TargetFramework = targetFramework;
        TargetFrameworks = targetFrameworks;
        ProjectReferences = projectReferences;
        PackageReferences = packageReferences;
    }

    public string? TargetFramework { get; }

    public string? TargetFrameworks { get; }

    public IReadOnlyList<string> ProjectReferences { get; }

    public IReadOnlyList<string> PackageReferences { get; }

    public static ProjectFile Load(string path)
    {
        var project = XDocument.Load(path).Root ?? throw new InvalidDataException($"{path} is empty.");

        return new ProjectFile(
            (string?)project.Elements("PropertyGroup").Elements("TargetFramework").FirstOrDefault(),
            (string?)project.Elements("PropertyGroup").Elements("TargetFrameworks").FirstOrDefault(),
            [.. Includes(project, "ProjectReference").Select(RepositoryFiles.ProjectName)],
            [.. Includes(project, "PackageReference")]);
    }

    private static IEnumerable<string> Includes(XElement project, string itemType) =>
        project.Elements("ItemGroup")
            .Elements(itemType)
            .Select(item => (string?)item.Attribute("Include"))
            .OfType<string>();
}
