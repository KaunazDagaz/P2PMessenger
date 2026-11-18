using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
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
    [Trait("Story", "US-07")]
    public async Task ContactList_ConnectionComesAndGoes_ShowsWhetherTheContactIsConnectedNow()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);

        var bob = await harness.ConnectPeerAsync("Bob");
        await UiHarness.WaitUntilAsync(() => UiHarness.Texts(view).Contains(UiText.ContactConnected));

        await UiHarness.StopPeerAsync(bob);
        await UiHarness.WaitUntilAsync(() => UiHarness.Texts(view).Contains(UiText.ContactNotConnected));
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
