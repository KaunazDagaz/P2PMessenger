using System.Text;
using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Core.Tests.Crypto;

public sealed class DigestsTests
{
    [Theory]
    [InlineData("", "a7ffc6f8bf1ed76651c14756a061d662f580ff4de43b49fa82d80a4b80f8434a")]
    [InlineData("abc", "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532")]
    [InlineData("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", "41c0dba2a9d6240849100376a8235e2c82e1b9998a999e21db32dd97496d3376")]
    [InlineData("abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu", "916f6061fe879741ca6469b43971dfdb28b1a32dc36cb3254e812be27aad1d18")]
    public void Sha3256_Fips202Vectors_MatchTheStandard(string message, string expected)
    {
        var digest = Digests.Sha3256(Encoding.ASCII.GetBytes(message));

        Assert.Equal(expected, Convert.ToHexStringLower(digest));
    }

    [Fact]
    public void Sha3256_OneMillionLowercaseA_MatchesTheStandard()
    {
        var digest = Digests.Sha3256(Encoding.ASCII.GetBytes(new string('a', 1_000_000)));

        Assert.Equal("5c8875ae474a3634ba4fd55ec85bffd661f32aca75c6d699d0cdcb6c115891c1", Convert.ToHexStringLower(digest));
    }

    [Theory]
    [InlineData("", "0e5751c026e543b2e8ab2eb06099daa1d1e5df47778f7787faab45cdf12fe3a8")]
    [InlineData("abc", "bddd813c634239723171ef3fee98579b94964e3bb1cb3e427262c8c068d52319")]
    public void Blake2b256_ReferenceVectors_MatchTheStandard(string message, string expected)
    {
        var digest = Digests.Blake2b256(Encoding.ASCII.GetBytes(message));

        Assert.Equal(expected, Convert.ToHexStringLower(digest));
    }
}
