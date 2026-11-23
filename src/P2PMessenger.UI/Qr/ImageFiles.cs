namespace P2PMessenger.UI.Qr;

public static class ImageFiles
{
    public const int MaxImageBytes = 20 * 1024 * 1024;
    public const int MaxDecodedSide = 2048;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    public static bool LooksLikeImage(ReadOnlySpan<byte> start) =>
        start.StartsWith(PngSignature)
        || start.StartsWith(JpegSignature)
        || start.StartsWith("GIF87a"u8)
        || start.StartsWith("GIF89a"u8)
        || (start.Length >= 12 && start.StartsWith("RIFF"u8) && start[8..12].SequenceEqual("WEBP"u8));
}
