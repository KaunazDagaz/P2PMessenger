using Android.Content;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using AndroidX.Lifecycle;
using Java.Util.Concurrent;
using P2PMessenger.Spikes.AndroidHead.Qr;

namespace P2PMessenger.Spikes.AndroidHead.Head;

internal sealed class CameraScanSession : IDisposable
{
    private readonly Context _context;
    private readonly PreviewView _preview;
    private readonly ILifecycleOwner _owner;
    private readonly IExecutorService _executor;
    private readonly Analyzer _analyzer;
    private ProcessCameraProvider? _provider;
    private bool _disposed;

    public CameraScanSession(Context context, PreviewView preview, ILifecycleOwner owner, Action<string> onText)
    {
        _context = context;
        _preview = preview;
        _owner = owner;
        _executor = Executors.NewSingleThreadExecutor() ?? throw new InvalidOperationException("No executor.");
        _analyzer = new Analyzer(onText, context.FilesDir?.AbsolutePath is { } files ? Path.Combine(files, "run") : null);
    }

    public void Start()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var future = ProcessCameraProvider.GetInstance(_context);
        var ready = new Java.Lang.Runnable(() => OnProviderReady(future, started));
        future.AddListener(ready, ContextCompat.GetMainExecutor(_context));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _provider?.UnbindAll();
        }
        catch (Java.Lang.Exception exception)
        {
            SpikeLog.Write($"scanner: unbinding failed: {exception.Message}");
        }

        _executor.Shutdown();
        _analyzer.Dispose();
        SpikeLog.Write($"scanner: closed after {_analyzer.Summary()}");
    }

    private void OnProviderReady(Google.Common.Util.Concurrent.IListenableFuture future, long started)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _provider = (ProcessCameraProvider)(future.Get() ?? throw new InvalidOperationException("No camera provider."));
            using var previewBuilder = new Preview.Builder();
            var preview = previewBuilder.Build() ?? throw new InvalidOperationException("No preview use case.");
            preview.SetSurfaceProvider(ContextCompat.GetMainExecutor(_context), _preview.SurfaceProvider);
            using var analysisBuilder = new ImageAnalysis.Builder();
            _ = analysisBuilder.SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest);
            var analysis = analysisBuilder.Build() ?? throw new InvalidOperationException("No analysis use case.");
            analysis.SetAnalyzer(_executor, _analyzer);
            _provider.UnbindAll();
            _ = _provider.BindToLifecycle(_owner, CameraSelector.DefaultBackCamera ?? throw new InvalidOperationException("No back camera selector."), preview, analysis);
            SpikeLog.Write($"scanner: camera bound after {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms");
        }
        catch (Exception exception) when (exception is Java.Lang.Exception or InvalidOperationException or System.Reflection.TargetInvocationException)
        {
            SpikeLog.Write($"scanner: binding the camera failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private sealed class Analyzer(Action<string> onText, string? frameFolder) : Java.Lang.Object, ImageAnalysis.IAnalyzer
    {
        private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(5);

        private readonly QrDecoder _decoder = new();
        private readonly long _created = System.Diagnostics.Stopwatch.GetTimestamp();
        private long _lastReport = System.Diagnostics.Stopwatch.GetTimestamp();
        private int _frames;
        private int _decoded;
        private double _decodeMilliseconds;
        private bool _reportedFirstFrame;

        public string Summary() =>
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{_frames} frames, {_decoded} decoded, average decode {(_frames == 0 ? 0 : _decodeMilliseconds / _frames):0.0} ms per frame");

        public void Analyze(IImageProxy? image)
        {
            if (image is null)
            {
                return;
            }

            try
            {
                _frames++;
                var planes = image.GetPlanes() ?? throw new InvalidOperationException("No image planes.");
                var plane = planes[0];
                var buffer = plane.Buffer ?? throw new InvalidOperationException("No plane buffer.");
                var bytes = new byte[buffer.Remaining()];
                _ = buffer.Get(bytes);
                if (!_reportedFirstFrame)
                {
                    _reportedFirstFrame = true;
                    SpikeLog.Write($"scanner: first frame after {System.Diagnostics.Stopwatch.GetElapsedTime(_created).TotalMilliseconds:0} ms, {image.Width}x{image.Height}, rotation {image.ImageInfo?.RotationDegrees}, row stride {plane.RowStride}, pixel stride {plane.PixelStride}, {bytes.Length} bytes");
                }

                if (_frames is 1 or 90)
                {
                    SaveFrame(bytes, plane.RowStride, plane.PixelStride, image.Width, image.Height);
                }

                var found = _decoder.TryDecodeLuminance(bytes, plane.RowStride, image.Width, image.Height, out var text);
                _decodeMilliseconds += _decoder.LastDuration.TotalMilliseconds;
                if (found)
                {
                    _decoded++;
                    onText(text);
                }

                if (System.Diagnostics.Stopwatch.GetElapsedTime(_lastReport) >= ReportEvery)
                {
                    _lastReport = System.Diagnostics.Stopwatch.GetTimestamp();
                    SpikeLog.Write($"scanner: {Summary()}");
                }
            }
            finally
            {
                image.Close();
            }
        }

        private void SaveFrame(byte[] bytes, int rowStride, int pixelStride, int width, int height)
        {
            if (frameFolder is null || pixelStride != 1 || rowStride < width || bytes.Length < (rowStride * (height - 1)) + width)
            {
                return;
            }

            var packed = new byte[width * height];
            var sum = 0L;
            var min = 255;
            var max = 0;
            for (var row = 0; row < height; row++)
            {
                Buffer.BlockCopy(bytes, row * rowStride, packed, row * width, width);
                for (var column = 0; column < width; column++)
                {
                    var value = (int)packed[(row * width) + column];
                    sum += value;
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }
            }

            try
            {
                Directory.CreateDirectory(frameFolder);
                var path = Path.Combine(frameFolder, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"scan-frame-{_frames}.png"));
                File.WriteAllBytes(path, QrPng.FromLuminance(packed, width, height));
                SpikeLog.Write(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"scanner: frame {_frames} saved to {path}; luminance min {min}, max {max}, mean {(double)sum / packed.Length:0.0}"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                SpikeLog.Write($"scanner: could not save frame {_frames}: {exception.Message}");
            }
        }
    }
}
