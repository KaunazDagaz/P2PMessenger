using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Text;

namespace P2PMessenger.Core.Tests.Identity;

public sealed class OnionAddressTests
{
    [Theory]
    [InlineData(TestKeys.Rfc8032Public1, "25njqamcweflpvkl73j4szahhihoc4xt3ktcgjnpaingr5yhkenl5sid")]
    [InlineData(TestKeys.Rfc8032Public2, "hvabpq7iioevvevxbktu2g36xsojqlgpf3cjndgazvk7ckxumygcmyyd")]
    [InlineData(TestKeys.Rfc8032Public3, "7ri43dtcdcq2hdnep3iaemhqlaebn3itxizqhlc55oirkseqqasxldad")]
    [InlineData(TestKeys.Rfc8032PublicSha, "5qlsxe5nlzldx5etfryocjcqgtbviz7pf36u2zhl7amwqndh4k7zx7ad")]
    public void FromPublicKey_KeysWhoseAddressTorReturned_GivesTheSameAddress(string publicHex, string torAddress)
    {
        var address = OnionAddress.FromPublicKey(PublicKey(publicHex));

        Assert.Equal(torAddress, address.Label);
        Assert.Equal(torAddress + ".onion", address.Host);
    }

    [Theory]
    [InlineData("d1b38b83a83b3ed918c5bb69dd444ad56bc8d5835a914de73447474e5f02591b", "2gzyxa5ihm7nsggfxnu52rck2vv4rvmdlkiu3zzui5du4xyclen53wid")]
    [InlineData("1d04a1d04a338c6e6ae970bfabee49049d6702250984ca950c01673f4ec034ad", "duckduckgogg42xjoc72x3sjasowoarfbgcmvfimaftt6twagswzczad")]
    public void FromPublicKey_AddressesPublishedByTheirServices_GivesTheSameAddress(string publicHex, string publishedAddress)
    {
        var address = OnionAddress.FromPublicKey(PublicKey(publicHex));

        Assert.Equal(publishedAddress, address.Label);
    }

    [Fact]
    public void FromPublicKey_AnyKey_GivesTheFieldsOfAVersion3Address()
    {
        using var keyPair = Ed25519KeyPair.Create();

        var address = OnionAddress.FromPublicKey(keyPair.PublicKey);

        Assert.Equal(OnionAddress.LabelLength, address.Label.Length);
        Assert.Equal(address.Label.ToLowerInvariant(), address.Label);
        var raw = new byte[35];
        Assert.True(Base32.TryDecode(address.Label, raw, out var written));
        Assert.Equal(35, written);
        Assert.Equal(keyPair.PublicKey.Bytes.ToArray(), raw[..32]);
        Assert.Equal(3, raw[34]);
        Assert.Equal(keyPair.PublicKey, address.PublicKey);
    }

    [Fact]
    public void FromPublicKey_ChangedKey_GivesAnotherAddress()
    {
        var key = Convert.FromHexString(TestKeys.Rfc8032Public1);
        var original = OnionAddress.FromPublicKey(PublicKey(TestKeys.Rfc8032Public1));

        for (var bit = 0; bit < key.Length * 8; bit++)
        {
            var changed = (byte[])key.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));
            Assert.True(Ed25519PublicKey.TryCreate(changed, out var changedKey));

