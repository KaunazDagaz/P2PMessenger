using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Core.Tests.Contacts;

internal static class CodeBuilder
{
    public static byte[] ForKeys(KeySet keys, string name) =>
        Build(
            keys,
            ContactCode.Version,
            keys.IdentityKey.Bytes.ToArray(),
            keys.StaticKey.Bytes.ToArray(),
            keys.OnionAddress.Bytes.ToArray(),
            CodeSamples.Utf8(name));

    public static byte[] Build(
        KeySet signer,
        byte version,
        byte[] identityKey,
        byte[] staticKey,
        byte[] onionAddress,
        byte[] name,
        int? declaredNameLength = null,
        bool withContext = true)
    {
        byte[] body = [version, .. identityKey, .. staticKey, .. onionAddress, (byte)(declaredNameLength ?? name.Length), .. name];
        var signature = signer.IdentityKeyPair.Sign(withContext ? ContactCode.SignedMessage(body) : body);

        return [.. body, .. signature];
    }
}
