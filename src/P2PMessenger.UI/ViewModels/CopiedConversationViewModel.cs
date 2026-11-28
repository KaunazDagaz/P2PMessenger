using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Copy;
using P2PMessenger.Core;
using P2PMessenger.Core.Copy;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class CopiedConversationViewModel : PageViewModel
{
    private readonly Messenger messenger;
    private readonly CopyOriginId origin;
    private readonly IUiThread uiThread;
    private readonly INavigationService navigation;
    private readonly TimeZoneInfo timeZone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(DeleteCopyQuestion))]
    private string chatTitle = string.Empty;

    [ObservableProperty]
    private string bannerText = string.Empty;

    [ObservableProperty]
    private bool isEmpty = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteCopyVisible))]
    private bool isConfirmingDeleteCopy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCopyCommand), nameof(CancelDeleteCopyCommand))]
    private bool isDeletingCopy;

    [ObservableProperty]
    private string? errorMessage;

    public CopiedConversationViewModel(Messenger messenger, CopyOriginId origin, IUiThread uiThread, INavigationService navigation)
        : this(messenger, origin, uiThread, navigation, TimeZoneInfo.Local)
    {
    }

    public CopiedConversationViewModel(
        Messenger messenger,
        CopyOriginId origin,
        IUiThread uiThread,
        INavigationService navigation,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(timeZone);

        this.messenger = messenger;
        this.origin = origin;
        this.uiThread = uiThread;
        this.navigation = navigation;
        this.timeZone = timeZone;
        Refresh();
    }

    public override string Title => ChatTitle;

    public CopyOriginId Origin => origin;

    public ObservableCollection<CopiedMessageItemViewModel> Messages { get; } = [];

    public bool IsDeleteCopyVisible => !IsConfirmingDeleteCopy;

    public string DeleteCopyQuestion => Formats.With(Formats.CopiedConversationDeleteQuestion, ChatTitle);

    private bool IsNotDeletingCopy => !IsDeletingCopy;

    public override void OnShown()
    {
        messenger.CopiedChats.Changed += OnCopiedChatsChanged;
        Refresh();
    }

    public override void OnHidden() => messenger.CopiedChats.Changed -= OnCopiedChatsChanged;

    [RelayCommand]
    private void AskDeleteCopy()
    {
        ErrorMessage = null;
        IsConfirmingDeleteCopy = true;
    }

    [RelayCommand(CanExecute = nameof(IsNotDeletingCopy))]
    private void CancelDeleteCopy() => IsConfirmingDeleteCopy = false;

    [RelayCommand(CanExecute = nameof(IsNotDeletingCopy))]
    private async Task DeleteCopyAsync(CancellationToken cancellationToken)
    {
        IsDeletingCopy = true;
        ErrorMessage = null;
        try
        {
            await messenger.DeleteCopiedChatAsync(origin, cancellationToken);
            IsConfirmingDeleteCopy = false;
            navigation.GoBack();
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            ErrorMessage = UiText.CopiedConversationDeleteFailed;
        }
        finally
        {
            IsDeletingCopy = false;
        }
    }

    private void OnCopiedChatsChanged(object? sender, CopiedChatChangedEventArgs e)
    {
        if (!e.Origin.Equals(origin))
        {
            return;
        }

        uiThread.Post(Refresh);
    }

    private void Refresh()
    {
        Messages.Clear();
        if (!messenger.CopiedChats.TryGet(origin, out CopiedConversation? conversation))
        {
            IsEmpty = true;
            return;
        }

        ChatTitle = conversation.Title.Value;
        string copiedAt = TimeZoneInfo.ConvertTime(conversation.CopiedAt, timeZone).ToString("g", CultureInfo.CurrentCulture);
        BannerText = Formats.With(Formats.CopiedConversationBanner, copiedAt);
        foreach (CopiedMessage message in conversation.Messages)
        {
            Messages.Add(new CopiedMessageItemViewModel(message, timeZone));
        }

        IsEmpty = Messages.Count == 0;
    }
}
