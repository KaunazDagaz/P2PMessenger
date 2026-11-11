using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace P2PMessenger.Spikes.AndroidHead.Qr;

public static class QrEncoder
{
    public static bool[,] Encode(string text)
    {
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = 0,
            [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
            [EncodeHintType.CHARACTER_SET] = "UTF-8",
        };
        var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0, hints);
        var modules = new bool[matrix.Height, matrix.Width];
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                modules[y, x] = matrix[x, y];
            }
        }

        return modules;
    }

    public static byte[] PlaceOnCanvas(byte[] pixels, int width, int height, int canvasWidth, int canvasHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(canvasWidth, width);
        ArgumentOutOfRangeException.ThrowIfLessThan(canvasHeight, height);
        var canvas = new byte[canvasWidth * canvasHeight];
        Array.Fill(canvas, (byte)255);
        var left = (canvasWidth - width) / 2;
        var top = (canvasHeight - height) / 2;
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(pixels, y * width, canvas, ((top + y) * canvasWidth) + left, width);
        }

        return canvas;
    }

    public static byte[] RenderLuminance(bool[,] modules, int scale, int quietModules, out int width, out int height)
    {
        var size = modules.GetLength(0);
        width = (size + (2 * quietModules)) * scale;
        height = width;
        var pixels = new byte[width * height];
        Array.Fill(pixels, (byte)255);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (!modules[y, x])
                {
                    continue;
                }

                for (var dy = 0; dy < scale; dy++)
                {
                    var row = (((y + quietModules) * scale) + dy) * width;
                    for (var dx = 0; dx < scale; dx++)
                    {
                        pixels[row + ((x + quietModules) * scale) + dx] = 0;
                    }
                }
            }
        }

        return pixels;
    }
}
