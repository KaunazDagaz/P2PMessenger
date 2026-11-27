using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application.LocalData;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class BusyBarTests
{
    [AvaloniaFact]
    public async Task MainView_WhileStarting_ShowsTheProgressBar()
    {
        await using var harness = UiHarness.Create();
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        var bar = UiHarness.Find<ProgressBar>(view, "StartingBar");
        Assert.True(bar.IsIndeterminate);
        Assert.True(bar.IsVisible);

        await harness.Shell.StartAsync(CancellationToken.None);
        UiHarness.Flush();

        Assert.False(bar.IsVisible);
    }

    [AvaloniaFact]
    public async Task CreateIdentity_WhileBusy_ShowsTheProgressBar()
    {
        await using var harness = UiHarness.Create();
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        var view = new CreateIdentityView { DataContext = page };
        harness.Show(view);

        FollowsBusy(UiHarness.Find<ProgressBar>(view, "BusyBar"), busy => page.IsBusy = busy);
    }

    [AvaloniaFact]
    public async Task Passphrase_WhileBusy_ShowsTheProgressBar()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = harness.Shell.Pages.Passphrase();
        var view = new PassphraseView { DataContext = page };
        harness.Show(view);

        FollowsBusy(UiHarness.Find<ProgressBar>(view, "BusyBar"), busy => page.IsBusy = busy);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Unlock_WhileBusyAndWhileDeleting_ShowsTheProgressBars()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = harness.Shell.Pages.Unlock(true, (_, _) => Task.FromResult(false));
        var deleteAll = Assert.IsType<DeleteAllDataViewModel>(page.DeleteAll);
        var view = new UnlockView { DataContext = page };
        harness.Show(view);

        FollowsBusy(UiHarness.Find<ProgressBar>(view, "BusyBar"), busy => page.IsBusy = busy);
        FollowsBusy(UiHarness.Descendant<ProgressBar>(view, "DeleteAllBusyBar"), busy => deleteAll.IsBusy = busy);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task StorageProblem_WhileBusyAndWhileDeleting_ShowsTheProgressBars()
    {
        await using var harness = UiHarness.Create();
        var page = harness.Shell.Pages.StorageProblem(LocalDataProblem.Damaged);
        var deleteAll = Assert.IsType<DeleteAllDataViewModel>(page.DeleteAll);
        var view = new StorageProblemView { DataContext = page };
        harness.Show(view);

        FollowsBusy(UiHarness.Find<ProgressBar>(view, "BusyBar"), busy => page.IsBusy = busy);
        FollowsBusy(UiHarness.Descendant<ProgressBar>(view, "DeleteAllBusyBar"), busy => deleteAll.IsBusy = busy);
    }

    [AvaloniaFact]
    public async Task ContactList_WhileSwitchingTor_ShowsTheProgressBar()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);

        FollowsBusy(UiHarness.Find<ProgressBar>(view, "SwitchTorBusyBar"), busy => page.IsSwitchingTor = busy);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task AddContact_WhileBusy_ShowsTheProgressBar()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = harness.Shell.Pages.AddContact();
        var view = new AddContactView { DataContext = page };
        harness.Show(view);

        FollowsBusy(UiHarness.Find<ProgressBar>(view, "BusyBar"), busy => page.IsBusy = busy);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task AddInPerson_WhileBusy_ShowsTheProgressBar()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = harness.Shell.Pages.AddInPerson();
        var view = new AddInPersonView { DataContext = page };
        harness.Show(view);

        FollowsBusy(UiHarness.Find<ProgressBar>(view, "BusyBar"), busy => page.IsBusy = busy);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Profile_WhileBusy_ShowsTheProgressBar()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = harness.Shell.Pages.Profile();
        var view = new ProfileView { DataContext = page };
        harness.Show(view);

        FollowsBusy(UiHarness.Find<ProgressBar>(view, "BusyBar"), busy => page.IsBusy = busy);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Background_WhileBusy_ShowsTheProgressBar()
    {
        var fake = new FakeBackground();
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        var page = harness.Shell.Pages.Background(false);
        var view = new BackgroundView { DataContext = page };
        harness.Show(view);

        FollowsBusy(UiHarness.Find<ProgressBar>(view, "BusyBar"), busy => page.IsBusy = busy);
    }

    [AvaloniaFact]
    public async Task Conversation_WhileSendingAndWhileDeletingTheChat_ShowsTheProgressBars()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = Core.Identity.KeySet.Create();
        var contact = await harness.AddContactAsync(bob, "Bob");
        var page = harness.Shell.Pages.Conversation(contact);
        var view = new ConversationView { DataContext = page };
        harness.Show(view);

        FollowsBusy(UiHarness.Find<ProgressBar>(view, "BusyBar"), busy => page.IsBusy = busy);
        FollowsBusy(UiHarness.Find<ProgressBar>(view, "DeleteChatBusyBar"), busy => page.IsDeletingChat = busy);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task VerifyContact_WhileBusy_ShowsTheProgressBar()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = Core.Identity.KeySet.Create();
        var contact = await harness.AddContactAsync(bob, "Bob");
        var page = harness.Shell.Pages.VerifyContact(contact);
        var view = new VerifyContactView { DataContext = page };
        harness.Show(view);

        FollowsBusy(UiHarness.Find<ProgressBar>(view, "BusyBar"), busy => page.IsBusy = busy);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task NewGroupAndGroupConversation_WhileBusyOrDeleting_ShowTheProgressBars()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var newGroup = harness.Shell.Pages.NewGroup();
        var newGroupView = new NewGroupView { DataContext = newGroup };
        harness.Show(newGroupView);
        var conversation = harness.Shell.Pages.GroupConversation(Core.Groups.GroupId.Create());
        var conversationView = new GroupConversationView { DataContext = conversation };
        harness.Show(conversationView);

        FollowsBusy(UiHarness.Find<ProgressBar>(newGroupView, "BusyBar"), busy => newGroup.IsBusy = busy);
        FollowsBusy(UiHarness.Find<ProgressBar>(conversationView, "BusyBar"), busy => conversation.IsBusy = busy);
        FollowsBusy(UiHarness.Find<ProgressBar>(conversationView, "DeleteChatBusyBar"), busy => conversation.IsDeletingChat = busy);
        FollowsBusy(UiHarness.Find<ProgressBar>(conversationView, "LeaveGroupBusyBar"), busy => conversation.IsLeaving = busy);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-22")]
    public async Task PcCopyViews_WhileBusyOrDeleting_ShowTheProgressBars()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var receive = harness.Shell.Pages.ReceiveCopy();
        var receiveView = new ReceiveCopyView { DataContext = receive };
        harness.Show(receiveView);

        var send = harness.Shell.Pages.SendCopy();
        var sendView = new SendCopyView { DataContext = send };
        harness.Show(sendView);

        using var owner = Core.Identity.KeySet.Create();
        using var bob = Core.Identity.KeySet.Create();
        var origin = Core.Copy.CopyOriginId.ForContact(owner.IdentityKey, bob.IdentityKey);
        var copied = harness.Shell.Pages.CopiedConversation(origin);
        var copiedView = new CopiedConversationView { DataContext = copied };
        harness.Show(copiedView);

        FollowsBusy(UiHarness.Find<ProgressBar>(receiveView, "BusyBar"), busy => receive.IsBusy = busy);
        FollowsBusy(UiHarness.Find<ProgressBar>(sendView, "BusyBar"), busy => send.IsBusy = busy);
        FollowsBusy(UiHarness.Find<ProgressBar>(copiedView, "DeleteCopyBusyBar"), busy => copied.IsDeletingCopy = busy);
        harness.Messenger.Identity.Dispose();
    }

    private static void FollowsBusy(ProgressBar bar, Action<bool> setBusy)
    {
        Assert.True(bar.IsIndeterminate);
        UiHarness.Flush();
        Assert.False(bar.IsVisible);

        setBusy(true);
        UiHarness.Flush();
        Assert.True(bar.IsVisible);

        setBusy(false);
        UiHarness.Flush();
        Assert.False(bar.IsVisible);
    }
}
