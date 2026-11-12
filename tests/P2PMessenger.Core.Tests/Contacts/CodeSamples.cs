using System.Text;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Core.Tests.Contacts;

internal static class CodeSamples
{
    public const string Alice = "alice";
    public const string Bob = "bob";
    public const string Cyrillic = "cyrillic";
    public const string Longest = "longest";

    public static IReadOnlyList<string> All { get; } = [Alice, Bob, Cyrillic, Longest];

    public static TheoryData<string> Names
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in All)
            {
                data.Add(name);
            }

            return data;
        }
    }

    public static string Hex(string sample) => sample switch
    {
        Alice => "01d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c26630305416c6963655daee82b16429dd5c2ec82935bc454d39f89ab817aaadf2e41f121e0d2a584b0e27223c177c3cf3fe3dc28625bbc026b129614382e3863927d6c4d66cac9d60a",
        Bob => "01fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4fec172b93ad5e563bf4932c70e1245034c35467ef2efd4d64ebf819683467e2bf9bfc0303426f62c8b56d2b5e039702adfa2613a0b2a4670285374df5de411ba00473bd2dc5e687f2548e6ae2e00f972110029207fe4a5148707b4d16404c3559087d7b118b9d0b",
        Cyrillic => "01d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c2663030cd09cd0b8d0bad0b8d182d0b02c7c2b8003068273ca6311ae90a035e1fa367fa62e3d814a0deaa05a19ca6ee940991b73096aa944678c9e2e7a4d61e0bdce332b5e6727605af7fcf63a10fa0f",
        Longest => "01fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4fec172b93ad5e563bf4932c70e1245034c35467ef2efd4d64ebf819683467e2bf9bfc034078787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878c52dc3470af7db6f703b5bcaaf969cd69f981e7b02c2b6b989216782c57dbd9d6ca329dd7f18a5daee6cabbe1cb07f597d649790ac308e7240dbaafdc8a9790f",
        _ => throw new ArgumentOutOfRangeException(nameof(sample)),
    };

    public static string Text(string sample) => sample switch
    {
        Alice => "P2PM1-AHLVVGABQKYQVN6VJP7NHSLEA45A5YLS6PNKMIZFV4BBU2HXA5IRVBJA6AEYSMFHKR2IW7O4WQ7POWQNX45A2JRYDL2OXJFJR2VJWTTKHVABPQ7IIOEVVEVXBKTU2G36XSOJQLGPF3CJNDGAZVK7CKXUMYGCMYYDAVAWY2LDMVO252BLCZBJ3VOC5SBJGW6EKTJZ7CNLQF5KVXZOIHYSDYGSUWCLBYTSEPAXPQ6PH7R5YKDCLO6AE2YSSYKDQLRYMOJH23CNM3FMTVQK",
        Bob => "P2PM1-AH6FDTMOMIMKDI4NUR7NAARQ6BMAQFXNCO5DGA5MLXVZCFKISCACLXU63N6XW7OBWTJVWYOC5TSDKNZ7QNB4QW3YM5G237D6CRXYQK2P5QLSXE5NLZLDX5ETFRYOCJCQGTBVIZ7PF36U2ZHL7AMWQNDH4K7ZX7ADANBG6YWIWVWSWXQDS4BK36RGCOQLFJDHAKCTOTPV3ZARXIAEOO6S3RPGQ7ZFJDTK4LQA7FZBCABJEB76JJIUQ4D3JULEATBVLEEH26YRROOQW",
        Cyrillic => "P2PM1-AHLVVGABQKYQVN6VJP7NHSLEA45A5YLS6PNKMIZFV4BBU2HXA5IRVBJA6AEYSMFHKR2IW7O4WQ7POWQNX45A2JRYDL2OXJFJR2VJWTTKHVABPQ7IIOEVVEVXBKTU2G36XSOJQLGPF3CJNDGAZVK7CKXUMYGCMYYDBTIJZUFY2C5NBOGRQLILALD4FOAAGBUCOPFGGENOSCQDLYP2GZ72MLR5QFFA32VALIM4U3XJICMRW4YJNKUUIZ4MTYXHUTLB4C644MZLLZTSOYC2676PMOQQ7IHQ",
        Longest => "P2PM1-AH6FDTMOMIMKDI4NUR7NAARQ6BMAQFXNCO5DGA5MLXVZCFKISCACLXU63N6XW7OBWTJVWYOC5TSDKNZ7QNB4QW3YM5G237D6CRXYQK2P5QLSXE5NLZLDX5ETFRYOCJCQGTBVIZ7PF36U2ZHL7AMWQNDH4K7ZX7ADIB4HQ6DYPB4HQ6DYPB4HQ6DYPB4HQ6DYPB4HQ6DYPB4HQ6DYPB4HQ6DYPB4HQ6DYPB4HQ6DYPB4HQ6DYPB4HQ6DYPB4HQ6DYPB4HQ6DYYUW4GRYK67NW64B3LPFK7FU422PZQHT3ALBLNOMJEFTYFRL5XWOWZIZJ3V7RRJO25ZWKXPQ4WB7VS7LES6IKYMEOOJANXKX5ZCUXSDY",
        _ => throw new ArgumentOutOfRangeException(nameof(sample)),
    };

    public static KeySet Keys(string sample) => sample is Alice or Cyrillic ? TestKeys.Alice() : TestKeys.Bob();

    public static string DisplayText(string sample) => sample switch
    {
        Alice => "Alice",
        Bob => "Bob",
        Cyrillic => "Микита",
        Longest => new string('x', 64),
        _ => throw new ArgumentOutOfRangeException(nameof(sample)),
    };

    public static ContactCode Create(string sample)
    {
        using var keys = Keys(sample);
        return ContactCode.Create(keys, TestKeys.Name(DisplayText(sample)));
    }

    public static byte[] Bytes(string sample) => Convert.FromHexString(Hex(sample));

    public static ContactCode Parse(string sample)
    {
        if (!ContactCode.TryParse(Bytes(sample), out var code, out _))
        {
            throw new InvalidOperationException("The sample code was not accepted.");
        }

        return code;
    }

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
