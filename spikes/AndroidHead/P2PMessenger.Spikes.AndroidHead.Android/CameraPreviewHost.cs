using AndroidX.Camera.View;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Platform;

namespace P2PMessenger.Spikes.AndroidHead.Head;

internal sealed class CameraPreviewHost(Action<string> onText) : NativeControlHost, IDisposable
{
    private CameraScanSession? _session;

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var activity = ActivityHost.Current ?? throw new InvalidOperationException("The app has no activity to bind the camera to.");
        var view = new PreviewView(activity);
        view.SetImplementationMode(PreviewView.ImplementationMode.Compatible);
        _session = new CameraScanSession(activity, view, activity, onText);
        _session.Start();
        return new AndroidViewControlHandle(view);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Dispose();
        base.DestroyNativeControlCore(control);
    }
}
