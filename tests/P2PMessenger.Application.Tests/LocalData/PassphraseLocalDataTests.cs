using System.Text;
using Microsoft.Data.Sqlite;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core.Identity;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Application.Tests.LocalData;

public sealed class PassphraseLocalDataTests
{
    private const string Passphrase = "lantern orchard pebble";
    private const string OtherPassphrase = "violet harbour 4471 kite";
    private const string ContactName = "Yorick Marlowe";

    private static readonly PassphraseKdf Cheap = new(1024, 1);

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Create_WithPassphraseAndAKeyStore_PutsTheKeyUnderBoth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);

        var result = await localData.CreateAsync(Passphrase, cancellationToken);

        Assert.True(result.IsOpen);
        Assert.Equal(LocalDataMode.Saved, result.Stores.Mode);
        Assert.Equal(LocalDataProtection.Passphrase, localData.Protection);
        Assert.Equal(1, protector.ProtectCalls);
        Assert.Equal(1L, await ScalarAsync(folder.DatabasePath, "SELECT protection FROM data_key;", cancellationToken));
        Assert.Equal(2L, await ScalarAsync(folder.DatabasePath, "SELECT unicode(substr(wrapped_key, 1, 1)) FROM data_key;", cancellationToken));
        Assert.Equal(1024L, await ScalarAsync(folder.DatabasePath, "SELECT kdf_memory_kib FROM data_key;", cancellationToken));
        Assert.Equal(1L, await ScalarAsync(folder.DatabasePath, "SELECT kdf_passes FROM data_key;", cancellationToken));
        Assert.Equal(16L, await ScalarAsync(folder.DatabasePath, "SELECT length(kdf_salt) FROM data_key;", cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Create_WithPassphraseAndNoKeyStore_SavesTheData()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        await (await FillAsync(folder.DatabasePath, null, cancellationToken)).DisposeAsync();

        await using var reopened = new DatabaseLocalData(folder.DatabasePath, null, Cheap);
        Assert.Equal(LocalDataStatus.PassphraseNeeded, (await reopened.OpenAsync(cancellationToken)).Status);
        var result = await reopened.UnlockAsync(Passphrase, cancellationToken);

        Assert.True(result.IsOpen);
        Assert.Equal(LocalDataMode.Saved, result.Stores.Mode);
        Assert.Single(await result.Stores.Contacts.LoadAllAsync(cancellationToken));
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData("")]
    [InlineData("eleven char")]
    public async Task Create_PassphraseThatIsTooShort_IsNotAcceptedAndWritesNothing(string passphrase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        await using var localData = new DatabaseLocalData(folder.DatabasePath, new TestKeyProtector(), Cheap);

        var result = await localData.CreateAsync(passphrase, cancellationToken);

        Assert.Equal(LocalDataStatus.PassphraseNotAccepted, result.Status);
        Assert.Empty(folder.Files());
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Create_NoKeyStoreAnswersButAPassphraseIsGiven_SavesTheData()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector { Protection = KeyProtection.LinuxSecretService, Outcome = KeyStoreOutcome.Unavailable };
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);

        Assert.Equal(LocalDataStatus.PassphraseRequired, (await localData.CreateAsync(null, cancellationToken)).Status);
        var result = await localData.CreateAsync(Passphrase, cancellationToken);

        Assert.True(result.IsOpen);
        Assert.Equal(LocalDataProtection.Passphrase, localData.Protection);
        Assert.Equal(PassphraseKeyWrap.WrappedLength, (long)(await ScalarAsync(folder.DatabasePath, "SELECT length(wrapped_key) FROM data_key;", cancellationToken))!);
        Assert.Equal(1L, await ScalarAsync(folder.DatabasePath, "SELECT unicode(substr(wrapped_key, 1, 1)) FROM data_key;", cancellationToken));
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData(KeyStoreOutcome.Refused, LocalDataProblem.KeyStoreRefused)]
    [InlineData(KeyStoreOutcome.KeyNotFound, LocalDataProblem.KeyNotFound)]
    public async Task Create_WithPassphraseAndARefusingKeyStore_ReportsItAndWritesNothing(KeyStoreOutcome outcome, LocalDataProblem problem)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        await using var localData = new DatabaseLocalData(folder.DatabasePath, new TestKeyProtector { Outcome = outcome }, Cheap);

        var result = await localData.CreateAsync(Passphrase, cancellationToken);

        Assert.Equal(LocalDataStatus.Failed, result.Status);
        Assert.Equal(problem, result.Problem);
        Assert.Empty(folder.Files());
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Open_AfterARestart_AsksForThePassphraseAndOpensNothingBeforeIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await (await FillAsync(folder.DatabasePath, protector, cancellationToken)).DisposeAsync();

        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);
        var result = await localData.OpenAsync(cancellationToken);

        Assert.Equal(LocalDataStatus.PassphraseNeeded, result.Status);
        Assert.False(result.IsOpen);
        Assert.Equal(LocalDataProtection.None, localData.Protection);
        Assert.Equal(1, protector.UnprotectCalls);
        Assert.False(await localData.CheckPassphraseAsync(Passphrase, cancellationToken));
        Assert.Equal(PassphraseChangeOutcome.NotAvailable, (await localData.ChangePassphraseAsync(Passphrase, OtherPassphrase, cancellationToken)).Outcome);
        Assert.Equal(LocalDataStatus.PassphraseNeeded, (await localData.CreateAsync(null, cancellationToken)).Status);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Unlock_WrongPassphrase_SaysSoAndTheRightOneOpensEverything()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await (await FillAsync(folder.DatabasePath, protector, cancellationToken)).DisposeAsync();

        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);
        Assert.Equal(LocalDataStatus.WrongPassphrase, (await localData.UnlockAsync(OtherPassphrase, cancellationToken)).Status);
        Assert.Equal(LocalDataStatus.WrongPassphrase, (await localData.UnlockAsync(Passphrase.ToUpperInvariant(), cancellationToken)).Status);
        var result = await localData.UnlockAsync(Passphrase, cancellationToken);

        Assert.True(result.IsOpen);
        Assert.Equal(LocalDataProtection.Passphrase, localData.Protection);
        using var identity = await result.Stores.Identity.LoadAsync(cancellationToken);
        Assert.Equal("Alice", identity!.Name.Value);
        Assert.Equal(ContactName, Assert.Single(await result.Stores.Contacts.LoadAllAsync(cancellationToken)).Name.Value);
        Assert.Same(result.Stores, (await localData.UnlockAsync(OtherPassphrase, cancellationToken)).Stores);
        Assert.Same(result.Stores, (await localData.OpenAsync(cancellationToken)).Stores);
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public async Task Open_FilesCopiedToAnotherDevice_AreUnreadableEvenWithThePassphrase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        using var otherDevice = new LocalDataFolder();
        await (await FillAsync(folder.DatabasePath, new TestKeyProtector(), cancellationToken)).DisposeAsync();
        var copied = folder.CopyTo(otherDevice);

        await using (var sameKind = new DatabaseLocalData(copied, new TestKeyProtector(), Cheap))
        {
            var result = await sameKind.UnlockAsync(Passphrase, cancellationToken);
            Assert.Equal(LocalDataStatus.Failed, result.Status);
            Assert.Equal(LocalDataProblem.KeyNotFound, result.Problem);
        }

        var otherStore = new TestKeyProtector { Protection = KeyProtection.LinuxSecretService };
        await using (var otherKind = new DatabaseLocalData(copied, otherStore, Cheap))
        {
            Assert.Equal(LocalDataProblem.KeyNotFound, (await otherKind.UnlockAsync(Passphrase, cancellationToken)).Problem);
            Assert.Equal(0, otherStore.UnprotectCalls);
        }

        await using var localData = new DatabaseLocalData(copied, null, Cheap);
        Assert.Equal(LocalDataProblem.KeyNotFound, (await localData.UnlockAsync(Passphrase, cancellationToken)).Problem);
        foreach (var file in otherDevice.Files())
        {
            var content = await ReadWhileOpenAsync(file, cancellationToken);
            Assert.Equal(-1, content.AsSpan().IndexOf(Encoding.UTF8.GetBytes(ContactName)));
            Assert.Equal(-1, content.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Passphrase)));
        }
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public async Task Open_FilesWithoutAKeyStoreCopiedToAnotherDevice_OpenOnlyWithThePassphrase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        using var otherDevice = new LocalDataFolder();
        await (await FillAsync(folder.DatabasePath, null, cancellationToken)).DisposeAsync();
        var copied = folder.CopyTo(otherDevice);

        await using var localData = new DatabaseLocalData(copied, new TestKeyProtector(), Cheap);

        Assert.Equal(LocalDataStatus.PassphraseNeeded, (await localData.OpenAsync(cancellationToken)).Status);
        Assert.Equal(LocalDataStatus.WrongPassphrase, (await localData.UnlockAsync(OtherPassphrase, cancellationToken)).Status);
        Assert.True((await localData.UnlockAsync(Passphrase, cancellationToken)).IsOpen);
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData(KeyStoreOutcome.Unavailable, LocalDataProblem.KeyStoreUnavailable)]
    [InlineData(KeyStoreOutcome.Refused, LocalDataProblem.KeyStoreRefused)]
    public async Task Open_KeyStoreDoesNotGiveItsShare_SaysSoBeforeAskingForThePassphrase(KeyStoreOutcome outcome, LocalDataProblem problem)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await (await FillAsync(folder.DatabasePath, protector, cancellationToken)).DisposeAsync();
        protector.Outcome = outcome;

        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);
        var result = await localData.OpenAsync(cancellationToken);

        Assert.Equal(LocalDataStatus.Failed, result.Status);
        Assert.Equal(problem, result.Problem);
        protector.Outcome = KeyStoreOutcome.Succeeded;
        Assert.Equal(LocalDataStatus.PassphraseNeeded, (await localData.OpenAsync(cancellationToken)).Status);
        Assert.True((await localData.UnlockAsync(Passphrase, cancellationToken)).IsOpen);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task CheckPassphrase_WhileOpen_TellsTheRightOneFromAWrongOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        await using var localData = await FillAsync(folder.DatabasePath, new TestKeyProtector(), cancellationToken);

        Assert.True(await localData.CheckPassphraseAsync(Passphrase, cancellationToken));
        Assert.False(await localData.CheckPassphraseAsync(OtherPassphrase, cancellationToken));
        Assert.False(await localData.CheckPassphraseAsync(string.Empty, cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task SetPassphrase_OnDataInTheKeyStore_MovesTheKeyToThePassphraseAndForgetsTheKeyStoresCopy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector { Protection = KeyProtection.LinuxSecretService };
        await using (var localData = new DatabaseLocalData(folder.DatabasePath, protector, Cheap))
        {
            var created = await localData.CreateAsync(null, cancellationToken);
            await SaveSampleAsync(created.Stores!, cancellationToken);
            var keyStoreCopy = (byte[])(await ScalarAsync(folder.DatabasePath, "SELECT wrapped_key FROM data_key;", cancellationToken))!;

            Assert.Equal(PassphraseChangeOutcome.NotAccepted, (await localData.SetPassphraseAsync("short", cancellationToken)).Outcome);
            var result = await localData.SetPassphraseAsync(Passphrase, cancellationToken);

            Assert.Equal(PassphraseChangeOutcome.Changed, result.Outcome);
            Assert.Equal(LocalDataProtection.Passphrase, localData.Protection);
            Assert.Equal(keyStoreCopy, Assert.Single(protector.Forgotten));
            Assert.Equal(1L, await ScalarAsync(folder.DatabasePath, "SELECT protection FROM data_key;", cancellationToken));
            Assert.Equal(2L, await ScalarAsync(folder.DatabasePath, "SELECT unicode(substr(wrapped_key, 1, 1)) FROM data_key;", cancellationToken));
            Assert.Equal(2, protector.ProtectCalls);
            Assert.Equal(PassphraseChangeOutcome.NotAvailable, (await localData.SetPassphraseAsync(OtherPassphrase, cancellationToken)).Outcome);
            await AssertNotInFilesAsync(folder, keyStoreCopy, cancellationToken);
        }

        await using var reopened = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);
        Assert.Equal(LocalDataStatus.PassphraseNeeded, (await reopened.OpenAsync(cancellationToken)).Status);
        var opened = await reopened.UnlockAsync(Passphrase, cancellationToken);
        Assert.Single(await opened.Stores!.Contacts.LoadAllAsync(cancellationToken));
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData(KeyStoreOutcome.Unavailable, LocalDataProblem.KeyStoreUnavailable)]
    [InlineData(KeyStoreOutcome.Refused, LocalDataProblem.KeyStoreRefused)]
    public async Task SetPassphrase_KeyStoreFails_ChangesNothing(KeyStoreOutcome outcome, LocalDataProblem problem)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);
        Assert.True((await localData.CreateAsync(null, cancellationToken)).IsOpen);
        protector.Outcome = outcome;

        var result = await localData.SetPassphraseAsync(Passphrase, cancellationToken);

        Assert.Equal(PassphraseChangeOutcome.KeyStoreFailed, result.Outcome);
        Assert.Equal(problem, result.Problem);
        Assert.Equal(LocalDataProtection.KeyStore, localData.Protection);
        Assert.Equal(2L, await ScalarAsync(folder.DatabasePath, "SELECT protection FROM data_key;", cancellationToken));
        Assert.Empty(protector.Forgotten);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task ChangePassphrase_WithTheCurrentOne_LetsOnlyTheNewOneOpenTheData()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await using (var localData = await FillAsync(folder.DatabasePath, protector, cancellationToken))
        {
            var before = (byte[])(await ScalarAsync(folder.DatabasePath, "SELECT wrapped_key FROM data_key;", cancellationToken))!;

            Assert.Equal(PassphraseChangeOutcome.WrongPassphrase, (await localData.ChangePassphraseAsync(OtherPassphrase, OtherPassphrase, cancellationToken)).Outcome);
            Assert.Equal(PassphraseChangeOutcome.NotAccepted, (await localData.ChangePassphraseAsync(Passphrase, "too short", cancellationToken)).Outcome);
            Assert.Equal(before, (byte[])(await ScalarAsync(folder.DatabasePath, "SELECT wrapped_key FROM data_key;", cancellationToken))!);
            Assert.Equal(PassphraseChangeOutcome.Changed, (await localData.ChangePassphraseAsync(Passphrase, OtherPassphrase, cancellationToken)).Outcome);

            Assert.True(await localData.CheckPassphraseAsync(OtherPassphrase, cancellationToken));
            Assert.False(await localData.CheckPassphraseAsync(Passphrase, cancellationToken));
            await AssertNotInFilesAsync(folder, before[^72..], cancellationToken);
            var after = (byte[])(await ScalarAsync(folder.DatabasePath, "SELECT wrapped_key FROM data_key;", cancellationToken))!;
            Assert.Equal(before[..^72], after[..^72]);
            Assert.Equal(1, protector.ProtectCalls);
            Assert.Empty(protector.Forgotten);
        }

        await using var reopened = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);
        Assert.Equal(LocalDataStatus.WrongPassphrase, (await reopened.UnlockAsync(Passphrase, cancellationToken)).Status);
        Assert.True((await reopened.UnlockAsync(OtherPassphrase, cancellationToken)).IsOpen);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task RemovePassphrase_WithTheCurrentOne_GivesTheKeyBackToTheKeyStore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await using (var localData = await FillAsync(folder.DatabasePath, protector, cancellationToken))
        {
            var shareCopy = (byte[])(await ScalarAsync(folder.DatabasePath, "SELECT substr(wrapped_key, 5, length(wrapped_key) - 76) FROM data_key;", cancellationToken))!;
            Assert.Equal(PassphraseChangeOutcome.WrongPassphrase, (await localData.RemovePassphraseAsync(OtherPassphrase, cancellationToken)).Outcome);
            Assert.Equal(1, protector.ProtectCalls);

            var result = await localData.RemovePassphraseAsync(Passphrase, cancellationToken);
            Assert.Equal(shareCopy, Assert.Single(protector.Forgotten));

            Assert.Equal(PassphraseChangeOutcome.Changed, result.Outcome);
            Assert.Equal(LocalDataProtection.KeyStore, localData.Protection);
            Assert.Equal(2L, await ScalarAsync(folder.DatabasePath, "SELECT protection FROM data_key;", cancellationToken));
            Assert.Null(await ScalarAsync(folder.DatabasePath, "SELECT kdf_salt FROM data_key;", cancellationToken));
            Assert.False(await localData.CheckPassphraseAsync(Passphrase, cancellationToken));
        }

        await using var reopened = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);
        var opened = await reopened.OpenAsync(cancellationToken);
        Assert.True(opened.IsOpen);
        Assert.Single(await opened.Stores.Contacts.LoadAllAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task RemovePassphrase_KeyStoreDoesNotAnswer_KeepsThePassphrase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await using var localData = await FillAsync(folder.DatabasePath, protector, cancellationToken);
        protector.Outcome = KeyStoreOutcome.Unavailable;

        var result = await localData.RemovePassphraseAsync(Passphrase, cancellationToken);

        Assert.Equal(PassphraseChangeOutcome.KeyStoreFailed, result.Outcome);
        Assert.Equal(LocalDataProblem.KeyStoreUnavailable, result.Problem);
        Assert.Equal(LocalDataProtection.Passphrase, localData.Protection);
        Assert.True(await localData.CheckPassphraseAsync(Passphrase, cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task RemovePassphrase_NoKeyStoreOnThisDevice_IsNotAvailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        await using var localData = await FillAsync(folder.DatabasePath, null, cancellationToken);

        Assert.Equal(PassphraseChangeOutcome.NotAvailable, (await localData.RemovePassphraseAsync(Passphrase, cancellationToken)).Outcome);
        Assert.Equal(PassphraseChangeOutcome.NotAvailable, (await localData.SetPassphraseAsync(OtherPassphrase, cancellationToken)).Outcome);
        Assert.Equal(LocalDataProtection.Passphrase, localData.Protection);
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData("UPDATE data_key SET kdf_memory_kib = 4194304;", LocalDataProblem.Damaged)]
    [InlineData("UPDATE data_key SET kdf_salt = zeroblob(16);", LocalDataProblem.None)]
    [InlineData("UPDATE data_key SET kdf_passes = 2;", LocalDataProblem.None)]
    public async Task Unlock_ChangedPassphraseSettings_AreDamagedOrRefuseTheRightPassphrase(string change, LocalDataProblem problem)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        await (await FillAsync(folder.DatabasePath, protector, cancellationToken)).DisposeAsync();
        await ExecuteAsync(folder.DatabasePath, change, cancellationToken);

        await using var localData = new DatabaseLocalData(folder.DatabasePath, protector, Cheap);
        var result = await localData.UnlockAsync(Passphrase, cancellationToken);

        Assert.False(result.IsOpen);
        Assert.Equal(problem == LocalDataProblem.None ? LocalDataStatus.WrongPassphrase : LocalDataStatus.Failed, result.Status);
        Assert.Equal(problem, result.Problem);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task InMemory_HasNoPassphrase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var localData = new InMemoryLocalData();

        Assert.False(localData.SupportsPassphrase);
        Assert.False(localData.HasKeyStore);
        Assert.Equal(LocalDataProtection.None, localData.Protection);
        Assert.True((await localData.CreateAsync(Passphrase, cancellationToken)).IsOpen);
        Assert.True((await localData.UnlockAsync(Passphrase, cancellationToken)).IsOpen);
        Assert.False(await localData.CheckPassphraseAsync(Passphrase, cancellationToken));
        Assert.Equal(PassphraseChangeOutcome.NotAvailable, (await localData.SetPassphraseAsync(Passphrase, cancellationToken)).Outcome);
        Assert.Equal(PassphraseChangeOutcome.NotAvailable, (await localData.ChangePassphraseAsync(Passphrase, OtherPassphrase, cancellationToken)).Outcome);
        Assert.Equal(PassphraseChangeOutcome.NotAvailable, (await localData.RemovePassphraseAsync(Passphrase, cancellationToken)).Outcome);
    }

    [Fact]
    public void Construct_SettingsOutsideTheRange_AreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DatabaseLocalData("messenger.db", null, new PassphraseKdf(1, 1)));
    }

    private static async Task<DatabaseLocalData> FillAsync(string databasePath, TestKeyProtector? protector, CancellationToken cancellationToken)
    {
        var localData = new DatabaseLocalData(databasePath, protector, Cheap);
        try
        {
            var result = await localData.CreateAsync(Passphrase, cancellationToken);
            Assert.True(result.IsOpen);
            await SaveSampleAsync(result.Stores, cancellationToken);
            return localData;
        }
        catch
        {
            await localData.DisposeAsync();
            throw;
        }
    }

    private static async Task SaveSampleAsync(LocalDataStores stores, CancellationToken cancellationToken)
    {
        var created = await new IdentityService(stores.Identity).CreateAsync("Alice", cancellationToken);
        Assert.True(created.IsCreated);
        using var bobKeys = KeySet.Create();
        await stores.Contacts.SaveAsync(new Contact(TestKeys.Code(bobKeys, ContactName), ContactState.Added, KeyComparison.NotCompared), cancellationToken);
    }

    private static async Task AssertNotInFilesAsync(LocalDataFolder folder, byte[] value, CancellationToken cancellationToken)
    {
        foreach (var file in folder.Files())
        {
            var content = await ReadWhileOpenAsync(file, cancellationToken);
            Assert.Equal(-1, content.AsSpan().IndexOf(value));
        }
    }

    private static async Task<byte[]> ReadWhileOpenAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        var content = new byte[stream.Length];
        await stream.ReadExactlyAsync(content, cancellationToken);
        return content;
    }

    private static async Task<object?> ScalarAsync(string databasePath, string statement, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false };
        await using var connection = new SqliteConnection(connectionString.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = statement;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DBNull ? null : value;
    }

    private static async Task ExecuteAsync(string databasePath, string statement, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false };
        await using var connection = new SqliteConnection(connectionString.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = statement;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
