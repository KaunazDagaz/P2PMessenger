using System.Buffers.Binary;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Tests.Sessions;

namespace P2PMessenger.Core.Tests.Messages;

public sealed class MessageCodecTests
{
    public static TheoryData<string> Samples
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in new[] { "keep-alive", "acknowledgement", "text", "long text" })
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Fact]
    [Trait("Story", "US-11")]
    public void Encode_KeepAlive_GivesTheContentOfAnIndependentImplementation()
    {
        AssertContent(SessionVectors.KeepAliveContent, 256, MessageCodec.Encode(KeepAlive.Instance));
    }

    [Fact]
    [Trait("Story", "US-11")]
    public void Encode_Acknowledgement_GivesTheContentOfAnIndependentImplementation()
    {
        AssertContent(SessionVectors.AcknowledgementContent, 256, MessageCodec.Encode(Acknowledgement.Create(SessionVectorTests.VectorId())));
    }

    [Fact]
    [Trait("Story", "US-11")]
    public void Encode_Text_GivesTheContentOfAnIndependentImplementation()
    {
        AssertContent(SessionVectors.TextContent, 256, MessageCodec.Encode(SessionVectorTests.VectorText()));
    }

    [Fact]
    [Trait("Story", "US-11")]
    public void Encode_LongText_GivesTheContentOfAnIndependentImplementationInTheNextSizeClass()
    {
        AssertContent(SessionVectors.LongTextContent, 1024, MessageCodec.Encode(LongText()));
    }

    [Theory]
    [Trait("Story", "US-11")]
    [MemberData(nameof(Samples))]
    public void Decode_EncodedSample_GivesTheSameMessage(string sample)
    {
        var message = Sample(sample);

        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(message), out var decoded));

        Assert.Equal(message, decoded);
    }

    [Theory]
    [Trait("NFR", "NFR-09")]
    [MemberData(nameof(Samples))]
    public void Decode_AnySingleBitFlip_IsRefusedOrDecodesToAMessageWhoseEncodingIsExactlyTheChangedBytes(string sample)
    {
        var plaintext = MessageCodec.Encode(Sample(sample));

        for (var bit = 0; bit < plaintext.Length * 8; bit++)
        {
            var changed = (byte[])plaintext.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            if (MessageCodec.TryDecode(changed, out var decoded))
            {
                Assert.Equal(Convert.ToHexStringLower(changed), Convert.ToHexStringLower(MessageCodec.Encode(decoded)));
            }
        }
    }

    [Theory]
    [Trait("NFR", "NFR-09")]
    [InlineData(0)]
    [InlineData(255)]
    [InlineData(257)]
    [InlineData(512)]
    [InlineData(1023)]
    [InlineData(16385)]
    public void Decode_PlaintextOfAnotherLength_IsRefused(int length)
    {
        var plaintext = new byte[length];
        if (length >= 4)
        {
            plaintext[1] = 2;
            plaintext[2] = (byte)SessionMessageType.KeepAlive;
            plaintext[3] = MessageCodec.CurrentVersion;
        }

        Assert.False(MessageCodec.TryDecode(plaintext, out _));
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void Decode_ContentInALargerSizeClassThanNeeded_IsRefused()
    {
        var plaintext = new byte[1024];
        MessageCodec.Encode(KeepAlive.Instance).CopyTo(plaintext, 0);

        Assert.False(MessageCodec.TryDecode(plaintext, out _));
    }

    [Theory]
    [Trait("NFR", "NFR-09")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(ushort.MaxValue)]
    public void Decode_ContentLengthOutOfRange_IsRefused(int contentLength)
    {
        var plaintext = MessageCodec.Encode(KeepAlive.Instance);
        BinaryPrimitives.WriteUInt16BigEndian(plaintext, (ushort)contentLength);

        Assert.False(MessageCodec.TryDecode(plaintext, out _));
    }

    [Theory]
    [Trait("NFR", "NFR-09")]
    [InlineData(4)]
    [InlineData(100)]
    [InlineData(255)]
    public void Decode_NonZeroPadding_IsRefused(int position)
    {
        var plaintext = MessageCodec.Encode(KeepAlive.Instance);
        plaintext[position] = 1;

        Assert.False(MessageCodec.TryDecode(plaintext, out _));
    }

    [Theory]
    [Trait("NFR", "NFR-09")]
    [InlineData(0, 1)]
    [InlineData(4, 1)]
    [InlineData(255, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    public void Decode_UnknownTypeOrVersion_IsRefused(int type, int version)
    {
        var plaintext = MessageCodec.Encode(KeepAlive.Instance);
        plaintext[2] = (byte)type;
        plaintext[3] = (byte)version;

        Assert.False(MessageCodec.TryDecode(plaintext, out _));
    }

    [Theory]
    [Trait("NFR", "NFR-09")]
    [InlineData(15)]
    [InlineData(17)]
    public void Decode_AcknowledgementOfAnotherLength_IsRefused(int bodyLength)
    {
        Assert.False(MessageCodec.TryDecode(Plaintext(SessionMessageType.Acknowledgement, new byte[bodyLength]), out _));
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void Decode_KeepAliveWithABody_IsRefused()
    {
        Assert.False(MessageCodec.TryDecode(Plaintext(SessionMessageType.KeepAlive, [0]), out _));
    }

    [Theory]
    [Trait("NFR", "NFR-09")]
    [InlineData("0000000000000000", "0000000000000001", "41")]
    [InlineData("8000000000000001", "0000000000000001", "41")]
    [InlineData("0000000000000001", "ffffffffffffffff", "41")]
    [InlineData("0000000000000001", "0000e677d21fdc00", "41")]
    [InlineData("0000000000000001", "0000000000000001", "")]
    [InlineData("0000000000000001", "0000000000000001", "c080")]
    [InlineData("0000000000000001", "0000000000000001", "80")]
    [InlineData("0000000000000001", "0000000000000001", "eda080")]
    [InlineData("0000000000000001", "0000000000000001", "f4908080")]
    [InlineData("0000000000000001", "0000000000000001", "41c3")]
    public void Decode_TextWithAnInvalidField_IsRefused(string sequenceHex, string writtenAtHex, string textHex)
    {
        byte[] body = [.. Convert.FromHexString(SessionVectors.MessageId), .. Convert.FromHexString(sequenceHex), .. Convert.FromHexString(writtenAtHex), .. Convert.FromHexString(textHex)];

        Assert.False(MessageCodec.TryDecode(Plaintext(SessionMessageType.Text, body), out _));
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void Decode_TextWithAValidField_IsAccepted()
    {
        byte[] body = [.. Convert.FromHexString(SessionVectors.MessageId), .. Convert.FromHexString("7fffffffffffffff"), .. Convert.FromHexString("0000e677d21fdbff"), .. "A"u8];

        Assert.True(MessageCodec.TryDecode(Plaintext(SessionMessageType.Text, body), out var message));

        var text = Assert.IsType<TextMessage>(message);
        Assert.Equal(long.MaxValue, text.Sequence);
        Assert.Equal(DateTimeOffset.MaxValue.ToUnixTimeMilliseconds(), text.WrittenAt.ToUnixTimeMilliseconds());
    }

    internal static SessionMessage Sample(string name) => name switch
    {
        "keep-alive" => KeepAlive.Instance,
        "acknowledgement" => Acknowledgement.Create(SessionVectorTests.VectorId()),
        "text" => SessionVectorTests.VectorText(),
        "long text" => LongText(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static TextMessage LongText()
    {
        if (!MessageId.TryCreate(Convert.FromHexString("ffeeddccbbaa99887766554433221100"), out var id)
            || !TextMessage.TryCreate(id, 42, DateTimeOffset.UnixEpoch, new string('é', 150) + new string('x', 50), out var message))
        {
            throw new InvalidOperationException("The long test message was not accepted.");
        }

        return message;
    }

    private static byte[] Plaintext(SessionMessageType type, byte[] body)
    {
        var contentLength = MessageCodec.TypeHeaderLength + body.Length;
        var plaintext = new byte[MessageCodec.PaddedLengthFor(contentLength)];
        BinaryPrimitives.WriteUInt16BigEndian(plaintext, (ushort)contentLength);
        plaintext[2] = (byte)type;
        plaintext[3] = MessageCodec.CurrentVersion;
        body.CopyTo(plaintext, 4);
        return plaintext;
    }

    private static void AssertContent(string expectedContentHex, int expectedLength, byte[] plaintext)
    {
        var content = Convert.FromHexString(expectedContentHex);

        Assert.Equal(expectedLength, plaintext.Length);
        Assert.Equal(content.Length, BinaryPrimitives.ReadUInt16BigEndian(plaintext));
        Assert.Equal(expectedContentHex, Convert.ToHexStringLower(plaintext.AsSpan(MessageCodec.LengthPrefixLength, content.Length)));
        Assert.All(plaintext[(MessageCodec.LengthPrefixLength + content.Length)..], value => Assert.Equal((byte)0, value));
    }
}
