namespace P2PMessenger.Storage.Tests;

public sealed class MigrationCatalogTests
{
    [Fact]
    public void Load_EmbeddedScripts_AreNumberedFromOneWithoutGaps()
    {
        var migrations = MigrationCatalog.Load();

        Assert.NotEmpty(migrations);
        Assert.Equal(Enumerable.Range(1, migrations.Count), migrations.Select(migration => migration.Version));
    }

    [Fact]
    public void Load_FirstScript_IsTheInitialSchema()
    {
        var first = MigrationCatalog.Load()[0];

        Assert.Equal("0001_initial", first.Name);
        Assert.Contains("CREATE TABLE message (", first.Script, StringComparison.Ordinal);
    }
}
