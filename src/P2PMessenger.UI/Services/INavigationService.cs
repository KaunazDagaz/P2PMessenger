using P2PMessenger.UI.ViewModels;

namespace P2PMessenger.UI.Services;

public interface INavigationService
{
    PageViewModel? CurrentPage { get; }

    bool CanGoBack { get; }

    void Push(PageViewModel page);

    void Open(PageViewModel page);

    void Replace(PageViewModel page);

    void ResetTo(PageViewModel page);

    void GoBack();
}
