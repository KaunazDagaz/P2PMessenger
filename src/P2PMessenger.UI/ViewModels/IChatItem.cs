namespace P2PMessenger.UI.ViewModels;

public interface IChatItem
{
    string Name { get; }

    DateTimeOffset? LastActivity { get; }
}
