namespace P2PMessenger.UI.Services;

public interface ICodeFileService
{
    Task<CodeFileResult> SaveAsync(string suggestedName, string text, CancellationToken cancellationToken);

    Task<CodeFileResult> SaveImageAsync(string suggestedName, byte[] png, CancellationToken cancellationToken);

    Task<CodeFileResult> OpenAsync(CancellationToken cancellationToken);
}
