using System.Net;
using System.Net.Sockets;
using System.Text;
using P2PMessenger.Spikes.AndroidHead.Probe;
using P2PMessenger.Spikes.AndroidHead.Qr;

namespace P2PMessenger.Spikes.AndroidHead.Cli;

internal sealed class SelfTestCommand
{
    private readonly QrDecoder _decoder = new(thorough: true);
    private readonly QrDecoder _fast = new();
    private int _passed;
    private int _failed;

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var test = new SelfTestCommand();
        test.RunQrChecks();
        test.RunFrameChecks();
        test.RunPngChecks();
        await test.RunProbeChecksAsync(cancellationToken).ConfigureAwait(false);
        SpikeLog.Write($"{test._passed} passed, {test._failed} failed");
        return test._failed == 0 ? 0 : 1;
    }

    private static byte[] Rotate(byte[] pixels, int size)
    {
        var rotated = new byte[pixels.Length];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                rotated[(x * size) + (size - 1 - y)] = pixels[(y * size) + x];
            }
        }

        return rotated;
    }

    private static byte[] WithRowPadding(byte[] pixels, int width, int height, int padding)
    {
        var stride = width + padding;
        var padded = new byte[stride * height];
        Array.Fill(padded, (byte)0x7F);
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(pixels, y * width, padded, y * stride, width);
        }

        return padded;
    }

    private static async Task<string?> ExchangeAsync(IPEndPoint endPoint, byte[] request, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(endPoint, timeout.Token).ConfigureAwait(false);
        var stream = client.GetStream();
        await stream.WriteAsync(request, timeout.Token).ConfigureAwait(false);
        var buffer = new byte[512];
        var length = 0;
        try
        {
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }
        }
        catch (IOException)
        {
        }

        return length == 0 ? null : Encoding.UTF8.GetString(buffer, 0, length).TrimEnd('\n');
    }

    private void Check(string name, bool condition, string detail = "")
    {
        if (condition)
        {
            _passed++;
        }
        else
        {
            _failed++;
        }

        SpikeLog.Write($"{(condition ? "PASS" : "FAIL")}  {name}{(detail.Length == 0 ? string.Empty : ": " + detail)}");
    }

    private void RunQrChecks()
    {
        string[] payloads =
        [
            "p2pm-test-code-0001",
            new string('Q', 120) + "-" + new string('7', 120),
            "\u041f\u0440\u0438\u0432\u0435\u0442, \u043a\u043e\u043d\u0442\u0430\u043a\u0442 \u2713",
        ];

        foreach (var payload in payloads)
        {
            var modules = QrEncoder.Encode(payload);
            var pixels = QrEncoder.RenderLuminance(modules, 6, 4, out var width, out var height);
            Check(
                $"QR round trip, {payload.Length} characters, {modules.GetLength(0)} modules",
                _decoder.TryDecodeLuminance(pixels, width, width, height, out var text) && text == payload,
                $"{_decoder.LastDuration.TotalMilliseconds:0.0} ms");

            var turned = pixels;
            for (var turn = 1; turn <= 3; turn++)
            {
                turned = Rotate(turned, width);
                Check($"QR rotated by {turn * 90} degrees", _decoder.TryDecodeLuminance(turned, width, width, height, out text) && text == payload);
                Check(
                    $"QR rotated by {turn * 90} degrees, fast decoder (no auto-rotate, no inversion)",
                    _fast.TryDecodeLuminance(turned, width, width, height, out text) && text == payload,
                    $"{_fast.LastDuration.TotalMilliseconds:0.0} ms");
            }

            var inverted = pixels.Select(value => (byte)(255 - value)).ToArray();
            Check("QR with light modules on a dark background", _decoder.TryDecodeLuminance(inverted, width, width, height, out text) && text == payload);
            Check("the fast decoder does not read light modules on a dark background", !_fast.TryDecodeLuminance(inverted, width, width, height, out _));

            var padded = WithRowPadding(pixels, width, height, 16);
            Check("QR in a camera plane with 16 padding bytes per row", _decoder.TryDecodeLuminance(padded, width + 16, width, height, out text) && text == payload);
            Check("QR in a camera plane with 16 padding bytes per row, fast decoder", _fast.TryDecodeLuminance(padded, width + 16, width, height, out text) && text == payload);
        }

        var blank = new byte[200 * 200];
        Array.Fill(blank, (byte)200);
        Check("a blank frame decodes to nothing", !_decoder.TryDecodeLuminance(blank, 200, 200, 200, out _));
        var noise = new byte[300 * 300];
        new Random(42).NextBytes(noise);
        Check("a noise frame decodes to nothing", !_decoder.TryDecodeLuminance(noise, 300, 300, 300, out _));

        const string PosterPayload = "p2pm-poster-check";
        var posterPixels = QrEncoder.RenderLuminance(QrEncoder.Encode(PosterPayload), 6, 4, out var posterWidth, out var posterHeight);
        var canvasWidth = posterWidth + 200;
        var canvasHeight = posterHeight + 120;
        var poster = QrEncoder.PlaceOnCanvas(posterPixels, posterWidth, posterHeight, canvasWidth, canvasHeight);
        Check(
            "a QR centred on a larger white canvas still decodes",
            poster.Length == canvasWidth * canvasHeight
            && _decoder.TryDecodeLuminance(poster, canvasWidth, canvasWidth, canvasHeight, out var posterText)
            && posterText == PosterPayload);
    }

    private void RunFrameChecks()
    {
        const string Payload = "p2pm-frame-check";
        var modules = QrEncoder.Encode(Payload);
        var pixels = QrEncoder.RenderLuminance(modules, 8, 4, out var width, out var height);
        var bitmap = CameraFrame.ToBitmap24(pixels, width, height);
        Check(
            "a 24-bit bottom-up bitmap, as FlashCap hands over, is read and decoded",
            CameraFrame.TryToLuminance(bitmap, 0, 0, out var plane, out var planeWidth, out var planeHeight)
            && planeWidth == width
            && planeHeight == height
            && _decoder.TryDecodeLuminance(plane, planeWidth, planeWidth, planeHeight, out var text)
            && text == Payload);

        var truncated = bitmap[..(bitmap.Length / 2)];
        Check("a truncated bitmap is refused", !CameraFrame.TryToLuminance(truncated, 0, 0, out _, out _, out _));
        Check("a JPEG frame is recognised", CameraFrame.IsJpeg([0xFF, 0xD8, 0xFF, 0xE0]));
        Check("a bitmap is not taken for JPEG", !CameraFrame.IsJpeg(bitmap));

        var hostile = (byte[])bitmap.Clone();
        hostile[18] = 0xFF;
        hostile[19] = 0xFF;
        hostile[20] = 0xFF;
        hostile[21] = 0x7F;
        Check("a bitmap that claims a huge width is refused", !CameraFrame.TryToLuminance(hostile, 0, 0, out _, out _, out _));
    }

    private void RunPngChecks()
    {
        var modules = QrEncoder.Encode("png-check");
        var pixels = QrEncoder.RenderLuminance(modules, 3, 2, out var width, out var height);
        var png = QrPng.FromLuminance(pixels, width, height);
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] end = [0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];
        Check("PNG signature", png.AsSpan(0, 8).SequenceEqual(signature));
        Check("PNG ends with the IEND chunk and its known CRC", png.AsSpan(png.Length - 12).SequenceEqual(end));
        Check(
            "PNG header carries the image size",
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)) == width
            && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)) == height);
    }

    private async Task RunProbeChecksAsync(CancellationToken cancellationToken)
    {
        await using var listener = new ProbeListener(new IPEndPoint(IPAddress.Loopback, 0), () => "screen=test");
        listener.Start();
        var endPoint = listener.LocalEndPoint;

        var reply = await ExchangeAsync(endPoint, Encoding.UTF8.GetBytes(ProbeProtocol.FormatRequest(7, 1_700_000_000_000) + "\n"), cancellationToken).ConfigureAwait(false);
        Check(
            "the listener answers a probe",
            reply is not null
            && ProbeProtocol.TryParseReply(reply, out var sequence, out var sent, out _, out var state)
            && sequence == 7
            && sent == 1_700_000_000_000
            && state == "screen=test",
            reply ?? "no reply");

        var garbage = await ExchangeAsync(endPoint, Encoding.UTF8.GetBytes("HELLO THERE\n"), cancellationToken).ConfigureAwait(false);
        Check("the listener closes on a request it does not know, without a reply", garbage is null);

        var oversized = await ExchangeAsync(endPoint, new byte[ProbeProtocol.MaxLineLength + 40], cancellationToken).ConfigureAwait(false);
        Check("the listener closes on a line over the limit, without a reply", oversized is null);

        var commandLine = CommandLine.Parse(["probe", "127.0.0.1", "--port", endPoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--count", "3", "--every", "0", "--timeout", "5"]);
        Check("the probe command reaches the listener three times", await ProbeCommand.RunAsync(commandLine, cancellationToken).ConfigureAwait(false) == 0);
        Check("the listener counted its answers", listener.Answered == 4, $"{listener.Answered} answered");
    }
}
