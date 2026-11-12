using P2PMessenger.Core.Identity;

namespace P2PMessenger.Core.Tests;

internal static class TestKeys
{
    public const string Rfc8032Seed1 = "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60";
    public const string Rfc8032Public1 = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a";
    public const string Rfc8032Seed2 = "4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb";
    public const string Rfc8032Public2 = "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c";
    public const string Rfc8032Seed3 = "c5aa8df43f9f837bedb7442f31dcb7b166d38535076f094b85ce3a2e0b4458f7";
    public const string Rfc8032Public3 = "fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025";
    public const string Rfc8032SeedSha = "833fe62409237b9d62ec77587520911e9a759cec1d19755b7da901b96dca3d42";
    public const string Rfc8032PublicSha = "ec172b93ad5e563bf4932c70e1245034c35467ef2efd4d64ebf819683467e2bf";
    public const string Rfc7748AlicePrivate = "77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a";
    public const string Rfc7748AlicePublic = "8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a";
    public const string Rfc7748BobPrivate = "5dab087e624a8a4b79e17f8b83800ee66f3bb1292618b6fd1c2f8b27ff88e0eb";
    public const string Rfc7748BobPublic = "de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f";

    public static KeySet Alice() => FromHex(Rfc8032Seed1, Rfc7748AlicePrivate, Rfc8032Seed2);

    public static KeySet Bob() => FromHex(Rfc8032Seed3, Rfc7748BobPrivate, Rfc8032SeedSha);

    public static KeySet FromHex(string identitySeed, string staticPrivateKey, string onionSeed)
    {
        byte[] exported = Export(identitySeed, staticPrivateKey, onionSeed);

        if (!KeySet.TryImport(exported, out KeySet? keySet))
        {
            throw new InvalidOperationException("The test key set was not accepted.");
        }

        return keySet;
    }

    public static byte[] Export(string identitySeed, string staticPrivateKey, string onionSeed) =>
        [1, .. Convert.FromHexString(identitySeed), .. Convert.FromHexString(staticPrivateKey), .. Convert.FromHexString(onionSeed)];

    public static DisplayName Name(string text)
    {
        if (!DisplayName.TryCreate(text, out DisplayName? name))
        {
            throw new InvalidOperationException("The test name was not accepted.");
        }

        return name;
    }
}
