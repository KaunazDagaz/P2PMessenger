namespace P2PMessenger.UI.Services;

public interface IToastService
{
    void Show(ToastKind kind, string text, bool sticky = false);
}
