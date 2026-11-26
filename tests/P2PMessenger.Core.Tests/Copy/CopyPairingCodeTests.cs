using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Core.Tests.Copy;

public sealed class CopyPairingCodeTests
{
    [Fact]
    [Trait("Story", "US-22")]
    public void PairingCode_IndependentVector_MatchesBinaryAndTextByteForByte()
    {
        Assert.True(X25519PublicKey.TryCreate(Convert.FromHexString(CopyVectors.PcPublicKey), out var key));
        Assert.True(Ipv4Address.TryCreate(192, 168, 1, 99, out var second));
        Assert.True(Ipv4Address.TryCreate(192, 168, 1, 42, out var first));

        var created = CopyPairingCode.Create(key, Convert.FromHexString(CopyVectors.Psk), CopyVectors.Port, [second, first, first]);

        Assert.Equal(CopyVectors.PairingBytes, Convert.ToHexStringLower(created.ToBytes()));
        Assert.Equal(CopyVectors.PairingText, created.ToText());
        Assert.Equal([first, second], created.Addresses);

        Assert.True(CopyPairingCode.TryParseText(CopyVectors.PairingText, out var parsed, out var error));
        Assert.Equal(CopyPairingCodeError.None, error);
        Assert.Equal(created, parsed);
        Assert.Equal(CopyVectors.Port, parsed.Port);
        Assert.Equal(key, parsed.ReceiverPublicKey);
        Assert.Equal(Convert.FromHexString(CopyVectors.Psk), parsed.PairingSecret.ToArray());
    }

    [Fact]
    [Trait("Story", "US-22")]
    public void TryParseText_LowercaseWithSpacesAndDashes_ParsesTheSamePairingCode()
    {
        var decorated = "  p2pmc1-" + string.Join(" - ", CopyVectors.PairingText[7..].Chunk(8).Select(static chunk => new string(chunk))) + "\r\n";

        Assert.True(CopyPairingCode.TryParseText(decorated, out var parsed, out var error));
        Assert.Equal(CopyPairingCodeError.None, error);
        Assert.Equal(CopyVectors.PairingText, parsed.ToText());
    }

    [Theory]
    [Trait("Story", "US-22")]
    [InlineData(null, CopyPairingCodeError.NotAPairingCode)]
    [InlineData("", CopyPairingCodeError.NotAPairingCode)]
    [InlineData("P2PM1-AAAA", CopyPairingCodeError.NotAPairingCode)]
    [InlineData("https://example.org", CopyPairingCodeError.NotAPairingCode)]
    [InlineData("P2PMC2-AAAA", CopyPairingCodeError.UnsupportedVersion)]
    [InlineData("P2PMC1-!!!!", CopyPairingCodeError.Damaged)]
    [InlineData("P2PMC1-AAAA", CopyPairingCodeError.Damaged)]
    [InlineData("P2PMC1-AEAAAAAA", CopyPairingCodeError.Damaged)]
    public void TryParseText_InvalidInputs_ReturnExpectedError(string? input, CopyPairingCodeError expected)
    {
        Assert.False(CopyPairingCode.TryParseText(input, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.Equal(expected, error);
    }

    [Fact]
    [Trait("Story", "US-22")]
    public void TryParse_InvalidVersionAddressesPortOrSecrets_AreRefused()
    {
        var valid = Convert.FromHexString(CopyVectors.PairingBytes);

        var wrongVersion = (byte[])valid.Clone();
        wrongVersion[0] = 2;
        Assert.False(CopyPairingCode.TryParse(wrongVersion, out _, out var versionError));
        Assert.Equal(CopyPairingCodeError.UnsupportedVersion, versionError);

        var zeroPort = (byte[])valid.Clone();
        zeroPort[CopyPairingCode.PortOffset] = 0;
        zeroPort[CopyPairingCode.PortOffset + 1] = 0;
        Assert.False(CopyPairingCode.TryParse(zeroPort, out _, out _));

        var zeroSecret = (byte[])valid.Clone();
        Array.Clear(zeroSecret, CopyPairingCode.SecretOffset, CopyPairingCode.SecretLength);
        Assert.False(CopyPairingCode.TryParse(zeroSecret, out _, out _));

        var zeroKey = (byte[])valid.Clone();
        Array.Clear(zeroKey, CopyPairingCode.ReceiverKeyOffset, X25519PublicKey.Length);
        Assert.False(CopyPairingCode.TryParse(zeroKey, out _, out _));

        var unsorted = (byte[])valid.Clone();
        var firstAddress = unsorted[CopyPairingCode.AddressesOffset..(CopyPairingCode.AddressesOffset + 4)];
        var secondAddress = unsorted[(CopyPairingCode.AddressesOffset + 4)..(CopyPairingCode.AddressesOffset + 8)];
        secondAddress.CopyTo(unsorted, CopyPairingCode.AddressesOffset);
        firstAddress.CopyTo(unsorted, CopyPairingCode.AddressesOffset + 4);
        Assert.False(CopyPairingCode.TryParse(unsorted, out _, out _));

        var duplicate = (byte[])valid.Clone();
        firstAddress.CopyTo(duplicate, CopyPairingCode.AddressesOffset + 4);
        Assert.False(CopyPairingCode.TryParse(duplicate, out _, out _));

        var multicast = (byte[])valid.Clone();
        multicast[CopyPairingCode.AddressesOffset] = 224;
        Assert.False(CopyPairingCode.TryParse(multicast, out _, out _));
    }

    [Theory]
    [Trait("Story", "US-22")]
    [InlineData(0, 0, 0, 1, false)]
    [InlineData(127, 0, 0, 1, true)]
    [InlineData(192, 168, 1, 10, true)]
    [InlineData(223, 255, 255, 254, true)]
    [InlineData(224, 0, 0, 1, false)]
    [InlineData(255, 255, 255, 255, false)]
    public void Ipv4Address_DialableRange_EnforcesUnicastOctets(byte a, byte b, byte c, byte d, bool expected)
    {
        Assert.Equal(expected, Ipv4Address.TryCreate(a, b, c, d, out _));
    }

    [Fact]
    [Trait("Story", "US-22")]
    public void Create_ZeroOrNineAddresses_Throws()
    {
        using var keyPair = X25519KeyPair.Create();
        var secret = Convert.FromHexString(CopyVectors.Psk);

        Assert.Throws<ArgumentException>(() => CopyPairingCode.Create(keyPair.PublicKey, secret, 50000, []));

        Ipv4Address[] nine = [.. Enumerable.Range(1, 9).Select(index =>
        {
            Assert.True(Ipv4Address.TryCreate(10, 0, 0, (byte)index, out var address));
            return address;
        })];
        Assert.Throws<ArgumentException>(() => CopyPairingCode.Create(keyPair.PublicKey, secret, 50000, nine));
    }
}
