using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Groups;
using P2PMessenger.Core;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class NewGroupViewModel : PageViewModel
{
    private readonly Messenger messenger;
    private readonly INavigationService navigation;
    private readonly Pages pages;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string name = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    public NewGroupViewModel(Messenger messenger, INavigationService navigation, Pages pages)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(pages);

        this.messenger = messenger;
        this.navigation = navigation;
        this.pages = pages;
        foreach (Contact contact in messenger.Contacts.All.Where(static contact => contact.IsAdded))
        {
            Candidates.Add(new GroupCandidateViewModel(contact, CreateCommand.NotifyCanExecuteChanged));
        }
    }

    public override string Title => UiText.NewGroupTitle;

    public ObservableCollection<GroupCandidateViewModel> Candidates { get; } = [];

    public bool HasNoContacts => Candidates.Count == 0;

    public bool HasContacts => !HasNoContacts;

    private bool CanCreate => !IsBusy && !string.IsNullOrWhiteSpace(Name) && Candidates.Any(static candidate => candidate.IsSelected);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            Ed25519PublicKey[] chosen = [.. Candidates.Where(static candidate => candidate.IsSelected).Select(static candidate => candidate.IdentityKey)];
            CreateGroupResult result = await messenger.CreateGroupAsync(Name.Trim(), chosen, cancellationToken);
            if (result.IsCreated)
            {
                navigation.Replace(pages.GroupConversation(result.Group.Id));
                return;
            }

            ErrorMessage = result.Outcome switch
            {
                CreateGroupOutcome.NameNotValid => UiText.NewGroupInvalidName,
                CreateGroupOutcome.NoMembers => UiText.NewGroupNoMembers,
                CreateGroupOutcome.TooManyMembers => Formats.With(Formats.NewGroupTooMany, GroupRecord.MaxMembers),
                _ => UiText.NewGroupNotAdded,
            };
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            ErrorMessage = UiText.NewGroupFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
