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

        Assert.Equal("0002_message_id", migrations[1].Name);
        Assert.Contains("ADD COLUMN message_id_enc", migrations[1].Script, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN recorded_at_enc", migrations[1].Script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-16")]
    public void Load_ThirdScript_GivesEarlierSentMessagesAWaitingDelivery()
    {
        var migrations = MigrationCatalog.Load();

        Assert.Equal("0003_outgoing_delivery", migrations[2].Name);
        Assert.Contains("INSERT INTO message_delivery", migrations[2].Script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-15")]
    public void Load_FourthScript_AddsTheSentSequenceThatOutlivesADeletedChat()
    {
        var migrations = MigrationCatalog.Load();

        Assert.Equal("0004_sent_sequence", migrations[3].Name);
        Assert.Contains("ADD COLUMN sent_sequence_enc", migrations[3].Script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Story", "US-12")]
    public void Load_FifthScript_AddsTheGroupRecordAndTheJoinAndNoticeStates()
    {
        var migrations = MigrationCatalog.Load();

        Assert.Equal("0005_groups", migrations[4].Name);
        Assert.Contains("ADD COLUMN record_enc", migrations[4].Script, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN joined", migrations[4].Script, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN invitation_state", migrations[4].Script, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN join_state", migrations[4].Script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Story", "US-21")]
    public void Load_SixthScript_AddsTheDeletedRangesOfAGroupChat()
    {
        var migrations = MigrationCatalog.Load();

        Assert.Equal("0006_deleted_group_messages", migrations[5].Name);
        Assert.Contains("ADD COLUMN deleted_ranges_enc", migrations[5].Script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Story", "US-22")]
    public void Load_SeventhScript_AddsTheEncryptedOriginToCopiedConversations()
    {
        var migrations = MigrationCatalog.Load();

        Assert.Equal("0007_copied_conversation_origin", migrations[6].Name);
        Assert.Contains("ADD COLUMN origin_enc", migrations[6].Script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Story", "US-17")]
    public void Load_EighthScript_AddsTheGroupAcknowledgementTable()
    {
        var migrations = MigrationCatalog.Load();

        Assert.Equal(8, migrations.Count);
        Assert.Equal("0008_group_acknowledgements", migrations[7].Name);
        Assert.Contains("CREATE TABLE group_acknowledgement", migrations[7].Script, StringComparison.Ordinal);
    }
}
