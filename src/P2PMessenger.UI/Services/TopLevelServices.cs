using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using P2PMessenger.Core;
using P2PMessenger.UI.Qr;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.Services;

public sealed class TopLevelServices : IClipboardService, ICodeFileService
{
    public const int MaxCodeFileBytes = 8192;

    private static readonly FilePickerFileType TextFiles = new("Text") { Patterns = ["*.txt"], MimeTypes = ["text/plain"] };

    private static readonly FilePickerFileType CodeFiles = new(UiText.AddContactFileType)
    {
        Patterns = ["*.txt", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp"],
        MimeTypes = ["text/plain", "image/png", "image/jpeg", "image/gif", "image/webp"],
    };

    private readonly Func<TopLevel?> topLevel;

    public TopLevelServices(Func<TopLevel?> topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        this.topLevel = topLevel;
    }

    public async Task<bool> SetTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        IClipboard? clipboard = topLevel()?.Clipboard;
        if (clipboard is null)
        {
            return false;
        }

        await clipboard.SetTextAsync(text);
        return true;
    }

    public async Task<CodeFileResult> SaveAsync(string suggestedName, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        IStorageProvider? storage = topLevel()?.StorageProvider;
        if (storage is null || !storage.CanSave)
        {
            return new CodeFileResult(CodeFileStatus.Failed, null);
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = UiText.ProfileFileTitle,
            SuggestedFileName = suggestedName,
            DefaultExtension = "txt",
            FileTypeChoices = [TextFiles],
        });
        if (file is null)
        {
            return new CodeFileResult(CodeFileStatus.Cancelled, null);
        }

        try
        {
            await using Stream stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await stream.WriteAsync(bytes, cancellationToken);
            return new CodeFileResult(CodeFileStatus.Done, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new CodeFileResult(CodeFileStatus.Failed, null);
        }
    }

    public async Task<CodeFileResult> SaveImageAsync(string suggestedName, byte[] png, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(png);

        IStorageProvider? storage = topLevel()?.StorageProvider;
        if (storage is null || !storage.CanSave)
        {
            return new CodeFileResult(CodeFileStatus.Failed, null);
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = UiText.ProfileImageFileTitle,
            SuggestedFileName = suggestedName,
            DefaultExtension = "png",
            FileTypeChoices = [FilePickerFileTypes.ImagePng],
        });
        if (file is null)
        {
            return new CodeFileResult(CodeFileStatus.Cancelled, null);
        }

        try
        {
            await using Stream stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(png, cancellationToken);
            return new CodeFileResult(CodeFileStatus.Done, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new CodeFileResult(CodeFileStatus.Failed, null);
        }
    }

    public async Task<CodeFileResult> OpenAsync(CancellationToken cancellationToken)
    {
        IStorageProvider? storage = topLevel()?.StorageProvider;
        if (storage is null || !storage.CanOpen)
        {
            return new CodeFileResult(CodeFileStatus.Failed, null);
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = UiText.AddContactFileTitle,
            AllowMultiple = false,
            FileTypeFilter = [CodeFiles, TextFiles, FilePickerFileTypes.ImageAll, FilePickerFileTypes.All],
        });
        if (files.Count == 0)
        {
            return new CodeFileResult(CodeFileStatus.Cancelled, null);
        }

        try
        {
            byte[] content;
            await using (Stream stream = await files[0].OpenReadAsync())
            {
                content = await ReadAtMostAsync(stream, ImageFiles.MaxImageBytes + 1, cancellationToken);
            }

            return await Task.Run(() => FromFile(content), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new CodeFileResult(CodeFileStatus.Failed, null);
        }
    }

    public static CodeFileResult FromFile(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (ImageFiles.LooksLikeImage(content))
        {
            if (content.Length > ImageFiles.MaxImageBytes)
            {
                return new CodeFileResult(CodeFileStatus.TooLarge, null);
            }

            try
            {
                using MemoryStream image = new(content, false);
                return QrImageReader.Read(image) is { } text
                    ? new CodeFileResult(CodeFileStatus.Done, text)
                    : new CodeFileResult(CodeFileStatus.NoCodeInImage, null);
            }
            catch (Exception exception) when (Failures.IsFailure(exception))
            {
                return new CodeFileResult(CodeFileStatus.Failed, null);
            }
        }

        return content.Length > MaxCodeFileBytes
            ? new CodeFileResult(CodeFileStatus.TooLarge, null)
            : new CodeFileResult(CodeFileStatus.Done, Encoding.UTF8.GetString(content));
    }

    private static async Task<byte[]> ReadAtMostAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using MemoryStream content = new();
        byte[] buffer = new byte[81920];
        int read;
        while (content.Length < limit && (read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - content.Length)), cancellationToken)) > 0)
        {
            content.Write(buffer, 0, read);
        }

        return content.ToArray();
    }
}
