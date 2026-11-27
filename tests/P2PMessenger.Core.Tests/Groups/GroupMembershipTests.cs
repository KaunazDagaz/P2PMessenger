using System.Buffers.Binary;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Tests.Groups;

public sealed class GroupMembershipTests
{
    [Fact]
    [Trait("Story", "US-14")]
    public void Encode_UpdateLeaveAndRemoved_MatchIndependentPythonVectorsByteForByte()
    {
        using var alice = TestKeys.Alice();
        using var key2 = GroupKey.TryImport(Convert.FromHexString(GroupVectors.Key2), out var imported) ? imported : throw new InvalidOperationException();
        var groupId = GroupSamples.VectorGroupId();
        var record2 = GroupRecord.Create(
            groupId,
            2,
            TestKeys.Name("Field team"),
            key2,
            [
                new GroupMember(GroupSamples.Key(TestKeys.Rfc8032Public1), TestKeys.Name("Alice")),
                new GroupMember(GroupSamples.Key(TestKeys.Rfc8032Public3), TestKeys.Name("Bob")),
            ],
            alice);
        var update = GroupUpdate.Create(key2, record2);
        var updatePlaintext = MessageCodec.Encode(update);

        using var carol = TestKeys.FromHex(TestKeys.Rfc8032Seed2, TestKeys.Rfc7748AlicePrivate, TestKeys.Rfc8032Seed1);
        var carolLeave = GroupLeave.Create(groupId, 1, carol);
        var carolLeavePlaintext = MessageCodec.Encode(carolLeave);

        var aliceLeave = GroupLeave.Create(groupId, 1, alice, GroupSamples.Key(TestKeys.Rfc8032Public3));
        var aliceLeavePlaintext = MessageCodec.Encode(aliceLeave);

        var removed = GroupRemoved.Create(groupId, 2, carol.IdentityKey, alice);
        var removedPlaintext = MessageCodec.Encode(removed);

        Assert.Equal(GroupVectors.Record2, Convert.ToHexStringLower(record2.ToBytes()));
        Assert.Equal(GroupVectors.Record2Hash, Convert.ToHexStringLower(record2.Hash));
        Assert.Equal(GroupVectors.UpdateContent, Content(updatePlaintext));
        Assert.Equal(GroupVectors.UpdateNoticeId, Convert.ToHexStringLower(update.NoticeId.Bytes));

        Assert.Equal(GroupLeave.MinLength, carolLeave.ToBytes().Length);
        Assert.Equal(MessageCodec.MinPaddedLength, carolLeavePlaintext.Length);
        Assert.Equal(GroupVectors.LeaveCarolBody, Convert.ToHexStringLower(carolLeave.ToBytes()));
        Assert.Equal(GroupVectors.LeaveCarolContent, Content(carolLeavePlaintext));
        Assert.Equal(GroupVectors.LeaveCarolNoticeId, Convert.ToHexStringLower(carolLeave.NoticeId.Bytes));
        Assert.True(carolLeave.HasValidSignature());

        Assert.Equal(GroupLeave.MaxLength, aliceLeave.ToBytes().Length);
        Assert.Equal(MessageCodec.MinPaddedLength, aliceLeavePlaintext.Length);
        Assert.Equal(GroupVectors.LeaveAliceBody, Convert.ToHexStringLower(aliceLeave.ToBytes()));
        Assert.Equal(GroupVectors.LeaveAliceContent, Content(aliceLeavePlaintext));
        Assert.Equal(GroupVectors.LeaveAliceNoticeId, Convert.ToHexStringLower(aliceLeave.NoticeId.Bytes));
        Assert.True(aliceLeave.HasValidSignature());

        Assert.Equal(GroupRemoved.Length, removed.ToBytes().Length);
        Assert.Equal(MessageCodec.MinPaddedLength, removedPlaintext.Length);
        Assert.Equal(GroupVectors.RemovedBody, Convert.ToHexStringLower(removed.ToBytes()));
        Assert.Equal(GroupVectors.RemovedContent, Content(removedPlaintext));
        Assert.Equal(GroupVectors.RemovedNoticeId, Convert.ToHexStringLower(removed.NoticeId.Bytes));
        Assert.True(removed.HasValidSignature());
    }

