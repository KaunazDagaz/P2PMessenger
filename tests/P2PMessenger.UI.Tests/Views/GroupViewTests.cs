using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class GroupViewTests
{
    [AvaloniaFact]
    [Trait("Story", "US-12")]
    public async Task NewGroup_WithoutAddedContacts_SaysSoAndCannotCreate()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        await harness.AddContactAsync(bob, "Bob");
        var page = harness.Shell.Pages.NewGroup();
        var view = new NewGroupView { DataContext = page };
        harness.Show(view);
        page.Name = "Team";
        UiHarness.Flush();

        Assert.True(UiHarness.Find<TextBlock>(view, "NoContactsText").IsVisible);
        Assert.False(UiHarness.Find<ItemsControl>(view, "CandidateItems").IsVisible);
        Assert.False(page.CreateCommand.CanExecute(null));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-12")]
    [Trait("Story", "US-11")]
    public async Task NewGroup_MembersWhoKnowEachOther_CreatesTheGroupAndItsConversationSendsAndCountsReceipts()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var carol = await harness.ConnectPeerAsync("Carol");
        await KnowEachOtherAsync(bob, carol);
        var page = harness.Shell.Pages.NewGroup();
        harness.Shell.Push(page);
        var view = new NewGroupView { DataContext = page };
        harness.Show(view);

        Assert.Equal(["Bob", "Carol"], page.Candidates.Select(static candidate => candidate.Name).Order(StringComparer.Ordinal));
        Assert.False(page.CreateCommand.CanExecute(null));
        page.Name = "Field team";
        foreach (var candidate in page.Candidates)
        {
            candidate.IsSelected = true;
        }

        Assert.True(page.CreateCommand.CanExecute(null));
        await page.CreateCommand.ExecuteAsync(null);
        UiHarness.Flush();

        var conversation = Assert.IsType<GroupConversationViewModel>(harness.Shell.CurrentPage);
        Assert.Equal("Field team", conversation.Title);
        var conversationView = new GroupConversationView { DataContext = conversation };
        harness.Show(conversationView);
        await UiHarness.WaitUntilAsync(() => conversation.CanWrite);
        Assert.True(UiHarness.Find<TextBox>(conversationView, "DraftBox").IsEffectivelyVisible);
        Assert.Equal(UiText.GroupCreatedBy, UiHarness.Find<TextBlock>(conversationView, "CreatedByText").Text);
        Assert.Contains("Alice (you)", UiHarness.Find<TextBlock>(conversationView, "MembersText").Text, StringComparison.Ordinal);

        conversation.Draft = "Meet at six";
        await conversation.SendCommand.ExecuteAsync(null);
        await UiHarness.WaitUntilAsync(() => conversation.Messages.Count == 1 && conversation.Messages[0].StateText == "Received by 2 of 2 members' apps");
        conversation.Messages[0].ToggleExplanationCommand.Execute(null);
        Assert.Equal(UiText.GroupDeliveryReceivedExplanation, conversation.Messages[0].StateExplanation);

        Assert.Equal(GroupSendOutcome.Queued, (await bob.SendToGroupAsync(conversation.GroupId, "On my way", CancellationToken.None)).Outcome);
        await UiHarness.WaitUntilAsync(() => conversation.Messages.Count == 2);
        Assert.Equal(["Alice", "Bob"], conversation.Messages.Select(static message => message.Sender));
        Assert.Null(conversation.Messages[1].StateText);
        Assert.Contains("On my way", UiHarness.Texts(conversationView));
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.StopPeerAsync(carol);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-12")]
    public async Task GroupConversation_InviteeWhoLacksAMember_NamesThemWithTheirFingerprintAndOffersNoMessageBox()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var carol = await harness.StartPeerAsync("Carol");
        await KnowEachOtherAsync(bob, carol);
        var created = await bob.CreateGroupAsync("Hikers", [harness.Messenger.Identity.Keys.IdentityKey, carol.Identity.Keys.IdentityKey], CancellationToken.None);
        Assert.True(created.IsCreated);
        await UiHarness.WaitUntilAsync(() => harness.Messenger.Groups.TryGet(created.Group.Id, out _));

        var list = harness.Shell.Pages.ContactList();
        var listView = new ContactListView { DataContext = list };
        harness.Shell.Push(list);
        harness.Show(listView);
        await UiHarness.WaitUntilAsync(() => list.Groups.Count == 1);
        Assert.True(UiHarness.Find<TextBlock>(listView, "GroupsHeading").IsVisible);
        Assert.Equal(UiText.GroupItemJoining, list.Groups[0].StateText);

        list.Groups[0].OpenCommand.Execute(null);
        UiHarness.Flush();
        var page = Assert.IsType<GroupConversationViewModel>(harness.Shell.CurrentPage);
        var view = new GroupConversationView { DataContext = page };
        harness.Show(view);

        Assert.True(UiHarness.Find<Border>(view, "JoiningPanel").IsVisible);
        var toAdd = Assert.Single(page.MembersToAdd);
        Assert.Equal("Carol", toAdd.Name);
        Assert.Equal(carol.Identity.Fingerprint.ToGroupedString(), toAdd.Fingerprint);
        Assert.Equal("Created by Bob.", UiHarness.Find<TextBlock>(view, "CreatedByText").Text);
        Assert.False(UiHarness.Find<TextBox>(view, "DraftBox").IsEffectivelyVisible);
        Assert.False(page.SendCommand.CanExecute(null));
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.StopPeerAsync(carol);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-12")]
    public async Task GroupConversation_CreatorWaitingForAMember_ListsWhoHasNotJoined()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var carol = await harness.ConnectPeerAsync("Carol");
        var created = await harness.Messenger.CreateGroupAsync("Team", [bob.Identity.Keys.IdentityKey, carol.Identity.Keys.IdentityKey], CancellationToken.None);
        Assert.True(created.IsCreated);
        var page = harness.Shell.Pages.GroupConversation(created.Group.Id);
        harness.Shell.Push(page);
        var view = new GroupConversationView { DataContext = page };
        harness.Show(view);

        Assert.True(UiHarness.Find<Border>(view, "WaitingPanel").IsVisible);
        Assert.Contains("Bob", page.WaitingText, StringComparison.Ordinal);
        Assert.Contains("Carol", page.WaitingText, StringComparison.Ordinal);
        Assert.False(UiHarness.Find<Border>(view, "JoiningPanel").IsVisible);
        Assert.False(page.CanWrite);
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.StopPeerAsync(carol);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-12")]
    public async Task NewGroup_NameWithAControlCharacter_ShowsTheNameError()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var page = harness.Shell.Pages.NewGroup();
        harness.Shell.Push(page);
        var view = new NewGroupView { DataContext = page };
        harness.Show(view);
        page.Name = "Team\u0007";
        page.Candidates[0].IsSelected = true;

        await page.CreateCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Same(page, harness.Shell.CurrentPage);
        Assert.Equal(UiText.NewGroupInvalidName, UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.Empty(harness.Messenger.Groups.All);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task KnowEachOtherAsync(Messenger first, Messenger second)
    {
        Assert.True((await first.Contacts.AddAsync(first.Contacts.Check(second.Identity.CodeText), CancellationToken.None)).IsSaved);
        Assert.True((await second.Contacts.AddAsync(second.Contacts.Check(first.Identity.CodeText), CancellationToken.None)).IsSaved);
        await UiHarness.WaitUntilAsync(() =>
            first.Contacts.TryGet(second.Identity.Keys.IdentityKey, out Contact? atFirst) && atFirst.IsAdded
            && second.Contacts.TryGet(first.Identity.Keys.IdentityKey, out Contact? atSecond) && atSecond.IsAdded);
    }
}
