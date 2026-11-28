using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application.Groups;
using P2PMessenger.Core.Groups;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class GroupDeletionViewTests
{
    [AvaloniaFact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-16")]
    public async Task GroupConversation_DeleteChat_AsksFirstThenRemovesTheMessagesOnlyOnThisDeviceAndKeepsTheGroup()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var groupId = await CreateGroupAsync(harness, bob);
        var page = harness.Shell.Pages.GroupConversation(groupId);
        var view = new GroupConversationView { DataContext = page };
        harness.Shell.Push(page);
        harness.Show(view);
        page.Draft = "Hi team";
        await page.SendCommand.ExecuteAsync(null);
        await bob.SendToGroupAsync(groupId, "Hi Alice", CancellationToken.None);
        await UiHarness.WaitUntilAsync(() => page.Messages.Count == 2 && page.Messages.Single(static item => item.IsOwn).ReceivedCount == 1);
        foreach (var item in page.Messages)
        {
            item.ToggleDetailsCommand.Execute(null);
            item.AskDeleteCommand.Execute(null);
            Assert.False(item.CanDelete);
            Assert.False(item.IsDeleteVisible);
            Assert.False(item.IsConfirmingDelete);
        }

        Assert.True(UiHarness.Find<Button>(view, "DeleteChatButton").IsVisible);
        Assert.False(UiHarness.Find<Border>(view, "DeleteChatPanel").IsVisible);

        page.AskDeleteChatCommand.Execute(null);
        UiHarness.Flush();

        Assert.True(UiHarness.Find<Border>(view, "DeleteChatPanel").IsVisible);
        Assert.False(UiHarness.Find<Button>(view, "DeleteChatButton").IsVisible);
        Assert.Equal("Delete the chat of Team? Its messages are removed from this device only.", UiHarness.Find<TextBlock>(view, "DeleteChatQuestionText").Text);
        page.CancelDeleteChatCommand.Execute(null);
        UiHarness.Flush();
        Assert.False(UiHarness.Find<Border>(view, "DeleteChatPanel").IsVisible);
        Assert.Equal(2, page.Messages.Count);

        page.AskDeleteChatCommand.Execute(null);
        await page.DeleteChatCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Empty(page.Messages);
        Assert.Empty(harness.Messenger.GroupChats.MessagesIn(groupId));
        Assert.True(UiHarness.Find<TextBlock>(view, "EmptyText").IsVisible);
        Assert.False(UiHarness.Find<Border>(view, "DeleteChatPanel").IsVisible);
        Assert.Contains(UiText.GroupChatDeleted, harness.ToastTexts);
        Assert.True(page.CanWrite);
        Assert.True(harness.Messenger.Groups.TryGet(groupId, out _));
        Assert.Equal(["Hi Alice", "Hi team"], bob.GroupChats.MessagesIn(groupId).Select(static message => message.Text).Order(StringComparer.Ordinal));

        await bob.SendToGroupAsync(groupId, "Still here", CancellationToken.None);
        await UiHarness.WaitUntilAsync(() => page.Messages.Count == 1);
        Assert.Equal("Still here", page.Messages[0].Text);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-21")]
    public async Task GroupConversation_WaitingMessageNoMemberHas_IsDeletedAfterConfirmationAndNeverSent()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var bobKey = bob.Identity.Keys.IdentityKey;
        var groupId = await CreateGroupAsync(harness, bob);
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.WaitUntilAsync(() => !harness.Messenger.Connections.IsConnected(bobKey));
        var page = harness.Shell.Pages.GroupConversation(groupId);
        var view = new GroupConversationView { DataContext = page };
        harness.Shell.Push(page);
        harness.Show(view);
        page.Draft = "Never mind";
        await page.SendCommand.ExecuteAsync(null);
        UiHarness.Flush();
        var item = Assert.Single(page.Messages);
        Assert.Equal(UiText.DeliveryWaiting, item.StateText);
        Assert.True(item.CanDelete);
        Assert.False(item.IsDeleteVisible);

        item.ToggleDetailsCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(item.IsDeleteVisible);
        Assert.Same(item, page.DetailsMessage);
        Assert.True(UiHarness.IsShown(UiHarness.Find<StackPanel>(view, "DeliveryDetailsPanel")));
        Assert.Contains(UiText.MessageDelete, UiHarness.Texts(view));
        item.AskDeleteCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(item.IsConfirmingDelete);
        Assert.False(item.IsDeleteVisible);
        Assert.Same(item, page.DeletingMessage);
        Assert.True(UiHarness.IsShown(UiHarness.Find<StackPanel>(view, "DeleteMessagePanel")));
        Assert.True(UiHarness.IsShown(UiHarness.Find<StackPanel>(view, "DeliveryDetailsPanel")));
        Assert.Contains(UiText.GroupMessageDeleteQuestion, UiHarness.Texts(view));
        item.CancelDeleteCommand.Execute(null);
        Assert.False(item.IsConfirmingDelete);
        item.AskDeleteCommand.Execute(null);

        await item.ConfirmDeleteCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Empty(page.Messages);
        Assert.Empty(harness.Messenger.GroupChats.WaitingFor(bobKey));
        Assert.Contains(UiText.GroupMessageDeleted, harness.ToastTexts);
        Assert.Null(page.ErrorMessage);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task<GroupId> CreateGroupAsync(UiHarness harness, Application.Messenger member)
    {
        var created = await harness.Messenger.CreateGroupAsync("Team", [member.Identity.Keys.IdentityKey], CancellationToken.None);
        Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
        var groupId = created.Group!.Id;
        await UiHarness.WaitUntilAsync(() =>
            harness.Messenger.Groups.TryGet(groupId, out var atAlice) && atAlice.State == GroupState.Created
            && member.Groups.TryGet(groupId, out var atMember) && atMember.State == GroupState.Created);
        return groupId;
    }
}
