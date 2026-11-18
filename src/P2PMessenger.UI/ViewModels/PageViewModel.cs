namespace P2PMessenger.UI.ViewModels;

public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }

    public virtual void OnShown()
    {
    }

    public virtual void OnHidden()
    {
    }
}
