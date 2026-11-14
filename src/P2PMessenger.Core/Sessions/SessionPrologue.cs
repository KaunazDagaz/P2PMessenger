using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Core.Sessions;

internal static class SessionPrologue
{
    public static ReadOnlySpan<byte> Label => "P2PM/session/v1"u8;

    public static byte[] Build(ReadOnlySpan<byte> hint, Ed25519PublicKey initiatorIdentityKey, Ed25519PublicKey responderIdentityKey)
    {
        int hintOffset = Label.Length;
        int initiatorOffset = hintOffset + hint.Length;
        int responderOffset = initiatorOffset + Ed25519PublicKey.Length;

        byte[] prologue = new byte[responderOffset + Ed25519PublicKey.Length];
        Label.CopyTo(prologue);
        hint.CopyTo(prologue.AsSpan(hintOffset));
        initiatorIdentityKey.Bytes.CopyTo(prologue.AsSpan(initiatorOffset));
        responderIdentityKey.Bytes.CopyTo(prologue.AsSpan(responderOffset));
        return prologue;
    }
}