            Assert.NotEqual(original, OnionAddress.FromPublicKey(changedKey));
        }
    }

    [Fact]
    public void Equals_SameKey_IsTrue()
    {
        var first = OnionAddress.FromPublicKey(PublicKey(TestKeys.Rfc8032Public1));
        var second = OnionAddress.FromPublicKey(PublicKey(TestKeys.Rfc8032Public1));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Bytes_OfAnAddress_AreTheKeyTheChecksumAndTheVersion()
    {
        var address = OnionAddress.FromPublicKey(PublicKey(TestKeys.Rfc8032Public1));

        Assert.Equal(OnionAddress.Length, address.Bytes.Length);
        Assert.Equal(Convert.FromHexString(TestKeys.Rfc8032Public1 + "bec903"), address.Bytes.ToArray());
    }

    [Theory]
    [InlineData(TestKeys.Rfc8032Public1)]
    [InlineData(TestKeys.Rfc8032Public2)]
    [InlineData(TestKeys.Rfc8032Public3)]
    [InlineData(TestKeys.Rfc8032PublicSha)]
    public void TryCreate_BytesOfAnAddress_GivesTheSameAddress(string publicHex)
    {
        var original = OnionAddress.FromPublicKey(PublicKey(publicHex));

        Assert.True(OnionAddress.TryCreate(original.Bytes, out var parsed));

        Assert.Equal(original, parsed);
        Assert.Equal(original.Label, parsed.Label);
        Assert.Equal(original.PublicKey, parsed.PublicKey);
    }

    [Theory]
    [InlineData("2gzyxa5ihm7nsggfxnu52rck2vv4rvmdlkiu3zzui5du4xyclen53wid")]
    [InlineData("duckduckgogg42xjoc72x3sjasowoarfbgcmvfimaftt6twagswzczad")]
    public void TryCreate_BytesOfAddressesPublishedByTheirServices_AreAccepted(string publishedAddress)
    {
        var bytes = new byte[OnionAddress.Length];
        Assert.True(Base32.TryDecode(publishedAddress, bytes, out var written));
        Assert.Equal(OnionAddress.Length, written);

        Assert.True(OnionAddress.TryCreate(bytes, out var address));

        Assert.Equal(publishedAddress, address.Label);
        Assert.Equal(publishedAddress + ".onion", address.Host);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(34)]
    [InlineData(36)]
    [InlineData(56)]
    public void TryCreate_WrongLength_IsRejected(int length)
    {
        Assert.False(OnionAddress.TryCreate(new byte[length], out _));
    }

    [Fact]
    public void TryCreate_AnyOtherVersionByte_IsRejected()
    {
        var bytes = OnionAddress.FromPublicKey(PublicKey(TestKeys.Rfc8032Public1)).Bytes.ToArray();

        for (var version = 0; version < 256; version++)
        {
            bytes[^1] = (byte)version;
            var accepted = OnionAddress.TryCreate(bytes, out _);

            if (version == 3)
            {
                Assert.True(accepted);
            }
            else
            {
                Assert.False(accepted, $"version {version}");
            }
        }
    }

    [Fact]
    public void TryCreate_AnySingleBitChangeOfTheChecksum_IsRejected()
    {
        var bytes = OnionAddress.FromPublicKey(PublicKey(TestKeys.Rfc8032Public1)).Bytes.ToArray();

        for (var bit = 32 * 8; bit < 34 * 8; bit++)
        {
            var changed = (byte[])bytes.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            Assert.False(OnionAddress.TryCreate(changed, out _), $"bit {bit}");
        }
    }

    [Theory]
    [InlineData(TestKeys.Rfc8032Public1)]
    [InlineData(TestKeys.Rfc8032Public2)]
    [InlineData(TestKeys.Rfc8032Public3)]
    [InlineData(TestKeys.Rfc8032PublicSha)]
    public void TryCreate_AnySingleBitChangeOfAReferenceKey_IsRejected(string publicHex)
    {
        var bytes = OnionAddress.FromPublicKey(PublicKey(publicHex)).Bytes.ToArray();

        for (var bit = 0; bit < 32 * 8; bit++)
        {
            var changed = (byte[])bytes.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            Assert.False(OnionAddress.TryCreate(changed, out _), $"bit {bit}");
        }
    }

    private static Ed25519PublicKey PublicKey(string hex)
    {
        Assert.True(Ed25519PublicKey.TryCreate(Convert.FromHexString(hex), out var key));
        return key;
    }
}
