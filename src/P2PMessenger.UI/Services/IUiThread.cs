namespace P2PMessenger.UI.Services;

public interface IUiThread
{
    void Post(Action action);
}
