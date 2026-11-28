using System.Collections.ObjectModel;
using P2PMessenger.UI.ViewModels;

namespace P2PMessenger.UI.Services;

public sealed class ToastCenter : IToastService, IDisposable
{
    public const int MaxVisible = 4;

    private readonly IUiThread uiThread;
    private bool disposed;

    public ToastCenter(IUiThread uiThread)
    {
        ArgumentNullException.ThrowIfNull(uiThread);
        this.uiThread = uiThread;
    }

    public ObservableCollection<ToastViewModel> Items { get; } = [];

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromSeconds(6);

    public TimeSpan ErrorLifetime { get; set; } = TimeSpan.FromSeconds(10);

    public void Show(ToastKind kind, string text, bool sticky = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        uiThread.Post(() => Add(kind, text, sticky));
    }

    public void Clear() => uiThread.Post(() =>
    {
        foreach (ToastViewModel toast in Items.ToArray())
        {
            Remove(toast);
        }
    });

    public void Dispose()
    {
        disposed = true;
        foreach (ToastViewModel toast in Items)
        {
            toast.Expiry?.Cancel();
            toast.Expiry?.Dispose();
            toast.Expiry = null;
        }
    }

    private void Add(ToastKind kind, string text, bool sticky)
    {
        if (disposed)
        {
            return;
        }

        ToastViewModel? same = Items.FirstOrDefault(toast => toast.Kind == kind && string.Equals(toast.Text, text, StringComparison.Ordinal));
        if (same is not null)
        {
            Remove(same);
        }

        ToastViewModel added = new(kind, text, sticky, Remove);
        Items.Add(added);
        while (Items.Count > MaxVisible)
        {
            Remove(Items[0]);
        }

        if (!sticky)
        {
            Expire(added, kind == ToastKind.Error ? ErrorLifetime : Lifetime);
        }
    }

    private void Expire(ToastViewModel toast, TimeSpan after)
    {
        CancellationTokenSource source = new();
        toast.Expiry = source;
        _ = Task.Delay(after, source.Token).ContinueWith(
            _ => uiThread.Post(() => Remove(toast)),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private void Remove(ToastViewModel toast)
    {
        toast.Expiry?.Cancel();
        toast.Expiry?.Dispose();
        toast.Expiry = null;
        _ = Items.Remove(toast);
    }
}
