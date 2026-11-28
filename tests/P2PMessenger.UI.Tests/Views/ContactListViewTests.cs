using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Tests.Shared;
using P2PMessenger.Transport.Discovery;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class ContactListViewTests
{
    [AvaloniaFact]
    public async Task ContactList_ContactsAndGroupsTogether_TheChatWithTheLatestMessageIsOnTop()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var carol = await harness.ConnectPeerAsync("Carol");
        Assert.True((await bob.Contacts.AddAsync(bob.Contacts.Check(carol.Identity.CodeText), CancellationToken.None)).IsSaved);
        Assert.True((await carol.Contacts.AddAsync(carol.Contacts.Check(bob.Identity.CodeText), CancellationToken.None)).IsSaved);
        await UiHarness.WaitUntilAsync(() =>
            bob.Contacts.TryGet(carol.Identity.Keys.IdentityKey, out var atBob) && atBob.IsAdded
            && carol.Contacts.TryGet(bob.Identity.Keys.IdentityKey, out var atCarol) && atCarol.IsAdded);
        var created = await harness.Messenger.CreateGroupAsync("Field team", [bob.Identity.Keys.IdentityKey, carol.Identity.Keys.IdentityKey], CancellationToken.None);
        var groupId = created.Group!.Id;
        await UiHarness.WaitUntilAsync(() => harness.Messenger.Groups.TryGet(groupId, out var group) && group.State == P2PMessenger.Application.Groups.GroupState.Created);
        var page = harness.Shell.Pages.ContactList();
        var view = new ContactListView { DataContext = page };
        harness.Shell.Push(page);
        harness.Show(view);

        Assert.Equal(["Bob", "Carol", "Field team"], Names(page));
        Assert.Equal(3, UiHarness.Find<ItemsControl>(view, "ChatItems").GetVisualDescendants().OfType<Button>().Count(static button => button.Classes.Contains("row")));

        await harness.Messenger.SendToGroupAsync(groupId, "Meeting point is the north gate.", CancellationToken.None);
        await UiHarness.WaitUntilAsync(() => Names(page)[0] == "Field team");

        await bob.SendAsync(harness.Messenger.Identity.Keys.IdentityKey, "See you at six", CancellationToken.None);
        await UiHarness.WaitUntilAsync(() => Names(page)[0] == "Bob");
        Assert.Equal(["Bob", "Field team", "Carol"], Names(page));

        await harness.Messenger.SendAsync(carol.Identity.Keys.IdentityKey, "Hello Carol", CancellationToken.None);
        await UiHarness.WaitUntilAsync(() => Names(page)[0] == "Carol");
        Assert.Equal(["Carol", "Bob", "Field team"], Names(page));
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.StopPeerAsync(carol);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task ContactList_NoContacts_ShowsTheEmptyText()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };

        harness.Show(view);

        Assert.True(UiHarness.Find<TextBlock>(view, "EmptyText").IsVisible);
        Assert.Equal(UiText.ContactListEmpty, UiHarness.Find<TextBlock>(view, "EmptyText").Text);
        Assert.Equal(BuildInfo.IsDebug, UiHarness.Find<Button>(view, "NetworkButton").IsVisible);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    [Trait("Story", "US-04")]
    public async Task ContactList_ImportedCode_ShowsTheContactWaitingWithItsKeyNotCompared()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);
        using var bob = KeySet.Create();

        await harness.AddContactAsync(bob, "Bob");
        UiHarness.Flush();

        var texts = UiHarness.Texts(view);
        Assert.Contains("Bob", texts);
        Assert.Contains(Fingerprint.Of(bob.IdentityKey).ToGroupedString(), texts);
        Assert.Contains(UiText.ContactWaiting, texts);
        Assert.Contains(UiText.ContactKeyNotCompared, texts);
        Assert.False(UiHarness.Find<TextBlock>(view, "EmptyText").IsVisible);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-06")]
    [Trait("Story", "US-07")]
    public async Task ContactList_ContactFoundAndThenGone_ShowsReachableNearbyAndThenNotReachableNow()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);

        var bob = await harness.ConnectPeerAsync("Bob");
        await UiHarness.WaitUntilAsync(() => UiHarness.Texts(view).Contains(UiText.ContactReachableNearby));

        await UiHarness.StopPeerAsync(bob);
        await UiHarness.WaitUntilAsync(() => UiHarness.Texts(view).Contains(UiText.ContactNotReachable));
        Assert.DoesNotContain(UiText.DialUnreachable, UiHarness.Texts(view));
        Assert.DoesNotContain(UiText.DialTimedOut, UiHarness.Texts(view));
        Assert.DoesNotContain(UiText.DialRefused, UiHarness.Texts(view));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-07")]
    [Trait("Story", "US-09")]
    public async Task ContactList_ContactConnectedOverTor_ShowsReachableOverTor()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        await using var tor = new FakeTorRoute();
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);

        var bob = await harness.ConnectPeerOverTorAsync("Bob", tor);
        await UiHarness.WaitUntilAsync(() => UiHarness.Texts(view).Contains(UiText.ContactReachableOverTor));

        Assert.DoesNotContain(UiText.ContactReachableNearby, UiHarness.Texts(view));
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.WaitUntilAsync(() => UiHarness.Texts(view).Contains(UiText.ContactNotReachable));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-07")]
    public async Task ContactList_NoWiFiAndNoInternet_ShowsTheNoNetworkLine()
    {
        await using var harness = await UiHarness.StartAsync("Alice", NetworkSnapshot.None);
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);

        Assert.True(page.HasNoNetwork);
        Assert.True(UiHarness.Find<Border>(view, "NoNetworkPanel").IsVisible);
        Assert.Equal(UiText.ContactListNoNetwork, UiHarness.Find<TextBlock>(view, "NoNetworkText").Text);

        harness.Node.Network = NetworkSnapshot.Create([LoopbackDiscovery.Interface], hasOtherNetwork: false);
        await UiHarness.WaitUntilAsync(() => !page.HasNoNetwork);
        Assert.False(UiHarness.Find<Border>(view, "NoNetworkPanel").IsVisible);

        harness.Node.Network = NetworkSnapshot.Create([], hasOtherNetwork: true);
        await Task.Delay(1500);
        UiHarness.Flush();
        Assert.False(page.HasNoNetwork);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-07")]
    public async Task ContactList_WithANetwork_SaysNothingAboutAHotspot()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);

        Assert.False(page.HasNoNetwork);
        Assert.False(UiHarness.Find<Border>(view, "NoNetworkPanel").IsVisible);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-04")]
    public async Task ContactList_KeyCheckedAndUnreadMessages_AreShown()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);
        using var bob = KeySet.Create();
        await harness.AddContactAsync(bob, "Bob");

        await harness.Messenger.Contacts.SetKeyComparisonAsync(bob.IdentityKey, KeyComparison.CheckedByYou, CancellationToken.None);
        await harness.Messenger.Chats.ReceiveAsync(bob.IdentityKey, Text(1, "one"), CancellationToken.None);
        await harness.Messenger.Chats.ReceiveAsync(bob.IdentityKey, Text(2, "two"), CancellationToken.None);
        UiHarness.Flush();

        var texts = UiHarness.Texts(view);
        Assert.Contains(UiText.ContactKeyChecked, texts);
        Assert.DoesNotContain(UiText.ContactKeyNotCompared, texts);
        Assert.Contains("2 new", texts);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-11")]
    public async Task ContactList_OpenContact_ShowsTheConversation()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        using var bob = KeySet.Create();
        await harness.AddContactAsync(bob, "Bob");

        Assert.Single(page.Contacts).OpenCommand.Execute(null);
        UiHarness.Flush();

        var conversation = Assert.IsType<ConversationViewModel>(harness.Shell.CurrentPage);
        Assert.Equal("Bob", conversation.Title);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-11")]
    public async Task ContactList_NoContacts_HasNoSearchBox()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };

        harness.Show(view);

        Assert.False(UiHarness.Find<DockPanel>(view, "SearchPanel").IsVisible);
        Assert.False(UiHarness.Find<TextBlock>(view, "NoMatchesText").IsVisible);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-11")]
    public async Task ContactList_TypingInTheSearchBox_ShowsOnlyTheNamesThatContainItIgnoringCase()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);
        using var bob = KeySet.Create();
        using var bobby = KeySet.Create();
        using var carol = KeySet.Create();
        await harness.AddContactAsync(bob, "Bob");
        await harness.AddContactAsync(bobby, "Bobby");
        await harness.AddContactAsync(carol, "Carol");
        Assert.True(UiHarness.Find<DockPanel>(view, "SearchPanel").IsVisible);
        Assert.Equal(["Bob", "Bobby", "Carol"], page.Contacts.Select(static contact => contact.Name));

        UiHarness.Find<TextBox>(view, "SearchBox").Text = "BO";
        UiHarness.Flush();

        Assert.Equal("BO", page.SearchText);
        Assert.Equal(["Bob", "Bobby"], page.Contacts.Select(static contact => contact.Name));
        Assert.DoesNotContain("Carol", UiHarness.Texts(view));
        Assert.True(UiHarness.Find<Button>(view, "ClearSearchButton").IsVisible);
        Assert.False(UiHarness.Find<TextBlock>(view, "NoMatchesText").IsVisible);

        UiHarness.Find<TextBox>(view, "SearchBox").Text = " arol ";
        UiHarness.Flush();

        Assert.Equal(["Carol"], page.Contacts.Select(static contact => contact.Name));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-11")]
    public async Task ContactList_SearchWithNoMatch_SaysSoAndClearBringsEveryoneBack()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);
        using var bob = KeySet.Create();
        await harness.AddContactAsync(bob, "Bob");

        page.SearchText = "zzz";
        UiHarness.Flush();

        Assert.Empty(page.Contacts);
        Assert.True(UiHarness.Find<TextBlock>(view, "NoMatchesText").IsVisible);
        Assert.Equal(UiText.ContactListNoMatches, UiHarness.Find<TextBlock>(view, "NoMatchesText").Text);
        Assert.False(UiHarness.Find<TextBlock>(view, "EmptyText").IsVisible);
        Assert.True(UiHarness.Find<DockPanel>(view, "SearchPanel").IsVisible);

        page.ClearSearchCommand.Execute(null);
        UiHarness.Flush();

        Assert.Equal(string.Empty, UiHarness.Find<TextBox>(view, "SearchBox").Text);
        Assert.Equal(["Bob"], page.Contacts.Select(static contact => contact.Name));
        Assert.False(UiHarness.Find<TextBlock>(view, "NoMatchesText").IsVisible);
        Assert.False(UiHarness.Find<Button>(view, "ClearSearchButton").IsVisible);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-11")]
    public async Task ContactList_ContactAddedWhileSearching_IsShownOnlyIfItMatches()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);
        using var bob = KeySet.Create();
        using var carol = KeySet.Create();
        using var bobby = KeySet.Create();
        await harness.AddContactAsync(bob, "Bob");
        page.SearchText = "bo";

        await harness.AddContactAsync(carol, "Carol");
        await harness.AddContactAsync(bobby, "Bobby");

        Assert.Equal(["Bob", "Bobby"], page.Contacts.Select(static contact => contact.Name));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task ContactList_ContactNotYetConnected_IsMarkedPendingAndNotOnline()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        using var bob = KeySet.Create();

        await harness.AddContactAsync(bob, "Bob");

        var item = Assert.Single(page.Contacts);
        Assert.True(item.IsPending);
        Assert.False(item.IsOnline);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task ContactList_ContactConnectedNearby_IsMarkedOnlineAndNotPending()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        harness.Show(new ContactListView { DataContext = page });

        var bob = await harness.ConnectPeerAsync("Bob");
        await UiHarness.WaitUntilAsync(() => page.Contacts.Any(static item => item.IsOnline));

        var item = Assert.Single(page.Contacts);
        Assert.False(item.IsPending);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    private static string[] Names(ContactListViewModel page) => [.. page.Chats.Select(static chat => chat.Name)];

    private static TextMessage Text(long sequence, string text)
    {
        Assert.True(TextMessage.TryCreate(MessageId.Create(), sequence, DateTimeOffset.UnixEpoch.AddDays(20000), text, out var message));
        return message;
    }
}
