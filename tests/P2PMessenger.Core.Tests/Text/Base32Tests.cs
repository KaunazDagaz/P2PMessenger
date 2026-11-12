using P2PMessenger.Core.Text;

namespace P2PMessenger.Core.Tests.Text;

public sealed class Base32Tests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Encode_Rfc4648Vectors_MatchTheStandardWithoutPadding(string text, string expected)
    {
        var data = System.Text.Encoding.ASCII.GetBytes(text);

        Assert.Equal(expected, Base32.Encode(data, lowerCase: false));
        Assert.Equal(expected.ToLowerInvariant(), Base32.Encode(data, lowerCase: true));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("MY", "f")]
    [InlineData("MZXQ", "fo")]
    [InlineData("MZXW6", "foo")]
    [InlineData("MZXW6YQ", "foob")]
    [InlineData("MZXW6YTB", "fooba")]
    [InlineData("MZXW6YTBOI", "foobar")]
    [InlineData("mzxw6ytboi", "foobar")]
    [InlineData("MzXw6yTbOi", "foobar")]
    public void TryDecode_Rfc4648Vectors_GiveTheOriginalBytes(string encoded, string expected)
    {
        var buffer = new byte[16];

        Assert.True(Base32.TryDecode(encoded, buffer, out var written));
        Assert.Equal(expected, System.Text.Encoding.ASCII.GetString(buffer, 0, written));
    }

    [Theory]
    [InlineData("MZ")]
    [InlineData("MZXW7")]
    [InlineData("MZXW6YTBOJ")]
    public void TryDecode_NonZeroPaddingBits_IsRejected(string encoded)
    {
        Assert.False(Base32.TryDecode(encoded, new byte[16], out _));
    }

    [Theory]
    [InlineData("M")]
    [InlineData("MZX")]
    [InlineData("MZXW6Y")]
    [InlineData("MZXW6YTBOIA")]
    public void TryDecode_LengthThatNoByteCountProduces_IsRejected(string encoded)
    {
        Assert.False(Base32.TryDecode(encoded, new byte[16], out _));
    }

    [Theory]
    [InlineData("MY======")]
    [InlineData("M0")]
    [InlineData("M1")]
    [InlineData("M8")]
    [InlineData("M9")]
    [InlineData("M=")]
    [InlineData("M ")]
    [InlineData("M-")]
    [InlineData("M\n")]
    [InlineData("MÝ")]
    public void TryDecode_CharacterOutsideTheAlphabet_IsRejected(string encoded)
    {
        Assert.False(Base32.TryDecode(encoded, new byte[16], out _));
    }

    [Fact]
    public void TryDecode_DestinationTooSmall_IsRejected()
    {
        Assert.False(Base32.TryDecode("MZXW6YTBOI", new byte[5], out _));
        Assert.True(Base32.TryDecode("MZXW6YTBOI", new byte[6], out _));
    }

    [Fact]
    public void Encode_ThenDecode_RoundTripsEveryLengthFromZeroTo100()
    {
        var random = new Random(8);

        for (var length = 0; length <= 100; length++)
        {
            var data = new byte[length];
            random.NextBytes(data);
            var buffer = new byte[length];

            foreach (var lowerCase in new[] { false, true })
            {
                var encoded = Base32.Encode(data, lowerCase);

                Assert.Equal(Base32.GetEncodedLength(length), encoded.Length);
                Assert.True(Base32.TryDecode(encoded, buffer, out var written));
                Assert.Equal(data, buffer[..written]);
            }
        }
    }
}
