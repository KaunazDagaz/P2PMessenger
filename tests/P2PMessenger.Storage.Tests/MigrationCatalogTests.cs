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

    [Fact]
    [Trait("Story", "US-15")]
    public void Load_SecondScript_AddsTheMessageIdAndTheRecordedTime()
    {
        var migrations = MigrationCatalog.Load();

        Assert.Equal(2, migrations.Count);
        Assert.Equal("0002_message_id", migrations[1].Name);
        Assert.Contains("ADD COLUMN message_id_enc", migrations[1].Script, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN recorded_at_enc", migrations[1].Script, StringComparison.Ordinal);
    }
}
