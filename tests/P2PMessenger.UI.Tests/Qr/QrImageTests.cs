using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using P2PMessenger.UI.Qr;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Qr;

public sealed class QrImageTests
{
    [AvaloniaFact]
    [Trait("Story", "US-02")]
    public void FromFile_SavedQrImage_GivesTheCodeBack()
    {
        var text = QrCodeTests.CodeText("Alice");

        var result = TopLevelServices.FromFile(QrPng.Render(QrMatrix.Encode(text)));

        Assert.Equal(new CodeFileResult(CodeFileStatus.Done, text), result);
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    public void FromFile_LargeScreenshotWithATransparentBackground_GivesTheCode()
    {
        var text = QrCodeTests.CodeText("Bob");
        var png = Screenshot(QrMatrix.Encode(text), 3000, 2400, 18);

        var result = TopLevelServices.FromFile(png);

        Assert.Equal(new CodeFileResult(CodeFileStatus.Done, text), result);
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    public void FromFile_JpegPhotoOfTheCode_GivesTheCode()
    {
        var text = QrCodeTests.CodeText("Dave");
        var jpeg = Screenshot(QrMatrix.Encode(text), 1200, 1600, 8, new JpegBitmapEncoderOptions { Quality = 80 });

        Assert.True(ImageFiles.LooksLikeImage(jpeg));
        Assert.Equal(new CodeFileResult(CodeFileStatus.Done, text), TopLevelServices.FromFile(jpeg));
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    public void FromFile_PictureWithoutACode_SaysSo()
    {
        var png = Screenshot(null, 400, 300, 1);

        Assert.Equal(new CodeFileResult(CodeFileStatus.NoCodeInImage, null), TopLevelServices.FromFile(png));
    }

    [AvaloniaFact]
    public void FromFile_DamagedPicture_CannotBeRead()
    {
        byte[] broken = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5];

        Assert.Equal(new CodeFileResult(CodeFileStatus.Failed, null), TopLevelServices.FromFile(broken));
    }

    [AvaloniaFact]
    [Trait("Story", "US-03")]
    public void FromFile_TextAndSizeLimits()
    {
        var text = QrCodeTests.CodeText("Carol");
        var tooLargeImage = new byte[ImageFiles.MaxImageBytes + 1];
        tooLargeImage[0] = 0xFF;
        tooLargeImage[1] = 0xD8;
        tooLargeImage[2] = 0xFF;

        Assert.Equal(new CodeFileResult(CodeFileStatus.Done, text), TopLevelServices.FromFile(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(new CodeFileResult(CodeFileStatus.TooLarge, null), TopLevelServices.FromFile(new byte[TopLevelServices.MaxCodeFileBytes + 1]));
        Assert.Equal(new CodeFileResult(CodeFileStatus.TooLarge, null), TopLevelServices.FromFile(tooLargeImage));
    }

    [AvaloniaFact]
    [Trait("Story", "US-01")]
    public async Task Profile_TheQrCodeOnTheScreen_IsReadable()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var view = new ProfileView { DataContext = harness.Shell.Pages.Profile() };
        var window = harness.Show(view);

        using var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.Equal(harness.Messenger.Identity.CodeText, Read(frame));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    public async Task AddInPerson_TheQrCodeOnTheScreen_IsReadable()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var view = new AddInPersonView { DataContext = harness.Shell.Pages.AddInPerson() };
        var window = harness.Show(view);

        using var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.Equal(harness.Messenger.Identity.CodeText, Read(frame));
        harness.Messenger.Identity.Dispose();
    }

    private static string? Read(Bitmap frame)
    {
        var size = frame.PixelSize;
        var pixels = BitmapPixels.CopyBgra(frame, out var stride);
        var plane = Luminance.FromBgraOverWhite(pixels, size.Width, size.Height, stride);
        return new QrDecoder(false).DecodeLuminance(plane, size.Width, size.Width, size.Height);
    }

    private static byte[] Screenshot(QrMatrix? matrix, int width, int height, int scale) => Screenshot(matrix, width, height, scale, PngBitmapEncoderOptions.Default);

    private static byte[] Screenshot(QrMatrix? matrix, int width, int height, int scale, BitmapEncoderOptions format)
    {
        var opaque = format is JpegBitmapEncoderOptions;
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Unpremul);
        using (var locked = bitmap.Lock())
        {
            var row = new byte[locked.RowBytes];
            for (var y = 0; y < height; y++)
            {
                Array.Clear(row);
                for (var x = 0; x < width; x++)
                {
                    if (matrix is null)
                    {
                        row[(x * 4) + 1] = (byte)(x % 256);
                        row[(x * 4) + 3] = 255;
                        continue;
                    }

                    var moduleX = (x - ((width - (matrix.Size * scale)) / 2)) / scale;
                    var moduleY = (y - ((height - (matrix.Size * scale)) / 2)) / scale;
                    var inside = x >= (width - (matrix.Size * scale)) / 2 && y >= (height - (matrix.Size * scale)) / 2 && moduleX < matrix.Size && moduleY < matrix.Size;
                    if (inside && matrix[moduleX, moduleY])
                    {
                        row[(x * 4) + 3] = 255;
                    }
                    else if (opaque)
                    {
                        row.AsSpan(x * 4, 4).Fill(255);
                    }
                }

                Marshal.Copy(row, 0, locked.Address + (y * locked.RowBytes), row.Length);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, format);
        return stream.ToArray();
    }
}
