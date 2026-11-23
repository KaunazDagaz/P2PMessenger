using System.Security.Cryptography;
using P2PMessenger.Android.KeyStores;
using P2PMessenger.Application.LocalData;

namespace P2PMessenger.Android.Tests.KeyStores;

public sealed class AndroidKeystoreKeyProtectorTests
{
    private const int KeyIdOffset = 1;
    private const int NonceOffset = KeyIdOffset + AndroidKeystoreKeyProtector.KeyIdLength;

    [Fact]
    [Trait("Story", "US-20")]
    public void Protection_IsTheAndroidKeystore()
    {
        var protector = new AndroidKeystoreKeyProtector(new FakeKeystore());

        Assert.Equal(KeyProtection.AndroidKeystore, protector.Protection);
        Assert.Equal(77, AndroidKeystoreKeyProtector.ProtectedKeyLength);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Protect_ThenUnprotect_GivesTheDataKeyBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore();
        var protector = new AndroidKeystoreKeyProtector(keystore);
        var dataKey = RandomNumberGenerator.GetBytes(32);

        var first = await protector.ProtectAsync(dataKey, cancellationToken);
        var second = await protector.ProtectAsync(dataKey, cancellationToken);
        var unprotected = new byte[32];
        var outcome = await protector.UnprotectAsync(first.ProtectedKey, unprotected, cancellationToken);

        Assert.True(first.Succeeded);
        Assert.Equal(AndroidKeystoreKeyProtector.ProtectedKeyLength, first.ProtectedKey.Length);
        Assert.Equal(1, first.ProtectedKey.Span[0]);
        Assert.Equal(-1, first.ProtectedKey.Span.IndexOf(dataKey));
        Assert.NotEqual(KeyId(first.ProtectedKey), KeyId(second.ProtectedKey));
        Assert.Equal(2, keystore.Aliases.Count);
        Assert.Equal(KeyStoreOutcome.Succeeded, outcome);
        Assert.Equal(dataKey, unprotected);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Protect_NewKey_IsNamedAfterTheKeyIdInTheStoredValue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore();
        var protector = new AndroidKeystoreKeyProtector(keystore);

        var result = await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken);

        var alias = Assert.Single(keystore.Aliases);
        Assert.Equal("p2pm-data-key-" + Convert.ToHexStringLower(KeyId(result.ProtectedKey)), alias);
        Assert.Matches("^p2pm-data-key-[0-9a-f]{32}$", alias);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Protect_AssociatedData_BindsTheLabelTheFormatAndTheKeyId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore();
        var protector = new AndroidKeystoreKeyProtector(keystore);