    [Fact]
    [Trait("Story", "US-14")]
    public void Decode_VectorUpdate_RoundTripsKeyRecordAndEquality()
    {
        var plaintext = Pad(Convert.FromHexString(GroupVectors.UpdateContent));

        Assert.True(MessageCodec.TryDecode(plaintext, out var decoded));
        var update = Assert.IsType<GroupUpdate>(decoded);
        using var openedKey = update.OpenKey();

        Assert.Equal(2, update.Record.Epoch);
        Assert.True(update.Record.Fits(openedKey));
        Assert.Equal(GroupVectors.UpdateNoticeId, Convert.ToHexStringLower(update.NoticeId.Bytes));
        Assert.Equal(plaintext, MessageCodec.Encode(update));
        Assert.Equal(update, Assert.IsType<GroupUpdate>(MessageCodec.TryDecode(plaintext, out var copy) ? copy : null));
    }

    [Fact]
    [Trait("Story", "US-14")]
    [Trait("NFR", "NFR-09")]
    public void Decode_EpochOneInGroupUpdate_IsRefused()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupSamples.VectorKey();
        var record1 = GroupSamples.VectorRecord(alice, key);
        byte[] raw = new byte[GroupKey.Length];
        key.Export(raw);
        byte[] content = [(byte)SessionMessageType.GroupUpdate, MessageCodec.CurrentVersion, .. raw, .. record1.ToBytes()];

        Assert.False(MessageCodec.TryDecode(Pad(content), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupUpdate.Create(key, record1));
    }

    [Fact]
    [Trait("Story", "US-14")]
    [Trait("NFR", "NFR-09")]
    public void Decode_GroupUpdateWithMismatchedKey_IsRefused()
    {
        using var alice = TestKeys.Alice();
        using var key2 = GroupKey.TryImport(Convert.FromHexString(GroupVectors.Key2), out var imported) ? imported : throw new InvalidOperationException();
        using var wrongKey = GroupKey.Create();
        var record2 = GroupRecord.TryParse(Convert.FromHexString(GroupVectors.Record2), out var parsed) ? parsed : throw new InvalidOperationException();
        byte[] raw = new byte[GroupKey.Length];
        wrongKey.Export(raw);
        byte[] content = [(byte)SessionMessageType.GroupUpdate, MessageCodec.CurrentVersion, .. raw, .. record2.ToBytes()];

        Assert.False(MessageCodec.TryDecode(Pad(content), out _));
        using var otherKey = GroupKey.Create();
        var otherRecord2 = GroupRecord.Create(
            GroupSamples.VectorGroupId(),
            2,
            TestKeys.Name("Field team"),
            otherKey,
            [
                new GroupMember(GroupSamples.Key(TestKeys.Rfc8032Public1), TestKeys.Name("Alice")),
                new GroupMember(GroupSamples.Key(TestKeys.Rfc8032Public3), TestKeys.Name("Bob")),
            ],
            alice);
        Assert.Throws<ArgumentException>(() => GroupUpdate.Create(wrongKey, record2));
        Assert.Throws<ArgumentException>(() => GroupUpdate.Create(key2, otherRecord2));
    }

    [Fact]
    [Trait("Story", "US-14")]
    [Trait("NFR", "NFR-09")]
    public void Decode_AnySingleBitFlipInGroupUpdate_IsRefused()
    {
        var plaintext = Pad(Convert.FromHexString(GroupVectors.UpdateContent));

        for (var bit = 0; bit < plaintext.Length * 8; bit++)
        {
            var changed = (byte[])plaintext.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            Assert.False(MessageCodec.TryDecode(changed, out _));
        }
    }

