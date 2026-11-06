namespace P2PMessenger.Integration.Tests.Architecture;

public sealed class ProjectFileTests
{
    [Fact]
    public void SourceFolder_ProjectFiles_AreExactlyTheLayersOfTheGuide()
    {
        var onDisk = RepositoryFiles.ProjectFilesUnder("src").Select(RepositoryFiles.ProjectName).Order(StringComparer.Ordinal);
        var expected = LayerRules.SourceProjects.Select(rule => rule.Name).Order(StringComparer.Ordinal);

        Assert.Equal(expected, onDisk);
    }

    [Fact]
    public void SourceProjects_ProjectReferences_PointOnlyToAllowedLayers()
    {
        var violations = LayerRules.SourceProjects
            .SelectMany(rule => RepositoryFiles.SourceProject(rule.Name).ProjectReferences
                .Where(reference => !rule.MayReference.Contains(reference, StringComparer.Ordinal))
                .Select(reference => $"{rule.Name} references {reference}; allowed: [{string.Join(", ", rule.MayReference)}]."));

        Assert.Empty(violations);
    }

    [Fact]
    public void SourceProjects_TargetFrameworks_MatchTheGuide()
    {
        var violations = LayerRules.SourceProjects
            .Select(rule => (Rule: rule, Project: RepositoryFiles.SourceProject(rule.Name)))
            .Where(item => item.Project.TargetFramework != item.Rule.TargetFramework || item.Project.TargetFrameworks is not null)
            .Select(item => $"{item.Rule.Name} targets '{item.Project.TargetFramework ?? item.Project.TargetFrameworks}'; expected '{item.Rule.TargetFramework}'.");

        Assert.Empty(violations);
    }

    [Fact]
    public void PlainLayers_PackageReferences_IncludeNoUiFrameworkOrPlatformSdk()
    {
        string[] uiAndPlatformPackages = ["Avalonia", "Xamarin.", "Microsoft.Maui"];

        var violations = LayerRules.PlainLayers
            .SelectMany(layer => RepositoryFiles.SourceProject(layer).PackageReferences
                .Where(package => uiAndPlatformPackages.Any(prefix => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .Select(package => $"{layer} references the package {package}."));

        Assert.Empty(violations);
    }
}
