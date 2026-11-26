namespace P2PMessenger.Core.Copy;

internal static class CopyPrologue
{
    private static ReadOnlySpan<byte> Label => "P2PM/pc-copy/v1"u8;

    public static byte[] Build(CopyPairingCode pairingCode)
    {
        ReadOnlySpan<byte> raw = pairingCode.RawBytes;
        byte[] prologue = new byte[Label.Length + raw.Length];
        Label.CopyTo(prologue);
        raw.CopyTo(prologue.AsSpan(Label.Length));
        return prologue;
    }
}
