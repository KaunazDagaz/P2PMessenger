using NSec.Cryptography;
using P2PMessenger.Core.Noise;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Core.Tests.Noise;

public sealed class NoiseHashTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(1000)]
    public void Hmac_Sha256_EqualsNSecHmac(int length)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var data = RandomNumberGenerator.GetBytes(length);
        using var nsecKey = Key.Import(MacAlgorithm.HmacSha256, key, KeyBlobFormat.RawSymmetricKey);
        var expected = MacAlgorithm.HmacSha256.Mac(nsecKey, data);
        var actual = new byte[NoiseHash.Sha256.HashLength];

        NoiseHash.Sha256.Hmac(key, data, default, actual);

        Assert.Equal(Convert.ToHexStringLower(expected), Convert.ToHexStringLower(actual));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(1000)]
    public void Hmac_Sha512_EqualsNSecHmac(int length)
    {
        var key = RandomNumberGenerator.GetBytes(64);
        var data = RandomNumberGenerator.GetBytes(length);
        using var nsecKey = Key.Import(MacAlgorithm.HmacSha512, key, KeyBlobFormat.RawSymmetricKey);
        var expected = MacAlgorithm.HmacSha512.Mac(nsecKey, data);
        var actual = new byte[NoiseHash.Sha512.HashLength];

        NoiseHash.Sha512.Hmac(key, data, default, actual);

        Assert.Equal(Convert.ToHexStringLower(expected), Convert.ToHexStringLower(actual));
    }

    [Theory]
    [InlineData("SHA256", "b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7")]
    [InlineData("SHA512", "87aa7cdea5ef619d4ff0b4241a1d6cb02379f4e2ce4ec2787ad0b30545e17cdedaa833b7d6b8a702038b274eaea3f4e4be9d914eeb61f1702e696c203a126854")]
    public void Hmac_Rfc4231TestCase1_GivesTheStandardValue(string hashName, string expected)
    {
        var hash = NoiseKeys.Hash(hashName);
        var key = Enumerable.Repeat((byte)0x0b, 20).ToArray();
        var actual = new byte[hash.HashLength];

        hash.Hmac(key, "Hi There"u8, default, actual);

        Assert.Equal(expected, Convert.ToHexStringLower(actual));
    }

    [Theory]
    [InlineData(
        "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f",
        "616263",
        "cfb7fadba5986a2320ddff615fe9386cf3927144d40c77320914b152bb06ab8e04e835035bab5ff0f4518528fe5ce4a190d75b7b4cc03337b78f8c9d8a0761e5")]
    [InlineData(
        "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f",
        "",
        "178056d90badbce5e533b4b4359a64b51cd95e3ed81262b62137f5de78de7523012b4f82839911b387702bda131597fb0a8f38304806f162184ab1c229f515db")]
    [InlineData(
        "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b",
        "4869205468657265",
        "358a6a184924894fc34bee5680eedf57d84a37bb38832f288e3b27dc63a98cc8c91e76da476b508bc6b2d408a248857452906e4a20b48c6b4b55d2df0fe1dd24")]
    [InlineData(
        "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f",
        "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff",
        "2339d5c02fb49d7039d8bcf09cd46999aefb03b2498c7668e0c294a626dc6afe10d48608af1c89f03f817f97362cd472dc5ba663f7acd73ca37bd1cf67a9eb08")]
    public void Hmac_Blake2b_EqualsAnIndependentImplementation(string keyHex, string dataHex, string expected)
    {
        var actual = new byte[NoiseHash.Blake2b.HashLength];

        NoiseHash.Blake2b.Hmac(Convert.FromHexString(keyHex), Convert.FromHexString(dataHex), default, actual);

        Assert.Equal(expected, Convert.ToHexStringLower(actual));
    }

    [Fact]
    public void Hmac_DataInTwoParts_EqualsTheDataInOnePart()
    {
        var key = RandomNumberGenerator.GetBytes(64);
        var data = RandomNumberGenerator.GetBytes(300);
        var whole = new byte[NoiseHash.Blake2b.HashLength];
        var parts = new byte[NoiseHash.Blake2b.HashLength];

        NoiseHash.Blake2b.Hmac(key, data, default, whole);
        NoiseHash.Blake2b.Hmac(key, data.AsSpan(0, 100), data.AsSpan(100), parts);

        Assert.Equal(Convert.ToHexStringLower(whole), Convert.ToHexStringLower(parts));
    }

    [Fact]
    public void Hmac_KeyLongerThanTheBlock_IsRefused()
    {
        var output = new byte[NoiseHash.Sha256.HashLength];

        Assert.Throws<ArgumentException>(() => NoiseHash.Sha256.Hmac(new byte[65], default, default, output));
    }
}