    [Fact]
    [Trait("Story", "US-14")]
    [Trait("NFR", "NFR-09")]
    public void TryParseAndDecode_AnySingleBitFlipInGroupLeave_IsRefusedOrFailsSignature()
    {
        foreach (var bodyHex in new[] { GroupVectors.LeaveCarolBody, GroupVectors.LeaveAliceBody })
        {
            var original = Convert.FromHexString(bodyHex);
            for (var bit = 0; bit < original.Length * 8; bit++)
            {
                var changed = (byte[])original.Clone();
                changed[bit / 8] ^= (byte)(1 << (bit % 8));

                if (GroupLeave.TryParse(changed, out var parsed))
                {
                    Assert.False(parsed.HasValidSignature());
                    Assert.Equal(changed, parsed.ToBytes());
                }
            }
        }

        foreach (var contentHex in new[] { GroupVectors.LeaveCarolContent, GroupVectors.LeaveAliceContent })
        {
            var plaintext = Pad(Convert.FromHexString(contentHex));
            for (var bit = 0; bit < plaintext.Length * 8; bit++)
            {
                var changed = (byte[])plaintext.Clone();
                changed[bit / 8] ^= (byte)(1 << (bit % 8));

                if (MessageCodec.TryDecode(changed, out var decoded))
                {
                    Assert.Equal(Convert.ToHexStringLower(changed), Convert.ToHexStringLower(MessageCodec.Encode(decoded)));
                    var leave = Assert.IsType<GroupLeave>(decoded);
                    Assert.False(leave.HasValidSignature());
                }
            }
        }
    }

    [Fact]
    [Trait("Story", "US-14")]
    [Trait("NFR", "NFR-09")]
    public void TryParse_GroupLeaveTruncationsExtensionsAndInvalidFlags_AreRefused()
    {
        var noSuccessor = Convert.FromHexString(GroupVectors.LeaveCarolBody);
        for (var length = 0; length < noSuccessor.Length; length++)
        {
            Assert.False(GroupLeave.TryParse(noSuccessor.AsSpan(0, length), out _));
        }

        Assert.False(GroupLeave.TryParse([.. noSuccessor, 0], out _));

        var withSuccessor = Convert.FromHexString(GroupVectors.LeaveAliceBody);
        for (var length = 0; length < withSuccessor.Length; length++)
        {
            if (length == GroupLeave.MinLength)
            {
                continue;
            }

            Assert.False(GroupLeave.TryParse(withSuccessor.AsSpan(0, length), out _));
        }

        Assert.False(GroupLeave.TryParse([.. withSuccessor, 0], out _));

        var badFlag = (byte[])noSuccessor.Clone();
        badFlag[GroupLeave.HasSuccessorOffset] = 2;
        Assert.False(GroupLeave.TryParse(badFlag, out _));

        var selfSuccessor = (byte[])withSuccessor.Clone();
        withSuccessor.AsSpan(GroupLeave.MemberOffset, Ed25519PublicKey.Length).CopyTo(selfSuccessor.AsSpan(GroupLeave.SuccessorOffset));
        Assert.False(GroupLeave.TryParse(selfSuccessor, out _));
    }

    [Fact]
    [Trait("Story", "US-14")]
    [Trait("NFR", "NFR-09")]
    public void TryParseAndDecode_AnySingleBitFlipInGroupRemoved_IsRefusedOrFailsSignature()
    {
        var original = Convert.FromHexString(GroupVectors.RemovedBody);
        for (var bit = 0; bit < original.Length * 8; bit++)
        {
            var changed = (byte[])original.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            if (GroupRemoved.TryParse(changed, out var parsed))
            {
                Assert.False(parsed.HasValidSignature());
                Assert.Equal(changed, parsed.ToBytes());
            }
        }

        var plaintext = Pad(Convert.FromHexString(GroupVectors.RemovedContent));
        for (var bit = 0; bit < plaintext.Length * 8; bit++)
        {
            var changed = (byte[])plaintext.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            if (MessageCodec.TryDecode(changed, out var decoded))
            {
                Assert.Equal(Convert.ToHexStringLower(changed), Convert.ToHexStringLower(MessageCodec.Encode(decoded)));
                var removed = Assert.IsType<GroupRemoved>(decoded);
                Assert.False(removed.HasValidSignature());
            }
        }
    }

