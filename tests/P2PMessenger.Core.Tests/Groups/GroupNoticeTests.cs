using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Tests.Groups;

public sealed class GroupNoticeTests
{
    [Fact]
    [Trait("Story", "US-12")]
    public void Encode_Invitation_GivesTheContentOfAnIndependentImplementation()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupSamples.VectorKey();
        var invitation = GroupInvitation.Create(key, GroupSamples.VectorRecord(alice, key));

        Assert.Equal(GroupVectors.InvitationContent, Content(MessageCodec.Encode(invitation)));
        Assert.Equal(GroupVectors.InvitationId, Convert.ToHexStringLower(invitation.NoticeId.Bytes));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public void Encode_Join_GivesTheContentOfAnIndependentImplementation()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupSamples.VectorKey();
        var record = GroupSamples.VectorRecord(alice, key);

        Assert.Equal(GroupVectors.JoinContent, Content(MessageCodec.Encode(GroupJoin.Create(record))));
        Assert.Equal(GroupVectors.JoinIdBob, Convert.ToHexStringLower(GroupNotices.JoinId(record.Hash, GroupSamples.Key(TestKeys.Rfc8032Public3)).Bytes));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public void Decode_Invitation_GivesTheRecordAndAKeyThatFitsIt()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupSamples.VectorKey();
        var invitation = GroupInvitation.Create(key, GroupSamples.VectorRecord(alice, key));

        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(invitation), out var decoded));
        var received = Assert.IsType<GroupInvitation>(decoded);
        using var opened = received.OpenKey();

        Assert.Equal(invitation, received);
        Assert.True(received.Record.Fits(opened));
    }

    [Fact]
    [Trait("Story", "US-12")]
    [Trait("NFR", "NFR-09")]
    public void Decode_InvitationWithAKeyThatDoesNotFitTheRecord_IsRefused()
    {
        var content = Convert.FromHexString(GroupVectors.InvitationContent);
        content[2] ^= 1;

        Assert.False(MessageCodec.TryDecode(Pad(content), out _));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public void Create_InvitationWithAKeyThatDoesNotFitTheRecord_Throws()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupSamples.VectorKey();
        using var other = GroupKey.Create();

        Assert.Throws<ArgumentException>(() => GroupInvitation.Create(other, GroupSamples.VectorRecord(alice, key)));
    }

    [Theory]
    [Trait("Story", "US-12")]
    [Trait("NFR", "NFR-09")]
    [InlineData(GroupVectors.InvitationContent)]
    [InlineData(GroupVectors.JoinContent)]
    public void Decode_AnySingleBitFlip_IsRefusedOrDecodesToExactlyTheChangedBytes(string contentHex)
    {
        var plaintext = Pad(Convert.FromHexString(contentHex));

        for (var bit = 0; bit < plaintext.Length * 8; bit++)
        {
            var changed = (byte[])plaintext.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            if (MessageCodec.TryDecode(changed, out var decoded))
            {
                Assert.Equal(Convert.ToHexStringLower(changed), Convert.ToHexStringLower(MessageCodec.Encode(decoded)));
                Assert.IsNotType<GroupInvitation>(decoded);
            }
        }
    }

    [Theory]
    [Trait("NFR", "NFR-09")]
    [InlineData(51)]
    [InlineData(53)]
    public void Decode_JoinOfAnotherLength_IsRefused(int bodyLength)
    {
        byte[] content = [(byte)SessionMessageType.GroupJoin, MessageCodec.CurrentVersion, .. new byte[bodyLength]];
        content[2 + 19] = 1;

        Assert.False(MessageCodec.TryDecode(Pad(content), out _));
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void Decode_JoinWithEpochZero_IsRefused()
    {
        var content = Convert.FromHexString(GroupVectors.JoinContent);
        content[2 + 19] = 0;

        Assert.False(MessageCodec.TryDecode(Pad(content), out _));
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
