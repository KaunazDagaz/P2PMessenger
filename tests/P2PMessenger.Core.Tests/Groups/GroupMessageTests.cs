using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Tests.Groups;

public sealed class GroupMessageTests
{
    [Fact]
    [Trait("Story", "US-12")]
    public void Seal_VectorInputs_GivesTheEnvelopeOfAnIndependentImplementation()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupSamples.VectorKey();

        var message = GroupSamples.VectorMessage(alice, key);

        Assert.Equal(GroupVectors.MessageEnvelope, Convert.ToHexStringLower(message.ToBytes()));
        Assert.Equal(GroupVectors.MessageContent, Content(MessageCodec.Encode(message)));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public void TryOpen_VectorEnvelope_GivesTheAuthorAndTheText()
    {
        using var key = GroupSamples.VectorKey();

        Assert.True(GroupMessage.TryParse(Convert.FromHexString(GroupVectors.MessageEnvelope), out var message));
        Assert.True(message.TryOpen(key, out var text));

        Assert.Equal(GroupVectors.GroupId, Convert.ToHexStringLower(message.GroupId.Bytes));
        Assert.Equal(1, message.Epoch);
        Assert.Equal(TestKeys.Rfc8032Public1, Convert.ToHexStringLower(message.AuthorKey.Bytes));
        Assert.Equal(GroupSamples.VectorText(), text);
    }

    [Fact]
    [Trait("Story", "US-13")]
    public void HasValidSignature_IsCheckedWithoutTheKey()
    {
        Assert.True(GroupMessage.TryParse(Convert.FromHexString(GroupVectors.MessageEnvelope), out var message));

        Assert.True(message.HasValidSignature());
    }

    [Fact]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-09")]
    public void TryOpen_AnotherKey_IsRefused()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupSamples.VectorKey();
        using var other = GroupKey.Create();

        Assert.False(GroupSamples.VectorMessage(alice, key).TryOpen(other, out var text));
        Assert.Null(text);
    }

    [Fact]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-09")]
    public void TryOpen_AnySingleBitFlipOfTheEnvelope_IsRefused()
    {
        using var key = GroupSamples.VectorKey();
        var envelope = Convert.FromHexString(GroupVectors.MessageEnvelope);

        for (var bit = 0; bit < envelope.Length * 8; bit++)
        {
            var changed = (byte[])envelope.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            Assert.False(GroupMessage.TryParse(changed, out var message) && message.TryOpen(key, out _), $"bit {bit} of {envelope.Length * 8}");
        }
    }

    [Fact]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-09")]
    public void HasValidSignature_AnySingleBitFlipOfTheEnvelope_IsFalse()
    {
        var envelope = Convert.FromHexString(GroupVectors.MessageEnvelope);

        for (var bit = 0; bit < envelope.Length * 8; bit++)
        {
            var changed = (byte[])envelope.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            Assert.False(GroupMessage.TryParse(changed, out var message) && message.HasValidSignature(), $"bit {bit} of {envelope.Length * 8}");
        }
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void TryOpen_EnvelopeReSignedByAnotherMemberUnderTheirOwnName_IsRefused()
    {
        using var bob = TestKeys.Bob();
        using var key = GroupSamples.VectorKey();
        var envelope = Convert.FromHexString(GroupVectors.MessageEnvelope);
        bob.IdentityKey.Bytes.CopyTo(envelope.AsSpan(GroupMessage.AuthorOffset));

        Assert.True(GroupMessage.TryParse(envelope, out var message));
        Assert.False(message.TryOpen(key, out _));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public void Seal_TheLongestText_FitsTheLargestSizeClass()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupKey.Create();
        var text = GroupSamples.Text(7, new string('x', GroupText.MaxUtf8Length));

        var plaintext = MessageCodec.Encode(GroupMessage.Seal(key, GroupId.Create(), 1, alice, text));

        Assert.Equal(16_192, GroupText.MaxUtf8Length);
        Assert.Equal(MessageCodec.MaxPaddedLength, plaintext.Length);
        Assert.True(MessageCodec.TryDecode(plaintext, out var decoded));
        Assert.True(Assert.IsType<GroupMessage>(decoded).TryOpen(key, out var opened));
        Assert.Equal(text, opened);
    }

    [Fact]
    [Trait("Story", "US-12")]
    public void TryCreate_TextLongerThanTheGroupLimit_IsRefused()
    {
        Assert.False(GroupText.TryCreate(MessageId.Create(), 1, DateTimeOffset.UnixEpoch, new string('x', GroupText.MaxUtf8Length + 1), out _));
        Assert.False(GroupText.TryCreate(MessageId.Create(), 1, DateTimeOffset.UnixEpoch, string.Empty, out _));
        Assert.False(GroupText.TryCreate(MessageId.Create(), 0, DateTimeOffset.UnixEpoch, "x", out _));
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void Decode_AnySingleBitFlipOfThePlaintext_IsRefusedOrDecodesToExactlyTheChangedBytes()
    {
        using var alice = TestKeys.Alice();
        using var key = GroupSamples.VectorKey();
        var plaintext = MessageCodec.Encode(GroupSamples.VectorMessage(alice, key));

        for (var bit = 0; bit < plaintext.Length * 8; bit++)
        {
            var changed = (byte[])plaintext.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            if (MessageCodec.TryDecode(changed, out var decoded))
            {
                Assert.Equal(Convert.ToHexStringLower(changed), Convert.ToHexStringLower(MessageCodec.Encode(decoded)));
                Assert.False(decoded is GroupMessage groupMessage && groupMessage.TryOpen(key, out _), $"bit {bit}");
            }
        }
    }

    private static string Content(byte[] plaintext)
    {
        var length = (plaintext[0] << 8) | plaintext[1];
        return Convert.ToHexStringLower(plaintext.AsSpan(2, length));
    }
}
