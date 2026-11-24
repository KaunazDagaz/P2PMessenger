using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Core.Tests.Identity;

[Trait("Story", "US-09")]
public sealed class OnionServiceKeyTests
{
    [Theory]
    [InlineData(TestKeys.Rfc8032Seed1, "MHyDhk8oM8tCei7xwAoBPP3/J2jZgMCjpSDwBpBN6U+bTwr+KAt0aneGhOdUQlAgV7dHOgPwj5b1o46Sh+Afjw==")]
    [InlineData(TestKeys.Rfc8032Seed2, "aL2e11iC1SgVqXWFyvR5Cn9sazt/ghxeJZoksC5QLlFFZoSCkdrK8iXMY96zSNoxjiwuF7ALgWD5zmv6BHKRHQ==")]
    [InlineData(TestKeys.Rfc8032Seed3, "kJqLdV7ZAoSQI6VbFcI9EbpNf07FwvUbEyWhgZkeqVxmCMhma5zeIyX1OdfYM4b+gYfGvmHYpwwkcZDWTt9fHg==")]
    [InlineData(TestKeys.Rfc8032SeedSha, "MN3OT1ngS+wLVxPxmwegknLY29ZdqKX0Go9Sx3JBtkXdOsl5e31yujOn6a4EFnj2C9hcAyai9K6KdhGzYgtUpw==")]
    public void TryExpand_Rfc8032Seeds_GivesTheExpandedKeysOfAnIndependentImplementation(string seedHex, string expectedBase64)
    {
        var expanded = new byte[OnionServiceKey.ExpandedLength];

        Assert.True(OnionServiceKey.TryExpand(Convert.FromHexString(seedHex), expanded));

        Assert.Equal(expectedBase64, Convert.ToBase64String(expanded));
    }

    [Fact]
    public void TryExpand_AnySeed_ClampsTheScalarAsEd25519Requires()
    {
        var seed = new byte[OnionServiceKey.SeedLength];
        var expanded = new byte[OnionServiceKey.ExpandedLength];

        for (var value = 0; value < 256; value++)
        {
            Array.Fill(seed, (byte)value);
            Assert.True(OnionServiceKey.TryExpand(seed, expanded));
            Assert.Equal(0, expanded[0] & 7);
            Assert.Equal(64, expanded[31] & 192);
        }
    }

    [Theory]
    [InlineData(31, 64)]
    [InlineData(33, 64)]
    [InlineData(32, 63)]
    [InlineData(0, 64)]
    public void TryExpand_WrongLengths_ReturnsFalse(int seedLength, int destinationLength)
    {
        Assert.False(OnionServiceKey.TryExpand(new byte[seedLength], new byte[destinationLength]));
    }

    [Fact]
    public void TryExportOnionServiceKey_TestKeySet_GivesTheExpandedOnionSeed()
    {
        using var keys = TestKeys.Alice();
        var exported = new byte[OnionServiceKey.ExpandedLength];
        var expected = new byte[OnionServiceKey.ExpandedLength];

        Assert.True(keys.TryExportOnionServiceKey(exported));
        Assert.True(OnionServiceKey.TryExpand(Convert.FromHexString(TestKeys.Rfc8032Seed2), expected));

        Assert.Equal(expected, exported);
    }

    [Fact]
    public void TryExportOnionServiceKey_ShortDestination_ReturnsFalse()
    {
        using var keys = KeySet.Create();

        Assert.False(keys.TryExportOnionServiceKey(new byte[OnionServiceKey.ExpandedLength - 1]));
    }

    [Fact]
    public void TryExportOnionServiceKey_NewKeySet_DiffersFromTheIdentityKeyExpansion()
    {
        using var keys = KeySet.Create();
        var exported = new byte[OnionServiceKey.ExpandedLength];
        var seeds = new byte[KeySet.ExportLength];

        Assert.True(keys.TryExportOnionServiceKey(exported));
        Assert.True(keys.TryExport(seeds, out _));
        var identityExpansion = new byte[OnionServiceKey.ExpandedLength];
        Assert.True(OnionServiceKey.TryExpand(seeds.AsSpan(1, Ed25519KeyPair.SeedLength), identityExpansion));

        Assert.NotEqual(identityExpansion, exported);
    }
}
