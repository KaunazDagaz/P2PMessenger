using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Tests.Groups;

public sealed class GroupRecordTests
{
    [Fact]
    [Trait("Story", "US-12")]
    public void Create_VectorInputs_GivesTheRecordOfAnIndependentImplementation()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupSamples.VectorKey();

        var record = GroupSamples.VectorRecord(alice, key);

        Assert.Equal(GroupVectors.Record, Convert.ToHexStringLower(record.ToBytes()));
        Assert.Equal(GroupVectors.RecordHash, Convert.ToHexStringLower(record.Hash));
        Assert.Equal(GroupVectors.KeyCheck, Convert.ToHexStringLower(record.KeyCheck));
        Assert.Equal(GroupVectors.KeyCheck, Convert.ToHexStringLower(key.Check));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public void TryParse_VectorRecord_GivesItsFieldsWithTheMembersInKeyOrder()
    {
        Assert.True(GroupRecord.TryParse(Convert.FromHexString(GroupVectors.Record), out var record));

        Assert.Equal(GroupVectors.GroupId, Convert.ToHexStringLower(record.GroupId.Bytes));
        Assert.Equal(1, record.Epoch);
        Assert.Equal("Field team", record.Name.Value);
        Assert.Equal(["Carol", "Alice", "Bob"], record.Members.Select(member => member.Name.Value));
        Assert.Equal(TestKeys.Rfc8032Public1, Convert.ToHexStringLower(record.Signer.Bytes));
        Assert.Equal(GroupVectors.RecordHash, Convert.ToHexStringLower(record.Hash));
        Assert.True(record.HasMember(GroupSamples.Key(TestKeys.Rfc8032Public3)));
        Assert.False(record.HasMember(GroupSamples.Key(TestKeys.Rfc8032PublicSha)));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public void Fits_TheRecordsKey_IsTrueAndAnotherKeyIsFalse()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupSamples.VectorKey();
        using var other = GroupKey.Create();

        var record = GroupSamples.VectorRecord(alice, key);

        Assert.True(record.Fits(key));
        Assert.False(record.Fits(other));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public void Create_TheSameMembersInAnotherOrder_GivesTheSameRecord()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupSamples.VectorKey();

        var reversed = GroupRecord.Create(GroupSamples.VectorGroupId(), 1, TestKeys.Name("Field team"), key, GroupSamples.VectorMembers().Reverse(), alice);

        Assert.Equal(GroupVectors.Record, Convert.ToHexStringLower(reversed.ToBytes()));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public void Create_SignerWhoIsNotAMember_Throws()
    {
        using var bob = TestKeys.Bob();
        using var key = GroupKey.Create();
        var members = GroupSamples.VectorMembers().Where(member => member.Name.Value != "Bob");

        Assert.Throws<ArgumentException>(() => GroupRecord.Create(GroupId.Create(), 1, TestKeys.Name("Team"), key, members, bob));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public void Create_APersonTwice_Throws()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupKey.Create();
        GroupMember[] members = [.. GroupSamples.VectorMembers(), new(GroupSamples.Key(TestKeys.Rfc8032Public3), TestKeys.Name("Bob again"))];

        Assert.Throws<ArgumentException>(() => GroupRecord.Create(GroupId.Create(), 1, TestKeys.Name("Team"), key, members, alice));
    }

    [Theory]
    [Trait("Story", "US-12")]
    [InlineData(1)]
    [InlineData(161)]
    public void Create_TooFewOrTooManyMembers_Throws(int count)
    {
        using var alice = TestKeys.Alice();
        using var key = GroupKey.Create();
        var members = new List<GroupMember> { new(alice.IdentityKey, TestKeys.Name("Alice")) };
        while (members.Count < count)
        {
            using var other = KeySet.Create();
            members.Add(new GroupMember(other.IdentityKey, TestKeys.Name($"Member {members.Count}")));
        }

        Assert.Throws<ArgumentException>(() => GroupRecord.Create(GroupId.Create(), 1, TestKeys.Name("Team"), key, members, alice));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public void Create_TheLargestGroupWithTheLongestNames_FitsIntoAnInvitation()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupKey.Create();
        var longName = new string('x', DisplayName.MaxUtf8Length);
        var members = new List<GroupMember> { new(alice.IdentityKey, TestKeys.Name(longName)) };
        while (members.Count < GroupRecord.MaxMembers)
        {
            using var other = KeySet.Create();
            members.Add(new GroupMember(other.IdentityKey, TestKeys.Name(longName)));
        }

        var record = GroupRecord.Create(GroupId.Create(), GroupRecord.MaxEpoch, TestKeys.Name(longName), key, members, alice);
        var plaintext = MessageCodec.Encode(GroupInvitation.Create(key, record));

        Assert.Equal(GroupRecord.MaxLength, record.Length);
        Assert.True(MessageCodec.TryDecode(plaintext, out var decoded));
        Assert.Equal(record, Assert.IsType<GroupInvitation>(decoded).Record);
    }

    [Fact]
    [Trait("Story", "US-12")]
    [Trait("NFR", "NFR-09")]
    public void TryParse_AnySingleBitFlip_IsRejected()
    {
        var bytes = Convert.FromHexString(GroupVectors.Record);

        for (var bit = 0; bit < bytes.Length * 8; bit++)
        {
            var changed = (byte[])bytes.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            Assert.False(GroupRecord.TryParse(changed, out _), $"bit {bit} of {bytes.Length * 8}");
        }
    }

    [Fact]
    [Trait("Story", "US-12")]
    [Trait("NFR", "NFR-09")]
    public void TryParse_EveryTruncationAndAnythingAppended_IsRejected()
    {
        var bytes = Convert.FromHexString(GroupVectors.Record);

        for (var length = 0; length < bytes.Length; length++)
        {
            Assert.False(GroupRecord.TryParse(bytes.AsSpan(0, length), out _), $"length {length}");
        }

        foreach (var extra in new byte[][] { [0], [255], new byte[64] })
        {
            Assert.False(GroupRecord.TryParse([.. bytes, .. extra], out _), $"{extra.Length} bytes appended");
        }
    }

    [Fact]
    [Trait("Story", "US-12")]
    [Trait("NFR", "NFR-09")]
    public void TryParse_RecordSignedByAMemberWhoIsNotTheNamedSigner_IsRejected()
    {
        using var alice = TestKeys.Alice();
        using var bob = TestKeys.Bob();
        using var key = GroupSamples.VectorKey();
        var record = GroupRecord.Create(GroupSamples.VectorGroupId(), 1, TestKeys.Name("Field team"), key, GroupSamples.VectorMembers(), bob).ToBytes();
        var signerOffset = record.Length - 64 - 32;
        alice.IdentityKey.Bytes.CopyTo(record.AsSpan(signerOffset));

        Assert.False(GroupRecord.TryParse(record, out _));
    }
}
