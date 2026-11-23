using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace P2PMessenger.UI.Qr;

public sealed class QrMatrix
{
    public const int QuietZone = 4;
    public const int MaskPatterns = 8;

    private static readonly int[] CheckScales = [3, 4];

    private readonly bool[] modules;

    private QrMatrix(int size, bool[] modules)
    {
        Size = size;
        this.modules = modules;
    }

    public int Size { get; }

    public int SizeWithQuietZone => Size + (2 * QuietZone);

    public bool this[int x, int y]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(x);
            ArgumentOutOfRangeException.ThrowIfNegative(y);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Size);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Size);
            return modules[(y * Size) + x];
        }
    }

    public static QrMatrix Encode(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        QrMatrix chosen = EncodeWith(text, null);
        if (ReadsUpright(chosen, text))
        {
            return chosen;
        }

        for (int mask = 0; mask < MaskPatterns; mask++)
        {
            QrMatrix candidate = EncodeWith(text, mask);
            if (ReadsUpright(candidate, text))
            {
                return candidate;
            }
        }

        return chosen;
    }

    public byte[] ToLuminance(int scale, out int width)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, 64);

        width = SizeWithQuietZone * scale;
        byte[] pixels = new byte[width * width];
        Array.Fill(pixels, (byte)255);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (!modules[(y * Size) + x])
                {
                    continue;
                }

                for (int row = 0; row < scale; row++)
                {
                    int start = ((((y + QuietZone) * scale) + row) * width) + ((x + QuietZone) * scale);
                    pixels.AsSpan(start, scale).Clear();
                }
            }
        }

        return pixels;
    }

    private static QrMatrix EncodeWith(string text, int? mask)
    {
        Dictionary<EncodeHintType, object> hints = new()
        {
            [EncodeHintType.MARGIN] = 0,
            [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
        };
        if (mask is { } pattern)
        {
            hints[EncodeHintType.QR_MASK_PATTERN] = pattern;
        }

        BitMatrix matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0, hints);
        if (matrix.Width != matrix.Height)
        {
            throw new InvalidOperationException("The QR encoder returned a matrix that is not square.");
        }

        int size = matrix.Width;
        bool[] modules = new bool[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                modules[(y * size) + x] = matrix[x, y];
            }
        }

        return new QrMatrix(size, modules);
    }

    private static bool ReadsUpright(QrMatrix matrix, string text)
    {
        QrDecoder decoder = new(false);
        foreach (int scale in CheckScales)
        {
            byte[] pixels = matrix.ToLuminance(scale, out int width);
            if (!string.Equals(decoder.DecodeLuminance(pixels, width, width, width), text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
