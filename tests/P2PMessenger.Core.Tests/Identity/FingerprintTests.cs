using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Core.Tests.Identity;

public sealed class FingerprintTests
{
    [Theory]
    [Trait("Story", "US-04")]
    [InlineData(TestKeys.Rfc8032Public1, "48720 76951 38111 09183 33093 40162")]
    [InlineData(TestKeys.Rfc8032Public2, "45022 98735 41532 01436 62574 92776")]
    [InlineData(TestKeys.Rfc8032Public3, "55455 55661 63967 27241 39015 40442")]
    [InlineData(TestKeys.Rfc8032PublicSha, "48682 07390 43848 29707 05772 26008")]
    public void Of_ReferenceKeys_GivesTheFingerprintOfTheIndependentImplementation(string publicHex, string expected)
    {
        var fingerprint = Fingerprint.Of(PublicKey(publicHex));

        Assert.Equal(expected, fingerprint.ToString());
        Assert.Equal(expected, fingerprint.ToGroupedString());
        Assert.Equal(expected.Replace(" ", string.Empty, StringComparison.Ordinal), fingerprint.Digits);
    }

    [Fact]
    [Trait("Story", "US-04")]
    public void Of_AnyKey_GivesThirtyDigitsInSixGroupsOfFive()
    {
        for (var count = 0; count < 100; count++)
        {
            using var keyPair = Ed25519KeyPair.Create();

            var fingerprint = Fingerprint.Of(keyPair.PublicKey);

            Assert.Equal(Fingerprint.DigitCount, fingerprint.Digits.Length);
            Assert.All(fingerprint.Digits, digit => Assert.InRange(digit, '0', '9'));
            var groups = fingerprint.ToGroupedString().Split(' ');
            Assert.Equal(6, groups.Length);
            Assert.All(groups, group => Assert.Equal(Fingerprint.GroupSize, group.Length));
        }
    }

    [Fact]
    [Trait("Story", "US-04")]
    public void Of_SameKeyTwice_GivesTheSameFingerprint()
    {
        var first = Fingerprint.Of(PublicKey(TestKeys.Rfc8032Public1));
        var second = Fingerprint.Of(PublicKey(TestKeys.Rfc8032Public1));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    [Trait("Story", "US-04")]
    public void Of_AnySingleBitChangeOfTheKey_GivesAnotherFingerprint()
    {
        var key = Convert.FromHexString(TestKeys.Rfc8032Public1);
        var original = Fingerprint.Of(PublicKey(TestKeys.Rfc8032Public1));

        for (var bit = 0; bit < key.Length * 8; bit++)
        {
            var changed = (byte[])key.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));
            Assert.True(Ed25519PublicKey.TryCreate(changed, out var changedKey));

            Assert.NotEqual(original, Fingerprint.Of(changedKey));
        }
    }

    [Fact]
    [Trait("Story", "US-04")]
    public void Pair_SeenFromBothSides_ShowsTheSameTwoFingerprintsInTheSameOrder()
    {
        var alice = PublicKey(TestKeys.Rfc8032Public1);
        var bob = PublicKey(TestKeys.Rfc8032Public3);

        var onAlicesPhone = FingerprintPair.Create(alice, bob);
        var onBobsPhone = FingerprintPair.Create(bob, alice);

        Assert.Equal(onAlicesPhone.First, onBobsPhone.First);
        Assert.Equal(onAlicesPhone.Second, onBobsPhone.Second);
        Assert.NotEqual(onAlicesPhone.OwnFirst, onBobsPhone.OwnFirst);
        Assert.Equal(Fingerprint.Of(alice), onAlicesPhone.Own);
        Assert.Equal(Fingerprint.Of(bob), onAlicesPhone.Contact);
        Assert.Equal(Fingerprint.Of(bob), onBobsPhone.Own);
        Assert.Equal(Fingerprint.Of(alice), onBobsPhone.Contact);
    }

    [Fact]
    [Trait("Story", "US-04")]
    public void Pair_OrderFollowsTheIdentityKeyBytes()
    {
        var lower = PublicKey(TestKeys.Rfc8032Public1);
        var higher = PublicKey(TestKeys.Rfc8032Public3);
        Assert.True(lower.Bytes.SequenceCompareTo(higher.Bytes) < 0);

        var pair = FingerprintPair.Create(lower, higher);

        Assert.True(pair.OwnFirst);
        Assert.Equal(Fingerprint.Of(lower), pair.First);
        Assert.Equal(Fingerprint.Of(higher), pair.Second);
    }

    [Fact]
    [Trait("Story", "US-04")]
    public void Pair_ReplacedContactKey_ChangesTheSideThatWasReplaced()
    {
        var alice = PublicKey(TestKeys.Rfc8032Public1);
        var bob = PublicKey(TestKeys.Rfc8032Public3);
        var mallory = PublicKey(TestKeys.Rfc8032Public2);

        var honest = FingerprintPair.Create(alice, bob);
        var swapped = FingerprintPair.Create(alice, mallory);

        Assert.Equal(honest.Own, swapped.Own);
        Assert.NotEqual(honest.Contact, swapped.Contact);
    }

    private static Ed25519PublicKey PublicKey(string hex)
    {
        Assert.True(Ed25519PublicKey.TryCreate(Convert.FromHexString(hex), out var key));
        return key;
    }
}
