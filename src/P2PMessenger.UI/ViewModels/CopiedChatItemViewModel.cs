using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application.Copy;
using P2PMessenger.Core.Copy;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class CopiedChatItemViewModel : ViewModelBase
{
    private readonly CopiedConversation conversation;
    private readonly Action<CopiedConversation> open;

    public CopiedChatItemViewModel(CopiedConversation conversation, TimeZoneInfo timeZone, Action<CopiedConversation> open)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(open);

        this.conversation = conversation;
        this.open = open;
        Origin = conversation.Origin;
        Title = conversation.Title.Value;
        string copiedAt = TimeZoneInfo.ConvertTime(conversation.CopiedAt, timeZone).ToString("g", CultureInfo.CurrentCulture);
        SummaryText = conversation.Messages.Count == 1
            ? Formats.With(Formats.CopiedChatItemSummaryOne, copiedAt)
            : Formats.WithAll(Formats.CopiedChatItemSummary, conversation.Messages.Count, copiedAt);
    }

    public CopyOriginId Origin { get; }

    public string Title { get; }

    public string SummaryText { get; }

    [RelayCommand]
    private void Open() => open(conversation);
}
