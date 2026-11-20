using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Tests.Protection;

public sealed class RecordKeysTests
{
    private static readonly RecordLocation NameLocation = new("person", "display_name_enc", 2);

    [Fact]
    public void HkdfSha256_Rfc5869TestCase1_GivesThePublishedKeys()
    {
        var hkdf = KeyDerivationAlgorithm.HkdfSha256;

        var prk = hkdf.Extract(RecordVectors.Bytes(RecordVectors.Rfc5869Ikm), RecordVectors.Bytes(RecordVectors.Rfc5869Salt));
        var okm = hkdf.Expand(prk, RecordVectors.Bytes(RecordVectors.Rfc5869Info), 42);

        Assert.Equal(RecordVectors.Rfc5869Prk, Convert.ToHexStringLower(prk));
        Assert.Equal(RecordVectors.Rfc5869Okm, Convert.ToHexStringLower(okm));
    }

    [Fact]
    public void XChaCha20Poly1305_DraftVector_GivesThePublishedCiphertextAndTag()
    {
        using var key = Key.Import(AeadAlgorithm.XChaCha20Poly1305, RecordVectors.Bytes(RecordVectors.XChaChaKey), KeyBlobFormat.RawSymmetricKey);

        var output = AeadAlgorithm.XChaCha20Poly1305.Encrypt(
            key,
            RecordVectors.Bytes(RecordVectors.XChaChaNonce),
            RecordVectors.Bytes(RecordVectors.XChaChaAssociatedData),
            Encoding.ASCII.GetBytes(RecordVectors.XChaChaPlaintext));

        Assert.Equal(RecordVectors.XChaChaCiphertext + RecordVectors.XChaChaTag, Convert.ToHexStringLower(output));
    }

    [Fact]
    public void AssociatedData_OfAValue_MatchesTheIndependentEncoding()
    {
        Assert.Equal(RecordVectors.NameAssociatedData, Convert.ToHexStringLower(NameLocation.AssociatedData()));
    }

    [Fact]
    [Trait("Story", "US-20")]
    public void TryOpen_ValueSealedByTheIndependentImplementation_GivesItsPlaintext()
    {
        using var keys = RecordKeys.Derive(RecordVectors.Bytes(RecordVectors.DataKey));

        Assert.True(keys.TryOpen(NameLocation, RecordVectors.Bytes(RecordVectors.SealedName), out var name));
        Assert.Equal("Alice", Encoding.UTF8.GetString(name));
        Assert.True(keys.TryOpen(new RecordLocation("message", "text_enc", 7), RecordVectors.Bytes(RecordVectors.SealedEmptyText), out var empty));
        Assert.Empty(empty);
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData(LookupDomain.IdentityKey, RecordVectors.IdentityKeyLookup)]
    [InlineData(LookupDomain.StaticKey, RecordVectors.StaticKeyLookup)]
    [InlineData(LookupDomain.MessageId, RecordVectors.MessageIdLookup)]
    public void Lookup_OfAValue_MatchesTheIndependentMac(LookupDomain domain, string expected)
    {
        using var keys = RecordKeys.Derive(RecordVectors.Bytes(RecordVectors.DataKey));

        var lookup = keys.Lookup(domain, RecordVectors.Bytes(RecordVectors.LookupValue));

        Assert.Equal(expected, Convert.ToHexStringLower(lookup));
        Assert.True(keys.MatchesLookup(domain, RecordVectors.Bytes(RecordVectors.LookupValue), lookup));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(97)]
    [InlineData(16348)]
    public void Seal_AValue_IsANonceTheCiphertextAndATagThatOpen(int length)
    {
        using var keys = NewKeys();
        var value = new byte[length];
        RandomNumberGenerator.Fill(value);

        var sealedValue = keys.Seal(NameLocation, value);

        Assert.Equal(length + RecordKeys.Overhead, sealedValue.Length);
        Assert.True(keys.TryOpen(NameLocation, sealedValue, out var opened));
        Assert.Equal(value, opened);
    }

    [Fact]
    public void Seal_TheSameValueTwice_UsesAFreshNonce()
    {
        using var keys = NewKeys();

        var first = keys.Seal(NameLocation, "Alice"u8);
        var second = keys.Seal(NameLocation, "Alice"u8);

        Assert.NotEqual(first[..RecordKeys.NonceLength], second[..RecordKeys.NonceLength]);
        Assert.NotEqual(first, second);
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public void TryOpen_EveryFlippedBit_IsRefused()
    {
        using var keys = RecordKeys.Derive(RecordVectors.Bytes(RecordVectors.DataKey));
        var sealedValue = RecordVectors.Bytes(RecordVectors.SealedName);

        for (var bit = 0; bit < sealedValue.Length * 8; bit++)
        {
            var changed = (byte[])sealedValue.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            Assert.False(keys.TryOpen(NameLocation, changed, out _), $"bit {bit}");
        }
    }

    [Fact]
    public void TryOpen_EveryShorterOrLongerValue_IsRefused()
    {
        using var keys = RecordKeys.Derive(RecordVectors.Bytes(RecordVectors.DataKey));
        var sealedValue = RecordVectors.Bytes(RecordVectors.SealedName);

        for (var length = 0; length < sealedValue.Length; length++)
        {
            Assert.False(keys.TryOpen(NameLocation, sealedValue.AsSpan(0, length), out _), $"length {length}");
        }

        Assert.False(keys.TryOpen(NameLocation, [.. sealedValue, 0], out _));
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData("contact", "display_name_enc", 2)]
    [InlineData("person", "identity_key_enc", 2)]
    [InlineData("person", "display_name_enc", 1)]
    [InlineData("person", "display_name_enc", 3)]
    public void TryOpen_ValueMovedToAnotherTableColumnOrRow_IsRefused(string table, string column, long row)
    {
        using var keys = RecordKeys.Derive(RecordVectors.Bytes(RecordVectors.DataKey));

        Assert.False(keys.TryOpen(new RecordLocation(table, column, row), RecordVectors.Bytes(RecordVectors.SealedName), out _));
    }

    [Fact]
    [Trait("NFR", "NFR-11")]
    public void TryOpen_UnderAnotherDataKey_IsRefused()
    {
        using var keys = NewKeys();

        Assert.False(keys.TryOpen(NameLocation, RecordVectors.Bytes(RecordVectors.SealedName), out _));
    }

    [Fact]
    public void Lookup_SameValueInAnotherDomainOrUnderAnotherKey_Differs()
    {
        using var keys = RecordKeys.Derive(RecordVectors.Bytes(RecordVectors.DataKey));
        using var other = NewKeys();
        var value = RecordVectors.Bytes(RecordVectors.LookupValue);

        var identity = keys.Lookup(LookupDomain.IdentityKey, value);

        Assert.Equal(RecordKeys.LookupLength, identity.Length);
        Assert.NotEqual(identity, keys.Lookup(LookupDomain.StaticKey, value));
        Assert.NotEqual(identity, other.Lookup(LookupDomain.IdentityKey, value));
        Assert.False(keys.MatchesLookup(LookupDomain.IdentityKey, value, identity.AsSpan(0, 31)));
        Assert.False(keys.MatchesLookup(LookupDomain.StaticKey, value, identity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void Derive_DataKeyOfAnotherLength_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => RecordKeys.Derive(new byte[length]));
    }

    private static RecordKeys NewKeys()
    {
        var dataKey = new byte[RecordKeys.DataKeyLength];
        RandomNumberGenerator.Fill(dataKey);
        return RecordKeys.Derive(dataKey);
    }
}
