using Avalonia.Threading;

namespace P2PMessenger.UI.Services;

public sealed class AvaloniaUiThread : IUiThread
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
