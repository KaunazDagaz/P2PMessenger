using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
    public async Task ContactList_NoWiFiAndNoInternet_SaysThatOnePersonCanShareAHotspot()
    {
        await using var harness = await UiHarness.StartAsync("Alice", NetworkSnapshot.None);
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);

        Assert.True(page.HasNoNetwork);
        Assert.True(UiHarness.Find<Border>(view, "NoNetworkPanel").IsVisible);
        Assert.Equal(UiText.ContactListNoNetwork, UiHarness.Find<TextBlock>(view, "NoNetworkText").Text);
        Assert.Contains("one person shares a hotspot", UiText.ContactListNoNetwork, StringComparison.Ordinal);

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

    private static TextMessage Text(long sequence, string text)
    {
        Assert.True(TextMessage.TryCreate(MessageId.Create(), sequence, DateTimeOffset.UnixEpoch.AddDays(20000), text, out var message));
        return message;
    }
}
