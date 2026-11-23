namespace P2PMessenger.UI.Services;

public sealed class NoQrScanner : IQrScanner
{
    public static NoQrScanner Instance { get; } = new();

    public Task<QrScanResult> ScanAsync(QrScanRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(QrScanResult.NoCamera);
}