    [Theory]
    [Trait("Story", "US-14")]
    [Trait("NFR", "NFR-09")]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(0x8000_0000u)]
    [InlineData(uint.MaxValue)]
    public void TryParse_GroupRemovedInvalidEpoch_IsRefused(uint epoch)
    {
        var bytes = Convert.FromHexString(GroupVectors.RemovedBody);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(GroupRemoved.EpochOffset), epoch);

        Assert.False(GroupRemoved.TryParse(bytes, out _));
    }

    [Fact]
    [Trait("Story", "US-14")]
    [Trait("NFR", "NFR-09")]
    public void GroupRemoved_SelfRemovalAndForgedSignature_AreRefused()
    {
        using var alice = TestKeys.Alice();
        using var bob = TestKeys.Bob();
        var groupId = GroupSamples.VectorGroupId();

        Assert.Throws<ArgumentException>(() => GroupRemoved.Create(groupId, 2, alice.IdentityKey, alice));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupRemoved.Create(groupId, 1, bob.IdentityKey, alice));

        var original = GroupRemoved.Create(groupId, 2, bob.IdentityKey, alice).ToBytes();
        var selfBytes = (byte[])original.Clone();
        alice.IdentityKey.Bytes.CopyTo(selfBytes.AsSpan(GroupRemoved.RemovedMemberOffset));
        Assert.False(GroupRemoved.TryParse(selfBytes, out _));

        using var carol = TestKeys.FromHex(TestKeys.Rfc8032Seed2, TestKeys.Rfc7748AlicePrivate, TestKeys.Rfc8032Seed1);
        var forgedBytes = (byte[])original.Clone();
        carol.IdentityKey.Bytes.CopyTo(forgedBytes.AsSpan(GroupRemoved.SignerOffset));
        Assert.True(GroupRemoved.TryParse(forgedBytes, out var forged));
        Assert.False(forged.HasValidSignature());
    }

    [Fact]
    [Trait("Story", "US-14")]
    public void GroupLeave_SelfSuccessorAndInvalidEpoch_Throw()
    {
        using var alice = TestKeys.Alice();
        var groupId = GroupSamples.VectorGroupId();

        Assert.Throws<ArgumentOutOfRangeException>(() => GroupLeave.Create(groupId, 0, alice));
        Assert.Throws<ArgumentException>(() => GroupLeave.Create(groupId, 1, alice, alice.IdentityKey));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupNotices.LeaveId(groupId, 0, alice.IdentityKey));
        Assert.Throws<ArgumentException>(() => GroupNotices.LeaveId(groupId, 1, alice.IdentityKey, alice.IdentityKey));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupNotices.RemovedId(groupId, 1, GroupSamples.Key(TestKeys.Rfc8032Public3), alice.IdentityKey));
        Assert.Throws<ArgumentException>(() => GroupNotices.RemovedId(groupId, 2, alice.IdentityKey, alice.IdentityKey));
    }

    private static string Content(byte[] plaintext)
    {
        var length = (plaintext[0] << 8) | plaintext[1];
        return Convert.ToHexStringLower(plaintext.AsSpan(2, length));
    }

    private static byte[] Pad(byte[] content)
    {
        var plaintext = new byte[MessageCodec.PaddedLengthFor(content.Length)];
        plaintext[0] = (byte)(content.Length >> 8);
        plaintext[1] = (byte)content.Length;
        content.CopyTo(plaintext, 2);
        return plaintext;
    }
}
