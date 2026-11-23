namespace P2PMessenger.UI.Services;

public interface IQrScanner
{
    Task<QrScanResult> ScanAsync(QrScanRequest request, CancellationToken cancellationToken);
}
