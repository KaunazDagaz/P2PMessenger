using System.Buffers.Binary;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Tests.Groups;

public sealed class GroupAcknowledgementTests
{
    [Fact]
    [Trait("Story", "US-17")]
    public void Encode_CarolAndBobAcknowledgements_MatchIndependentPythonVectorsByteForByte()
    {
        using var carol = TestKeys.FromHex(TestKeys.Rfc8032Seed2, TestKeys.Rfc7748AlicePrivate, TestKeys.Rfc8032Seed1);
        using var bob = TestKeys.Bob();
        var groupId = GroupSamples.VectorGroupId();
        var messageId = GroupSamples.VectorText().Id;

        var carolAck = GroupAcknowledgement.Create(groupId, 1, messageId, carol);
        var bobAck = GroupAcknowledgement.Create(groupId, 1, messageId, bob);
        var carolPlaintext = MessageCodec.Encode(carolAck);
        var bobPlaintext = MessageCodec.Encode(bobAck);

        Assert.Equal(GroupAcknowledgement.Length, carolAck.ToBytes().Length);
        Assert.Equal(MessageCodec.MinPaddedLength, carolPlaintext.Length);
        Assert.Equal(MessageCodec.MinPaddedLength, bobPlaintext.Length);
        Assert.Equal(GroupVectors.GroupAckCarolBody, Convert.ToHexStringLower(carolAck.ToBytes()));
        Assert.Equal(GroupVectors.GroupAckCarolContent, Content(carolPlaintext));
        Assert.Equal(GroupVectors.GroupAckCarolNoticeId, Convert.ToHexStringLower(carolAck.NoticeId.Bytes));
        Assert.Equal(GroupVectors.GroupAckBobContent, Content(bobPlaintext));
        Assert.Equal(GroupVectors.GroupAckBobNoticeId, Convert.ToHexStringLower(bobAck.NoticeId.Bytes));
        Assert.True(carolAck.HasValidSignature());
        Assert.True(bobAck.HasValidSignature());
    }

    [Fact]
    [Trait("Story", "US-17")]
    public void Decode_VectorAcknowledgement_RoundTripsAllFieldsAndEquality()
    {
        var plaintext = Pad(Convert.FromHexString(GroupVectors.GroupAckCarolContent));

        Assert.True(MessageCodec.TryDecode(plaintext, out var decoded));
        var ack = Assert.IsType<GroupAcknowledgement>(decoded);
        Assert.True(GroupAcknowledgement.TryParse(Convert.FromHexString(GroupVectors.GroupAckCarolBody), out var parsed));

        Assert.Equal(GroupSamples.VectorGroupId(), ack.GroupId);
        Assert.Equal(1, ack.Epoch);
        Assert.Equal(GroupSamples.VectorText().Id, ack.MessageId);
        Assert.Equal(GroupSamples.Key(TestKeys.Rfc8032Public2), ack.RecipientKey);
        Assert.Equal(GroupVectors.GroupAckCarolNoticeId, Convert.ToHexStringLower(ack.NoticeId.Bytes));
        Assert.True(ack.HasValidSignature());
        Assert.Equal(ack, parsed);
        Assert.Equal(ack.GetHashCode(), parsed.GetHashCode());
        Assert.Equal(plaintext, MessageCodec.Encode(ack));
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-09")]
    public void TryParse_AnySingleBitFlipInBody_IsRefusedOrFailsSignatureVerification()
    {
        var original = Convert.FromHexString(GroupVectors.GroupAckCarolBody);

        for (var bit = 0; bit < original.Length * 8; bit++)
        {
            var changed = (byte[])original.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            if (GroupAcknowledgement.TryParse(changed, out var parsed))
            {
                Assert.False(parsed.HasValidSignature());
                Assert.Equal(changed, parsed.ToBytes());
            }
        }
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-09")]
    public void Decode_AnySingleBitFlipInPaddedPlaintext_IsRefusedOrFailsSignatureVerification()
    {
        var plaintext = Pad(Convert.FromHexString(GroupVectors.GroupAckCarolContent));

        for (var bit = 0; bit < plaintext.Length * 8; bit++)
        {
            var changed = (byte[])plaintext.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            if (MessageCodec.TryDecode(changed, out var decoded))
            {
                Assert.Equal(Convert.ToHexStringLower(changed), Convert.ToHexStringLower(MessageCodec.Encode(decoded)));
                var ack = Assert.IsType<GroupAcknowledgement>(decoded);
                Assert.False(ack.HasValidSignature());
            }
        }
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-09")]
    public void TryParse_EveryTruncationAndExtension_IsRefused()
    {
        var original = Convert.FromHexString(GroupVectors.GroupAckCarolBody);

        for (var length = 0; length < original.Length; length++)
        {
            Assert.False(GroupAcknowledgement.TryParse(original.AsSpan(0, length), out _));
            byte[] content = [(byte)SessionMessageType.GroupAcknowledgement, MessageCodec.CurrentVersion, .. original.AsSpan(0, length)];
            Assert.False(MessageCodec.TryDecode(Pad(content), out _));
        }

        foreach (var extra in new[] { 1, 2, 64 })
        {
            byte[] extended = [.. original, .. new byte[extra]];
            Assert.False(GroupAcknowledgement.TryParse(extended, out _));
            byte[] content = [(byte)SessionMessageType.GroupAcknowledgement, MessageCodec.CurrentVersion, .. extended];
            Assert.False(MessageCodec.TryDecode(Pad(content), out _));
        }
    }

    [Theory]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-09")]
    [InlineData(0u)]
    [InlineData(0x8000_0000u)]
    [InlineData(uint.MaxValue)]
    public void TryParse_InvalidEpoch_IsRefused(uint epoch)
    {
        var bytes = Convert.FromHexString(GroupVectors.GroupAckCarolBody);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(GroupAcknowledgement.EpochOffset), epoch);

        Assert.False(GroupAcknowledgement.TryParse(bytes, out _));
        byte[] content = [(byte)SessionMessageType.GroupAcknowledgement, MessageCodec.CurrentVersion, .. bytes];
        Assert.False(MessageCodec.TryDecode(Pad(content), out _));
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-09")]
    public void HasValidSignature_ForgedUnderAnotherMembersKey_IsFalse()
    {
        using var bob = TestKeys.Bob();
        using var carol = TestKeys.FromHex(TestKeys.Rfc8032Seed2, TestKeys.Rfc7748AlicePrivate, TestKeys.Rfc8032Seed1);
        var bobAck = GroupAcknowledgement.Create(GroupSamples.VectorGroupId(), 1, GroupSamples.VectorText().Id, bob);
        var forgedBytes = bobAck.ToBytes();
        carol.IdentityKey.Bytes.CopyTo(forgedBytes.AsSpan(GroupAcknowledgement.RecipientOffset));

        Assert.True(GroupAcknowledgement.TryParse(forgedBytes, out var forged));
        Assert.Equal(carol.IdentityKey, forged.RecipientKey);
        Assert.False(forged.HasValidSignature());
    }

    [Fact]
    [Trait("Story", "US-17")]
    public void CreateAndNoticeId_InvalidArguments_Throw()
    {
        using var bob = TestKeys.Bob();
        var groupId = GroupId.Create();
        var messageId = MessageId.Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => GroupAcknowledgement.Create(groupId, 0, messageId, bob));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupAcknowledgement.Create(groupId, -1, messageId, bob));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupNotices.AcknowledgementId(groupId, 0, messageId, bob.IdentityKey));
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
