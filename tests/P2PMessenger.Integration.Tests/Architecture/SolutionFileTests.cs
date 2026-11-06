namespace P2PMessenger.Integration.Tests.Architecture;

public sealed class SolutionFileTests
{
    [Fact]
    public void Solution_Projects_AreEverySourceAndTestProject()
    {
        var onDisk = RepositoryFiles.ProjectFilesUnder("src")
            .Concat(RepositoryFiles.ProjectFilesUnder("tests"))
            .Select(RepositoryFiles.ProjectName)
            .Order(StringComparer.Ordinal);

        Assert.Equal(onDisk, RepositoryFiles.SolutionProjects().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void DesktopFilter_Projects_AreTheSolutionWithoutTheAndroidHead()
    {
        var (solution, projects) = RepositoryFiles.DesktopFilter();
        var expected = RepositoryFiles.SolutionProjects()
            .Where(project => project != LayerRules.Android)
            .Order(StringComparer.Ordinal);

        Assert.Equal(RepositoryFiles.SolutionFileName, solution);
        Assert.Equal(expected, projects.Order(StringComparer.Ordinal));
    }
}
