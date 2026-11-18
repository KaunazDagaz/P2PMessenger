namespace P2PMessenger.UI.Services;

public interface IClipboardService
{
    Task<bool> SetTextAsync(string text);
}
