using Avalonia.Media.Imaging;
using P2PMessenger.UI.Qr;

namespace P2PMessenger.Desktop.Camera;

public static class JpegFrames
{
    public static CameraImage? Decode(byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);

        using MemoryStream stream = new(jpeg, false);
        using Bitmap bitmap = new(stream);
        if (bitmap.PixelSize.Width is < 1 or > CameraFrames.MaxSide || bitmap.PixelSize.Height is < 1 or > CameraFrames.MaxSide)
        {
            return null;
        }

        byte[] pixels = BitmapPixels.CopyBgra(bitmap, out int stride);
        return CameraFrames.FromBgra(pixels, bitmap.PixelSize.Width, bitmap.PixelSize.Height, stride);
    }
}
