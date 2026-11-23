using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace P2PMessenger.UI.Qr;

public static class BitmapPixels
{
    public static byte[] CopyBgra(Bitmap bitmap, out int stride)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        PixelSize size = bitmap.PixelSize;
        using WriteableBitmap target = new(size, new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Unpremul);
        using ILockedFramebuffer locked = target.Lock();
        bitmap.CopyPixels(locked);
        stride = locked.RowBytes;
        byte[] pixels = new byte[stride * size.Height];
        Marshal.Copy(locked.Address, pixels, 0, pixels.Length);
        return pixels;
    }
}
