using Avalonia;
using Avalonia.Media.Imaging;

namespace P2PMessenger.UI.Qr;

public static class QrImageReader
{
    public static string? Read(Stream image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using Bitmap decoded = new(image);
        PixelSize size = decoded.PixelSize;
        if (size.Width < 1 || size.Height < 1)
        {
            return null;
        }

        int longest = Math.Max(size.Width, size.Height);
        if (longest <= ImageFiles.MaxDecodedSide)
        {
            return ReadBitmap(decoded);
        }

        double factor = (double)ImageFiles.MaxDecodedSide / longest;
        PixelSize smaller = new(Math.Max(1, (int)(size.Width * factor)), Math.Max(1, (int)(size.Height * factor)));
        using Bitmap scaled = decoded.CreateScaledBitmap(smaller, BitmapInterpolationMode.HighQuality);
        return ReadBitmap(scaled);
    }

    private static string? ReadBitmap(Bitmap bitmap)
    {
        PixelSize size = bitmap.PixelSize;
        byte[] pixels = BitmapPixels.CopyBgra(bitmap, out int stride);
        byte[] plane = Luminance.FromBgraOverWhite(pixels, size.Width, size.Height, stride);
        return new QrDecoder(true).DecodeLuminance(plane, size.Width, size.Width, size.Height)
            ?? new QrDecoder(false).DecodeLuminance(plane, size.Width, size.Width, size.Height);
    }
}
