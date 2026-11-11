using System.Globalization;
using P2PMessenger.Spikes.AndroidHead.Qr;

namespace P2PMessenger.Spikes.AndroidHead.Cli;

internal static class QrCommand
{
    public static int Run(CommandLine line)
    {
        var text = line.Positional(0);
        if (text is null)
        {
            Console.WriteLine("qr needs the text to encode, for example: qr \"p2pm-test-code-0001\" --png run\\qr.png");
            return 2;
        }

        var modules = QrEncoder.Encode(text);
        var png = line.Text("png");
        if (png is not null)
        {
            var scale = Math.Max(1, line.Number("scale", 10));
            var pixels = QrEncoder.RenderLuminance(modules, scale, 4, out var width, out var height);
            var canvas = line.Text("canvas");
            if (canvas is not null)
            {
                if (!TryParseCanvas(canvas, out var canvasWidth, out var canvasHeight) || canvasWidth < width || canvasHeight < height)
                {
                    Console.WriteLine($"--canvas needs <width>x<height> of at least {width}x{height} for this code and scale, for example --canvas 1280x960");
                    return 2;
                }

                pixels = QrEncoder.PlaceOnCanvas(pixels, width, height, canvasWidth, canvasHeight);
                width = canvasWidth;
                height = canvasHeight;
            }

            File.WriteAllBytes(png, QrPng.FromLuminance(pixels, width, height));
            Console.WriteLine($"Wrote {Path.GetFullPath(png)}: {width} x {height} pixels, {modules.GetLength(0)} modules a side");
            return 0;
        }

        Console.WriteLine(QrText.Render(modules, 2, line.Flag("dark")));
        Console.WriteLine($"{modules.GetLength(0)} modules a side; add --dark for a terminal with light text on a dark background, or --png <file>");
        return 0;
    }

    private static bool TryParseCanvas(string text, out int width, out int height)
    {
        width = 0;
        height = 0;
        var parts = text.Split('x');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out height)
            && width is > 0 and <= 8192
            && height is > 0 and <= 8192;
    }
}
