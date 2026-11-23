namespace P2PMessenger.UI.Qr;

public sealed class QrFrameReader
{
    public const int ThoroughEvery = 5;

    private readonly QrDecoder fast = new(false);
    private readonly QrDecoder thorough = new(true);
    private long frames;

    public long Frames => frames;

    public string? ReadLuminance(byte[] plane, int rowStride, int width, int height)
    {
        long frame = ++frames;
        return fast.DecodeLuminance(plane, rowStride, width, height)
            ?? (frame % ThoroughEvery == 0 ? thorough.DecodeLuminance(plane, rowStride, width, height) : null);
    }
}
