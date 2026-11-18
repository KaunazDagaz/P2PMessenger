using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.Tests;

internal sealed class FakePlatform : IClipboardService, ICodeFileService
{
    public string? Clipboard { get; private set; }

    public bool ClipboardWorks { get; set; } = true;

    public (string Name, string Text)? Saved { get; private set; }

    public CodeFileResult SaveResult { get; set; } = new(CodeFileStatus.Done, null);

    public CodeFileResult OpenResult { get; set; } = new(CodeFileStatus.Cancelled, null);

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

    public Task<CodeFileResult> OpenAsync(CancellationToken cancellationToken) => Task.FromResult(OpenResult);
}
