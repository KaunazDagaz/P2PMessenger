namespace P2PMessenger.Desktop.Camera;

public interface ICameraSource
{
    IReadOnlyList<CameraEntry> List();

    Task<IAsyncDisposable> OpenAsync(CameraEntry entry, CameraFormat format, Action<byte[]> onFrame, CancellationToken cancellationToken);
}
