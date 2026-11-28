using CommunityToolkit.Mvvm.Input;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class ToastViewModel : ViewModelBase
{
    private readonly Action<ToastViewModel> dismiss;

    public ToastViewModel(ToastKind kind, string text, bool isSticky, Action<ToastViewModel> dismiss)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dismiss);

        Kind = kind;
        Text = text;
        IsSticky = isSticky;
        this.dismiss = dismiss;
    }

    public ToastKind Kind { get; }

    public string Text { get; }

    public bool IsSticky { get; }

    public bool IsSuccess => Kind == ToastKind.Success;

    public bool IsWarning => Kind == ToastKind.Warning;

    public bool IsError => Kind == ToastKind.Error;

    internal CancellationTokenSource? Expiry { get; set; }

    [RelayCommand]
    private void Dismiss() => dismiss(this);
}
