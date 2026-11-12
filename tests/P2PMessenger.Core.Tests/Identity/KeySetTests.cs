using P2PMessenger.Core.Identity;

namespace P2PMessenger.Core.Tests.Identity;

public sealed class KeySetTests
{
    [Fact]
    [Trait("Story", "US-01")]
    public void Create_TwoKeySets_ShareNoKey()
    {
        using var first = KeySet.Create();
        using var second = KeySet.Create();

        Assert.NotEqual(first.IdentityKey, second.IdentityKey);
        Assert.NotEqual(first.StaticKey, second.StaticKey);
        Assert.NotEqual(first.OnionKey, second.OnionKey);
        Assert.NotEqual(first.OnionAddress, second.OnionAddress);
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    [Trait("Story", "US-01")]
    public void Create_OneKeySet_KeepsTheThreeRolesApart()
    {
        using var keySet = KeySet.Create();

        Assert.NotEqual(keySet.IdentityKey.Bytes.ToArray(), keySet.OnionKey.Bytes.ToArray());
        Assert.NotEqual(keySet.IdentityKey.Bytes.ToArray(), keySet.StaticKey.Bytes.ToArray());
        Assert.NotEqual(keySet.OnionKey.Bytes.ToArray(), keySet.StaticKey.Bytes.ToArray());
    }

    [Fact]
    [Trait("Story", "US-01")]
    public void TryImport_ReferenceSeeds_GivesTheStandardPublicKeysAndTheAddressTorReturned()
    {
        using var keySet = TestKeys.Alice();

        Assert.Equal(TestKeys.Rfc8032Public1, Convert.ToHexStringLower(keySet.IdentityKey.Bytes));
        Assert.Equal(TestKeys.Rfc7748AlicePublic, Convert.ToHexStringLower(keySet.StaticKey.Bytes));
        Assert.Equal(TestKeys.Rfc8032Public2, Convert.ToHexStringLower(keySet.OnionKey.Bytes));
        Assert.Equal("hvabpq7iioevvevxbktu2g36xsojqlgpf3cjndgazvk7ckxumygcmyyd", keySet.OnionAddress.Label);
        Assert.Equal("48720 76951 38111 09183 33093 40162", keySet.Fingerprint.ToString());
    }

    [Fact]
    [Trait("Story", "US-01")]
    public void TryExport_ThenTryImport_GivesTheSameKeys()
    {
        using var original = KeySet.Create();
        var exported = new byte[KeySet.ExportLength];

        Assert.True(original.TryExport(exported, out var written));
        Assert.Equal(KeySet.ExportLength, written);
        Assert.True(KeySet.TryImport(exported, out var restored));
        using (restored)
        {
            Assert.Equal(original.IdentityKey, restored.IdentityKey);
            Assert.Equal(original.StaticKey, restored.StaticKey);
            Assert.Equal(original.OnionKey, restored.OnionKey);
            Assert.Equal(original.OnionAddress, restored.OnionAddress);

            var again = new byte[KeySet.ExportLength];
            Assert.True(restored.TryExport(again, out _));
            Assert.Equal(exported, again);
        }
    }

    [Fact]
    [Trait("Story", "US-01")]
    public void TryExport_ReferenceSeeds_GivesTheLayoutOfTheStorageFormat()
    {
        using var keySet = TestKeys.Alice();
        var exported = new byte[KeySet.ExportLength + 5];

        Assert.True(keySet.TryExport(exported, out var written));

        Assert.Equal(97, written);
        Assert.Equal(TestKeys.Export(TestKeys.Rfc8032Seed1, TestKeys.Rfc7748AlicePrivate, TestKeys.Rfc8032Seed2), exported[..written]);
    }

    [Fact]
    public void TryExport_DestinationTooSmall_IsRejected()
    {
        using var keySet = KeySet.Create();

        Assert.False(keySet.TryExport(new byte[KeySet.ExportLength - 1], out var written));
        Assert.Equal(0, written);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(96)]
    [InlineData(98)]
    public void TryImport_WrongLength_IsRejected(int length)
    {
        Assert.False(KeySet.TryImport(new byte[length], out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(255)]
    public void TryImport_UnknownVersion_IsRejected(byte version)
    {
        var exported = TestKeys.Export(TestKeys.Rfc8032Seed1, TestKeys.Rfc7748AlicePrivate, TestKeys.Rfc8032Seed2);
        exported[0] = version;

        Assert.False(KeySet.TryImport(exported, out _));
    }

    [Fact]
    public void TryImport_SameSeedForIdentityAndOnionService_IsRejected()
    {
        var exported = TestKeys.Export(TestKeys.Rfc8032Seed1, TestKeys.Rfc7748AlicePrivate, TestKeys.Rfc8032Seed1);

        Assert.False(KeySet.TryImport(exported, out _));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var keySet = KeySet.Create();

        keySet.Dispose();
        keySet.Dispose();
    }
}