        var result = await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken);

        byte[] expected = [.. "P2PM/data-key/android-keystore/v1"u8, 1, .. KeyId(result.ProtectedKey)];
        Assert.Equal(expected, keystore.LastAssociatedData);
        Assert.Equal(expected, AndroidKeystoreKeyProtector.AssociatedData(KeyId(result.ProtectedKey)));
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Protect_PhoneWithStrongBox_CreatesTheKeyInStrongBox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore { HasStrongBox = true };
        var protector = new AndroidKeystoreKeyProtector(keystore);

        Assert.True((await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken)).Succeeded);

        var create = Assert.Single(keystore.Creates);
        Assert.True(create.StrongBox);
        Assert.True(keystore.IsInStrongBox(create.Alias));
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData((int)KeystoreStatus.StrongBoxUnavailable)]
    [InlineData((int)KeystoreStatus.Unavailable)]
    public async Task Protect_StrongBoxDoesNotMakeTheKey_FallsBackToTheTee(int strongBoxAnswer)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore { HasStrongBox = true, StrongBoxCreate = (KeystoreStatus)strongBoxAnswer };
        var protector = new AndroidKeystoreKeyProtector(keystore);
        var dataKey = RandomNumberGenerator.GetBytes(32);

        var result = await protector.ProtectAsync(dataKey, cancellationToken);
        var unprotected = new byte[32];

        Assert.True(result.Succeeded);
        Assert.Equal([true, false], keystore.Creates.Select(create => create.StrongBox));
        Assert.Equal(keystore.Creates[0].Alias, keystore.Creates[1].Alias);
        Assert.False(keystore.IsInStrongBox(Assert.Single(keystore.Aliases)));
        Assert.Equal(KeyStoreOutcome.Succeeded, await protector.UnprotectAsync(result.ProtectedKey, unprotected, cancellationToken));
        Assert.Equal(dataKey, unprotected);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Protect_PhoneWithoutStrongBox_CreatesTheKeyInTheTeeOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore { HasStrongBox = false };
        var protector = new AndroidKeystoreKeyProtector(keystore);

        Assert.True((await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken)).Succeeded);

        Assert.False(Assert.Single(keystore.Creates).StrongBox);
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Protect_KeystoreMakesNoKey_SaysItIsUnavailableAndLeavesNoKey(bool hasStrongBox)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore
        {
            HasStrongBox = hasStrongBox,
            StrongBoxCreate = KeystoreStatus.Unavailable,
            TeeCreate = KeystoreStatus.Unavailable,
        };
        var protector = new AndroidKeystoreKeyProtector(keystore);

        var result = await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(KeyStoreOutcome.Unavailable, result.Outcome);
        Assert.True(result.ProtectedKey.IsEmpty);
        Assert.Empty(keystore.Aliases);
        Assert.Contains(keystore.Creates[0].Alias, keystore.Deletes);
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData((int)KeystoreStatus.Unavailable, 12, 0)]
    [InlineData((int)KeystoreStatus.NotFound, 12, 0)]
    [InlineData(null, 16, 0)]
    [InlineData(null, 12, 1)]
    public async Task Protect_EncryptionFailsOrGivesOtherSizes_SaysItIsUnavailableAndDeletesTheNewKey(int? answer, int nonceLength, int extraBytes)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore { EncryptAnswer = (KeystoreStatus?)answer, NonceLength = nonceLength, ExtraCiphertextBytes = extraBytes };
        var protector = new AndroidKeystoreKeyProtector(keystore);

        var result = await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken);

        Assert.Equal(KeyStoreOutcome.Unavailable, result.Outcome);
        Assert.Empty(keystore.Aliases);
        Assert.Equal(Assert.Single(keystore.Creates).Alias, Assert.Single(keystore.Deletes));
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public async Task Protect_DataKeyOfAnotherLength_IsRefusedBeforeTheKeystore(int length)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore();
        var protector = new AndroidKeystoreKeyProtector(keystore);

        await Assert.ThrowsAsync<ArgumentException>(() => protector.ProtectAsync(new byte[length], cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => protector.UnprotectAsync(new byte[AndroidKeystoreKeyProtector.ProtectedKeyLength], new byte[length], cancellationToken));
        Assert.Empty(keystore.Creates);
        Assert.Equal(0, keystore.DecryptCalls);
    }

    [Fact]
    [Trait("NFR", "NFR-11")]
    public async Task Unprotect_OnAnotherPhone_SaysTheKeyIsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var protector = new AndroidKeystoreKeyProtector(new FakeKeystore());
        var otherPhone = new AndroidKeystoreKeyProtector(new FakeKeystore());
        var result = await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken);
        var unprotected = new byte[32];

        var outcome = await otherPhone.UnprotectAsync(result.ProtectedKey, unprotected, cancellationToken);

        Assert.Equal(KeyStoreOutcome.KeyNotFound, outcome);
        Assert.Equal(new byte[32], unprotected);
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public async Task Unprotect_EveryBitFlipped_IsNeverAccepted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore();
        var protector = new AndroidKeystoreKeyProtector(keystore);
        var dataKey = RandomNumberGenerator.GetBytes(32);
        var stored = (await protector.ProtectAsync(dataKey, cancellationToken)).ProtectedKey.ToArray();
        var accepted = new List<int>();

        for (var bit = 0; bit < stored.Length * 8; bit++)
        {
            var tampered = (byte[])stored.Clone();
            tampered[bit / 8] ^= (byte)(1 << (bit % 8));
            var unprotected = new byte[32];
            if (await protector.UnprotectAsync(tampered, unprotected, cancellationToken) != KeyStoreOutcome.KeyNotFound || unprotected.AsSpan().IndexOfAnyExcept((byte)0) >= 0)
            {
                accepted.Add(bit);
            }
        }

        var untouched = new byte[32];
        Assert.Empty(accepted);
        Assert.Equal(KeyStoreOutcome.Succeeded, await protector.UnprotectAsync(stored, untouched, cancellationToken));
        Assert.Equal(dataKey, untouched);
    }

    [Fact]
    [Trait("NFR", "NFR-11")]
    public async Task Unprotect_NonceAndCiphertextOfAnotherKey_AreRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore();
        var protector = new AndroidKeystoreKeyProtector(keystore);
        var first = (await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken)).ProtectedKey.ToArray();
        var second = (await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken)).ProtectedKey.ToArray();
        byte[] spliced = [.. first.AsSpan(0, NonceOffset), .. second.AsSpan(NonceOffset)];

        Assert.Equal(KeyStoreOutcome.KeyNotFound, await protector.UnprotectAsync(spliced, new byte[32], cancellationToken));
    }

    [Theory]
    [Trait("NFR", "NFR-11")]
    [InlineData(0, 1)]
    [InlineData(76, 1)]
    [InlineData(78, 1)]
    [InlineData(77, 0)]
    [InlineData(77, 2)]
    public async Task Unprotect_ValueOfAnotherLengthOrFormat_SaysTheKeyIsNotFoundWithoutAskingTheKeystore(int length, byte version)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore();
        var protector = new AndroidKeystoreKeyProtector(keystore);
        var stored = new byte[length];
        if (length > 0)
        {
            stored[0] = version;
        }

        Assert.Equal(KeyStoreOutcome.KeyNotFound, await protector.UnprotectAsync(stored, new byte[32], cancellationToken));
        Assert.Equal(0, keystore.DecryptCalls);
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData((int)KeystoreStatus.Unavailable, KeyStoreOutcome.Unavailable)]
    [InlineData((int)KeystoreStatus.NotFound, KeyStoreOutcome.KeyNotFound)]
    [InlineData((int)KeystoreStatus.Rejected, KeyStoreOutcome.KeyNotFound)]
    [InlineData((int)KeystoreStatus.StrongBoxUnavailable, KeyStoreOutcome.KeyNotFound)]
    public async Task Unprotect_KeystoreAnswer_BecomesTheOutcomeTheAppShows(int answer, KeyStoreOutcome expected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore();
        var protector = new AndroidKeystoreKeyProtector(keystore);
        var stored = (await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken)).ProtectedKey;
        keystore.DecryptAnswer = (KeystoreStatus)answer;
        var unprotected = new byte[32];

        Assert.Equal(expected, await protector.UnprotectAsync(stored, unprotected, cancellationToken));
        Assert.Equal(new byte[32], unprotected);
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public async Task Unprotect_KeystoreGivesAnotherLength_SaysTheKeyIsNotFound(int length)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore();
        var protector = new AndroidKeystoreKeyProtector(keystore);
        var stored = (await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken)).ProtectedKey;
        keystore.PlaintextLength = length;
        var unprotected = new byte[32];

        Assert.Equal(KeyStoreOutcome.KeyNotFound, await protector.UnprotectAsync(stored, unprotected, cancellationToken));
        Assert.Equal(new byte[32], unprotected);
    }

    [Fact]
    [Trait("Story", "US-21")]
    public async Task Forget_DeletesTheKeyFromTheKeystore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore();
        var protector = new AndroidKeystoreKeyProtector(keystore);
        var kept = (await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken)).ProtectedKey;
        var forgotten = (await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken)).ProtectedKey;

        await protector.ForgetAsync(forgotten, cancellationToken);

        Assert.Equal(AndroidKeystoreKeyProtector.Alias(KeyId(kept)), Assert.Single(keystore.Aliases));
        Assert.Equal(KeyStoreOutcome.KeyNotFound, await protector.UnprotectAsync(forgotten, new byte[32], cancellationToken));
        Assert.Equal(KeyStoreOutcome.Succeeded, await protector.UnprotectAsync(kept, new byte[32], cancellationToken));
    }

    [Theory]
    [Trait("Story", "US-21")]
    [InlineData(10)]
    [InlineData(77)]
    public async Task Forget_ValueOfAnotherFormat_DeletesNothing(int length)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keystore = new FakeKeystore();
        var protector = new AndroidKeystoreKeyProtector(keystore);
        _ = await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken);

        await protector.ForgetAsync(new byte[length], cancellationToken);

        Assert.Single(keystore.Aliases);
        Assert.Empty(keystore.Deletes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public void Alias_KeyIdOfAnotherLength_IsRefused(int length) =>
        Assert.Throws<ArgumentException>(() => AndroidKeystoreKeyProtector.Alias(new byte[length]));

    private static byte[] KeyId(ReadOnlyMemory<byte> protectedKey) =>
        protectedKey.Span.Slice(KeyIdOffset, AndroidKeystoreKeyProtector.KeyIdLength).ToArray();
}
