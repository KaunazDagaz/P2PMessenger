namespace P2PMessenger.UI.Services;

internal sealed class NoToasts : IToastService
{
    public static readonly NoToasts Instance = new();

    private NoToasts()
    {
    }

    public void Show(ToastKind kind, string text, bool sticky = false)
    {
    }
}
