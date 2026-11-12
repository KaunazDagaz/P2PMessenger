using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Core.Tests.Crypto;

public sealed class X25519Tests
{
    [Theory]
    [InlineData(TestKeys.Rfc7748AlicePrivate, TestKeys.Rfc7748AlicePublic)]
    [InlineData(TestKeys.Rfc7748BobPrivate, TestKeys.Rfc7748BobPublic)]
    public void KeyPair_Rfc7748Vector_GivesTheStandardPublicKey(string privateHex, string publicHex)
    {
        Assert.True(X25519KeyPair.TryImportPrivateKey(Convert.FromHexString(privateHex), out var keyPair));
        using (keyPair)
        {
            Assert.Equal(publicHex, Convert.ToHexStringLower(keyPair.PublicKey.Bytes));
        }
    }

    [Fact]
    public void PrivateKey_ExportedAfterImport_GivesTheSamePublicKey()
    {
        Assert.True(X25519KeyPair.TryImportPrivateKey(Convert.FromHexString(TestKeys.Rfc7748AlicePrivate), out var original));
        using (original)
        {
            var exported = new byte[X25519KeyPair.PrivateKeyLength];
            Assert.True(original.TryExportPrivateKey(exported, out var written));
            Assert.Equal(X25519KeyPair.PrivateKeyLength, written);

            Assert.True(X25519KeyPair.TryImportPrivateKey(exported, out var restored));
            using (restored)
            {
                Assert.Equal(original.PublicKey, restored.PublicKey);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void Keys_OfWrongLength_AreRejected(int length)
    {
        Assert.False(X25519KeyPair.TryImportPrivateKey(new byte[length], out _));
        Assert.False(X25519PublicKey.TryCreate(new byte[length], out _));
    }

    [Theory]
    [InlineData("edffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")]
    [InlineData("eeffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")]
    [InlineData("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")]
    [InlineData("8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4eea")]
    public void PublicKey_NonCanonicalEncoding_IsRejected(string hex)
    {
        Assert.False(X25519PublicKey.TryCreate(Convert.FromHexString(hex), out _));
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("0100000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("e0eb7a7c3b41b8ae1656e3faf19fc46ada098deb9c32b1fd866205165f49b800")]
    [InlineData("5f9c95bca3508c24b1d0b1559c83ef5b04445cc4581c8e86d8224eddd09f1157")]
    [InlineData("ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")]
    public void PublicKey_OfSmallOrder_IsCanonicalButNotUsable(string hex)
    {
        Assert.True(X25519PublicKey.TryCreate(Convert.FromHexString(hex), out var key));

        Assert.False(key.IsUsable());
    }

    [Theory]
    [InlineData(TestKeys.Rfc7748AlicePublic)]
    [InlineData(TestKeys.Rfc7748BobPublic)]
    public void PublicKey_Rfc7748Vector_IsUsable(string hex)
    {
        Assert.True(X25519PublicKey.TryCreate(Convert.FromHexString(hex), out var key));

        Assert.True(key.IsUsable());
    }

    [Fact]
    public void PublicKey_OfFreshKeyPairs_IsCanonicalAndUsable()
    {
        for (var count = 0; count < 50; count++)
        {
            using var keyPair = X25519KeyPair.Create();

            Assert.True(X25519PublicKey.TryCreate(keyPair.PublicKey.Bytes, out var key));
            Assert.True(key.IsUsable());
        }
    }
}
