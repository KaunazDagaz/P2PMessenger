using System.Globalization;
using Microsoft.Data.Sqlite;
using P2PMessenger.Android.KeyStores;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Android.Tests.KeyStores;

public sealed class KeystoreLocalDataTests : IDisposable
{
    private const string Passphrase = "lantern orchard pebble";
    private const string OtherPassphrase = "violet harbour 4471 kite";

    private static readonly PassphraseKdf Cheap = new(1024, 1);

    private readonly string root = Path.Combine(Path.GetTempPath(), string.Create(CultureInfo.InvariantCulture, $"p2pmessenger-android-test-{Guid.NewGuid():N}"));

    private string DatabasePath => Path.Combine(root, "data", DatabaseLocalData.FileName);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("Story", "US-15")]
    public async Task Create_WithoutPassphrase_SavesUnderTheKeystoreAndOpensAfterARestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var phone = new FakeKeystore { HasStrongBox = true };

        await using (var localData = new DatabaseLocalData(DatabasePath, new AndroidKeystoreKeyProtector(phone), Cheap))
        {
            var created = await localData.CreateAsync(null, cancellationToken);
            Assert.True(created.IsOpen);
            Assert.Equal(LocalDataMode.Saved, created.Stores.Mode);
            Assert.Equal(LocalDataProtection.KeyStore, localData.Protection);
            Assert.True(localData.HasKeyStore);
        }

        await using var restarted = new DatabaseLocalData(DatabasePath, new AndroidKeystoreKeyProtector(phone), Cheap);
        var opened = await restarted.OpenAsync(cancellationToken);

        Assert.True(opened.IsOpen);
        Assert.Equal(3L, await ScalarAsync("SELECT protection FROM data_key;", cancellationToken));
        Assert.Equal(1L, await ScalarAsync("SELECT unicode(substr(wrapped_key, 1, 1)) FROM data_key;", cancellationToken));
        Assert.Equal(77L, await ScalarAsync("SELECT length(wrapped_key) FROM data_key;", cancellationToken));
        Assert.True(phone.IsInStrongBox(Assert.Single(phone.Aliases)));
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Create_WithPassphrase_PutsTheShareUnderTheKeystore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var phone = new FakeKeystore();

        await using (var localData = new DatabaseLocalData(DatabasePath, new AndroidKeystoreKeyProtector(phone), Cheap))
        {
            Assert.True((await localData.CreateAsync(Passphrase, cancellationToken)).IsOpen);
        }

        await using var restarted = new DatabaseLocalData(DatabasePath, new AndroidKeystoreKeyProtector(phone), Cheap);

        Assert.Equal(LocalDataStatus.PassphraseNeeded, (await restarted.OpenAsync(cancellationToken)).Status);
        Assert.Equal(LocalDataStatus.WrongPassphrase, (await restarted.UnlockAsync(OtherPassphrase, cancellationToken)).Status);
        Assert.True((await restarted.UnlockAsync(Passphrase, cancellationToken)).IsOpen);
        Assert.Equal(1L, await ScalarAsync("SELECT protection FROM data_key;", cancellationToken));
        Assert.Equal(2L, await ScalarAsync("SELECT unicode(substr(wrapped_key, 1, 1)) FROM data_key;", cancellationToken));
        Assert.Equal(3L, await ScalarAsync("SELECT unicode(substr(wrapped_key, 2, 1)) FROM data_key;", cancellationToken));
        Assert.Single(phone.Aliases);
    }

    [Theory]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Open_FilesCopiedToAnotherPhone_SaysTheKeyIsNotFound(bool withPassphrase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var localData = new DatabaseLocalData(DatabasePath, new AndroidKeystoreKeyProtector(new FakeKeystore()), Cheap))
        {
            Assert.True((await localData.CreateAsync(withPassphrase ? Passphrase : null, cancellationToken)).IsOpen);
        }

        await using var otherPhone = new DatabaseLocalData(DatabasePath, new AndroidKeystoreKeyProtector(new FakeKeystore()), Cheap);
        var opened = await otherPhone.OpenAsync(cancellationToken);

        Assert.Equal(LocalDataStatus.Failed, opened.Status);
        Assert.Equal(LocalDataProblem.KeyNotFound, opened.Problem);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task SetPassphrase_LaterOnDataInTheKeystore_PutsTheKeyUnderBothAndDeletesTheOldKeystoreKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var phone = new FakeKeystore();
        await using var localData = new DatabaseLocalData(DatabasePath, new AndroidKeystoreKeyProtector(phone), Cheap);
        Assert.True((await localData.CreateAsync(null, cancellationToken)).IsOpen);
        var before = Assert.Single(phone.Aliases);

        var result = await localData.SetPassphraseAsync(Passphrase, cancellationToken);

        Assert.Equal(PassphraseChangeOutcome.Changed, result.Outcome);
        Assert.Equal(LocalDataProtection.Passphrase, localData.Protection);
        var after = Assert.Single(phone.Aliases);
        Assert.NotEqual(before, after);
        Assert.Contains(before, phone.Deletes);
        Assert.Equal(2L, await ScalarAsync("SELECT unicode(substr(wrapped_key, 1, 1)) FROM data_key;", cancellationToken));
        Assert.Equal(3L, await ScalarAsync("SELECT unicode(substr(wrapped_key, 2, 1)) FROM data_key;", cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task RemovePassphrase_GivesTheKeyToTheKeystoreAndDeletesTheShare()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var phone = new FakeKeystore();
        await using (var localData = new DatabaseLocalData(DatabasePath, new AndroidKeystoreKeyProtector(phone), Cheap))
        {
            Assert.True((await localData.CreateAsync(Passphrase, cancellationToken)).IsOpen);
            var share = Assert.Single(phone.Aliases);

            var result = await localData.RemovePassphraseAsync(Passphrase, cancellationToken);

            Assert.Equal(PassphraseChangeOutcome.Changed, result.Outcome);
            Assert.Equal(LocalDataProtection.KeyStore, localData.Protection);
            Assert.Contains(share, phone.Deletes);
            Assert.NotEqual(share, Assert.Single(phone.Aliases));
        }

        await using var restarted = new DatabaseLocalData(DatabasePath, new AndroidKeystoreKeyProtector(phone), Cheap);
        Assert.True((await restarted.OpenAsync(cancellationToken)).IsOpen);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Open_DataUnderThePassphraseAloneFromBeforeTheKeystore_StillOpensWithThePassphrase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var withoutKeystore = new DatabaseLocalData(DatabasePath, null, Cheap))
        {
            Assert.True((await withoutKeystore.CreateAsync(Passphrase, cancellationToken)).IsOpen);
        }

        var phone = new FakeKeystore();
        await using var localData = new DatabaseLocalData(DatabasePath, new AndroidKeystoreKeyProtector(phone), Cheap);

        Assert.Equal(LocalDataStatus.PassphraseNeeded, (await localData.OpenAsync(cancellationToken)).Status);
        Assert.True((await localData.UnlockAsync(Passphrase, cancellationToken)).IsOpen);
        Assert.Equal(1L, await ScalarAsync("SELECT unicode(substr(wrapped_key, 1, 1)) FROM data_key;", cancellationToken));
        Assert.Empty(phone.Aliases);
        Assert.Equal(PassphraseChangeOutcome.Changed, (await localData.RemovePassphraseAsync(Passphrase, cancellationToken)).Outcome);
        Assert.Equal(LocalDataProtection.KeyStore, localData.Protection);
        Assert.Single(phone.Aliases);
    }

    [Theory]
    [Trait("Story", "US-21")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAll_DeletesTheKeystoreKeyAndTheFiles(bool withPassphrase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var phone = new FakeKeystore();
        await using var localData = new DatabaseLocalData(DatabasePath, new AndroidKeystoreKeyProtector(phone), Cheap);
        Assert.True((await localData.CreateAsync(withPassphrase ? Passphrase : null, cancellationToken)).IsOpen);
        var alias = Assert.Single(phone.Aliases);

        Assert.True(await localData.DeleteAllAsync(cancellationToken));

        Assert.Empty(phone.Aliases);
        Assert.Contains(alias, phone.Deletes);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(DatabasePath)!));
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Create_KeystoreMakesNoKey_AsksForAPassphraseAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var phone = new FakeKeystore { HasStrongBox = true, StrongBoxCreate = KeystoreStatus.Unavailable, TeeCreate = KeystoreStatus.Unavailable };
        await using var localData = new DatabaseLocalData(DatabasePath, new AndroidKeystoreKeyProtector(phone), Cheap);

        var created = await localData.CreateAsync(null, cancellationToken);

        Assert.Equal(LocalDataStatus.PassphraseRequired, created.Status);
        Assert.Empty(phone.Aliases);
        Assert.False(File.Exists(DatabasePath));
    }

    private async Task<object?> ScalarAsync(string statement, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false };
        await using var connection = new SqliteConnection(connectionString.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = statement;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DBNull ? null : value;
    }
}
