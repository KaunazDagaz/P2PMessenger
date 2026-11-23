using Android.Content;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using AndroidX.Lifecycle;
using Java.Util.Concurrent;
using P2PMessenger.UI.Qr;

namespace P2PMessenger.Android.Scanning;

internal sealed class CameraXScanner : IDisposable
{
    private readonly Context context;
    private readonly PreviewView preview;
    private readonly ILifecycleOwner owner;
    private readonly Action<string> found;
    private readonly Action<bool> failed;
    private readonly IExecutorService executor;
    private readonly Analyzer analyzer;
    private ProcessCameraProvider? provider;
    private bool disposed;

    public CameraXScanner(Context context, PreviewView preview, ILifecycleOwner owner, Action<string> found, Action<bool> failed)
    {
        this.context = context;
        this.preview = preview;
        this.owner = owner;
        this.found = found;
        this.failed = failed;
        executor = Executors.NewSingleThreadExecutor() ?? throw new InvalidOperationException("No executor for the camera analysis.");
        analyzer = new Analyzer(found);
    }

    public void Start()
    {
        Google.Common.Util.Concurrent.IListenableFuture future = ProcessCameraProvider.GetInstance(context);
        future.AddListener(new Java.Lang.Runnable(() => OnProviderReady(future)), ContextCompat.GetMainExecutor(context));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            provider?.UnbindAll();
        }
        catch (Java.Lang.Exception)
        {
        }

        executor.Shutdown();
        analyzer.Dispose();
    }

    private void OnProviderReady(Google.Common.Util.Concurrent.IListenableFuture future)
    {
        if (disposed)
        {
            return;
        }

        try
        {
            provider = (ProcessCameraProvider)(future.Get() ?? throw new InvalidOperationException("No camera provider."));
            CameraSelector? selector = Choose(provider);
            if (selector is null)
            {
                failed(true);
                return;
            }

            using Preview.Builder previewBuilder = new();
            Preview previewCase = previewBuilder.Build() ?? throw new InvalidOperationException("No preview use case.");
            previewCase.SetSurfaceProvider(ContextCompat.GetMainExecutor(context), preview.SurfaceProvider);
            using ImageAnalysis.Builder analysisBuilder = new();
            _ = analysisBuilder.SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest);
            ImageAnalysis analysis = analysisBuilder.Build() ?? throw new InvalidOperationException("No analysis use case.");
            analysis.SetAnalyzer(executor, analyzer);
            provider.UnbindAll();
            _ = provider.BindToLifecycle(owner, selector, previewCase, analysis);
        }
        catch (Exception exception) when (exception is Java.Lang.Exception or InvalidOperationException or System.Reflection.TargetInvocationException)
        {
            failed(false);
        }
    }

    private static CameraSelector? Choose(ProcessCameraProvider provider)
    {
        foreach (CameraSelector? selector in new[] { CameraSelector.DefaultBackCamera, CameraSelector.DefaultFrontCamera })
        {
            if (selector is not null && provider.HasCamera(selector))
            {
                return selector;
            }
        }

        return null;
    }

    private sealed class Analyzer(Action<string> found) : Java.Lang.Object, ImageAnalysis.IAnalyzer
    {
        private readonly QrFrameReader reader = new();
        private int done;

        public void Analyze(IImageProxy? image)
        {
            if (image is null)
            {
                return;
            }

            try
            {
                if (Volatile.Read(ref done) != 0)
                {
                    return;
                }

                var planes = image.GetPlanes();
                if (planes is null || planes.Length == 0)
                {
                    return;
                }

                var luminance = planes[0];
                if (luminance.Buffer is not { } buffer || luminance.PixelStride != 1)
                {
                    return;
                }

                byte[] plane = new byte[buffer.Remaining()];
                _ = buffer.Get(plane);
                if (reader.ReadLuminance(plane, luminance.RowStride, image.Width, image.Height) is { } text
                    && Interlocked.Exchange(ref done, 1) == 0)
                {
                    found(text);
                }
            }
            finally
            {
                image.Close();
            }
        }
    }
}
