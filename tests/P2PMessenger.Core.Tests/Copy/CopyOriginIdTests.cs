using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;

namespace P2PMessenger.Core.Tests.Copy;

public sealed class CopyOriginIdTests
{
    [Fact]
    [Trait("Story", "US-22")]
    public void ForContactAndForGroup_IndependentVectors_MatchByteForByte()
    {
        Assert.True(Ed25519PublicKey.TryCreate(Convert.FromHexString(CopyVectors.AliceIdentityKey), out var alice));
        Assert.True(Ed25519PublicKey.TryCreate(Convert.FromHexString(CopyVectors.BobIdentityKey), out var bob));
        Assert.True(GroupId.TryCreate(Convert.FromHexString(CopyVectors.GroupIdHex), out var group));

        var contactOrigin = CopyOriginId.ForContact(alice, bob);
        var groupOrigin = CopyOriginId.ForGroup(alice, group);

        Assert.Equal(CopyVectors.ContactOrigin, Convert.ToHexStringLower(contactOrigin.Bytes));
        Assert.Equal(CopyVectors.GroupOrigin, Convert.ToHexStringLower(groupOrigin.Bytes));
        Assert.NotEqual(contactOrigin, groupOrigin);
    }

    [Fact]
    [Trait("Story", "US-22")]
    public void OriginIds_DifferentOwnersOrTargets_AreDistinct()
    {
        using var alice = Ed25519KeyPair.Create();
        using var bob = Ed25519KeyPair.Create();
        using var carol = Ed25519KeyPair.Create();

        var aToB = CopyOriginId.ForContact(alice.PublicKey, bob.PublicKey);
        var bToA = CopyOriginId.ForContact(bob.PublicKey, alice.PublicKey);
        var aToC = CopyOriginId.ForContact(alice.PublicKey, carol.PublicKey);

        Assert.NotEqual(aToB, bToA);
        Assert.NotEqual(aToB, aToC);
        Assert.False(CopyOriginId.TryCreate(new byte[31], out _));
        Assert.False(CopyOriginId.TryCreate(new byte[33], out _));
    }
}
