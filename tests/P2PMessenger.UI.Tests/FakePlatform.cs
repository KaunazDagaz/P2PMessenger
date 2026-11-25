using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.Tests;

internal sealed class FakePlatform : IClipboardService, ICodeFileService, IQrScanner
{
    private readonly Queue<QrScanResult> scans = new();

    public string? Clipboard { get; private set; }

    public bool ClipboardWorks { get; set; } = true;

    public (string Name, string Text)? Saved { get; private set; }

    public CodeFileResult SaveResult { get; set; } = new(CodeFileStatus.Done, null);

    public CodeFileResult OpenResult { get; set; } = new(CodeFileStatus.Cancelled, null);

    public Exception? OpenFailure { get; set; }

    public (string Name, byte[] Png)? SavedImage { get; private set; }

    public List<QrScanRequest> ScanRequests { get; } = [];

    public void WillScan(QrScanResult result) => scans.Enqueue(result);

    public void WillScan(string text) => scans.Enqueue(QrScanResult.Read(text));

    public Task<QrScanResult> ScanAsync(QrScanRequest request, CancellationToken cancellationToken)
    {
        ScanRequests.Add(request);
        return Task.FromResult(scans.TryDequeue(out QrScanResult result) ? result : QrScanResult.Cancelled);
    }

    public Task<CodeFileResult> SaveImageAsync(string suggestedName, byte[] png, CancellationToken cancellationToken)
    {
        if (SaveResult.Status == CodeFileStatus.Done)
        {
            SavedImage = (suggestedName, png);
        }

        return Task.FromResult(SaveResult);
    }

    public Task<bool> SetTextAsync(string text)
    {
        if (ClipboardWorks)
        {
            Clipboard = text;
        }

        return Task.FromResult(ClipboardWorks);
    }

    public Task<CodeFileResult> SaveAsync(string suggestedName, string text, CancellationToken cancellationToken)
    {
        if (SaveResult.Status == CodeFileStatus.Done)
        {
            Saved = (suggestedName, text);
        }

        return Task.FromResult(SaveResult);
    }

    public Task<CodeFileResult> OpenAsync(CancellationToken cancellationToken) =>
        OpenFailure is { } failure ? Task.FromException<CodeFileResult>(failure) : Task.FromResult(OpenResult);
}
