using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using P2PMessenger.Application.Groups;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class GroupMembershipViewTests
{
    private static readonly CompositeFormat MembersButtonFormat = CompositeFormat.Parse(UiText.GroupMembersButtonFormat);
    private static readonly CompositeFormat RemoveQuestionFormat = CompositeFormat.Parse(UiText.GroupRemoveMemberQuestionFormat);
    private static readonly CompositeFormat MemberRemovedFormat = CompositeFormat.Parse(UiText.GroupMemberRemovedFormat);
    private static readonly CompositeFormat LeaveAdminQuestionFormat = CompositeFormat.Parse(UiText.GroupLeaveAdminQuestionFormat);
    private static readonly CompositeFormat LeaveQuestionFormat = CompositeFormat.Parse(UiText.GroupLeaveQuestionFormat);
    private static readonly CompositeFormat InactiveDeleteQuestionFormat = CompositeFormat.Parse(UiText.GroupDeleteInactiveChatQuestionFormat);

    [AvaloniaFact]
    [Trait("Story", "US-14")]
    public async Task GroupConversation_AdminMembersPanel_ShowsRemoveAndSuccessorPickerAndBusyBarAndExecutesRemoval()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var carol = await harness.ConnectPeerAsync("Carol");
        var knowEachOther = bob.Contacts.Check(carol.Identity.CodeText);
        Assert.True((await bob.Contacts.AddAsync(knowEachOther, CancellationToken.None)).IsSaved);
        var reverseKnow = carol.Contacts.Check(bob.Identity.CodeText);
        Assert.True((await carol.Contacts.AddAsync(reverseKnow, CancellationToken.None)).IsSaved);
        await UiHarness.WaitUntilAsync(() =>
            bob.Contacts.TryGet(carol.Identity.Keys.IdentityKey, out var bobView) && bobView.IsAdded
            && carol.Contacts.TryGet(bob.Identity.Keys.IdentityKey, out var carolView) && carolView.IsAdded);

        var created = await harness.Messenger.CreateGroupAsync("Field team", [bob.Identity.Keys.IdentityKey, carol.Identity.Keys.IdentityKey], CancellationToken.None);
        Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
        var groupId = created.Group!.Id;
        await UiHarness.WaitUntilAsync(() =>
            harness.Messenger.Groups.TryGet(groupId, out var atAlice) && atAlice.State == GroupState.Created
            && bob.Groups.TryGet(groupId, out var atBob) && atBob.State == GroupState.Created
            && carol.Groups.TryGet(groupId, out var atCarol) && atCarol.State == GroupState.Created);

        var page = harness.Shell.Pages.GroupConversation(groupId);
        var view = new GroupConversationView { DataContext = page };
        harness.Shell.Push(page);
        harness.Show(view);
        UiHarness.Flush();

        Assert.Equal(string.Format(CultureInfo.InvariantCulture, MembersButtonFormat, 3), UiHarness.Find<Button>(view, "MembersButton").Content);
        Assert.False(UiHarness.Find<Border>(view, "MembersPanel").IsVisible);

        page.ToggleMembersCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(UiHarness.Find<Border>(view, "MembersPanel").IsVisible);

        Assert.Equal(3, page.Members.Count);
        var aliceItem = page.Members.Single(static m => m.IsYou);
        var carolItem = page.Members.Single(static m => m.Name == "Carol");
        var bobItem = page.Members.Single(static m => m.Name == "Bob");
        Assert.True(aliceItem.IsAdmin);
        Assert.False(aliceItem.CanRemove);
        Assert.False(aliceItem.IsRemoveButtonVisible);
        Assert.False(bobItem.IsAdmin);
        Assert.True(bobItem.CanRemove);
        Assert.True(carolItem.IsRemoveButtonVisible);

        var adminBadges = UiHarness.Find<ItemsControl>(view, "MembersItems")
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(static tb => tb.Text == UiText.GroupMemberAdmin && tb.IsEffectivelyVisible)
            .ToList();
        Assert.Single(adminBadges);

        var busyBar = UiHarness.Find<ItemsControl>(view, "MembersItems")
            .GetVisualDescendants()
            .OfType<ProgressBar>()
            .Single(bar => bar.Name == "RemoveMemberBusyBar" && ReferenceEquals(bar.DataContext, carolItem));
        Assert.True(busyBar.IsIndeterminate);
        carolItem.IsRemoving = true;
        UiHarness.Flush();
        Assert.True(busyBar.IsVisible);
        carolItem.IsRemoving = false;
        UiHarness.Flush();
        Assert.False(busyBar.IsVisible);

        Assert.True(page.NeedsSuccessor);
        page.AskLeaveCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(UiHarness.Find<Border>(view, "LeaveGroupPanel").IsVisible);
        Assert.True(UiHarness.Find<ComboBox>(view, "SuccessorPicker").IsEffectivelyVisible);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, LeaveAdminQuestionFormat, "Field team"), UiHarness.Find<TextBlock>(view, "LeaveGroupQuestionText").Text);
        Assert.Equal(2, page.SuccessorCandidates.Count);
        Assert.NotNull(page.SelectedSuccessor);
        page.CancelLeaveCommand.Execute(null);
        UiHarness.Flush();

        carolItem.AskRemoveCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(carolItem.IsConfirmingRemove);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, RemoveQuestionFormat, "Carol", "Field team"), carolItem.RemoveQuestion);
        carolItem.CancelRemoveCommand.Execute(null);
        UiHarness.Flush();
        Assert.False(carolItem.IsConfirmingRemove);

        carolItem.AskRemoveCommand.Execute(null);
        await carolItem.ConfirmRemoveCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Equal(string.Format(CultureInfo.InvariantCulture, MemberRemovedFormat, "Carol"), page.MembershipNoticeText);
        Assert.True(UiHarness.Find<Border>(view, "MembershipNoticePanel").IsVisible);
        Assert.Equal(2, page.Members.Count);
        Assert.False(page.NeedsSuccessor);
        Assert.True(page.CanWrite);

        await UiHarness.StopPeerAsync(bob);
        await UiHarness.StopPeerAsync(carol);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-14")]
    public async Task GroupConversation_NonAdminAndInactiveStates_ShowTruthfulTextsAndHideDraftBox()
    {
        await using var aliceHarness = await UiHarness.StartAsync("Alice");
        var bob = await aliceHarness.ConnectPeerAsync("Bob");
        var carol = await aliceHarness.ConnectPeerAsync("Carol");
        Assert.True((await bob.Contacts.AddAsync(bob.Contacts.Check(carol.Identity.CodeText), CancellationToken.None)).IsSaved);
        Assert.True((await carol.Contacts.AddAsync(carol.Contacts.Check(bob.Identity.CodeText), CancellationToken.None)).IsSaved);
        await UiHarness.WaitUntilAsync(() =>
            bob.Contacts.TryGet(carol.Identity.Keys.IdentityKey, out var b) && b.IsAdded
            && carol.Contacts.TryGet(bob.Identity.Keys.IdentityKey, out var c) && c.IsAdded);

        var created = await aliceHarness.Messenger.CreateGroupAsync("Squad", [bob.Identity.Keys.IdentityKey, carol.Identity.Keys.IdentityKey], CancellationToken.None);
        var groupId = created.Group!.Id;
        await UiHarness.WaitUntilAsync(() =>
            aliceHarness.Messenger.Groups.TryGet(groupId, out var g1) && g1.State == GroupState.Created
            && bob.Groups.TryGet(groupId, out var g2) && g2.State == GroupState.Created
            && carol.Groups.TryGet(groupId, out var g3) && g3.State == GroupState.Created);

        var aliceGroupView = aliceHarness.Shell.Pages.GroupConversation(groupId);
        aliceHarness.Shell.Push(aliceGroupView);
        aliceHarness.Show(new GroupConversationView { DataContext = aliceGroupView });
        aliceGroupView.Draft = "Stay safe";
        await aliceGroupView.SendCommand.ExecuteAsync(null);
        await UiHarness.WaitUntilAsync(() => bob.GroupChats.MessagesIn(groupId).Count == 1 && carol.GroupChats.MessagesIn(groupId).Count == 1);

        Assert.Equal(RemoveMemberOutcome.Removed, (await aliceHarness.Messenger.RemoveGroupMemberAsync(groupId, carol.Identity.Keys.IdentityKey, CancellationToken.None)).Outcome);
        await UiHarness.WaitUntilAsync(() => carol.Groups.TryGet(groupId, out var atCarol) && atCarol.State == GroupState.Removed);

        Assert.Equal(LeaveGroupOutcome.Left, await bob.LeaveGroupAsync(groupId, null, CancellationToken.None));
        await UiHarness.WaitUntilAsync(() =>
            aliceHarness.Messenger.Groups.TryGet(groupId, out var atAlice)
            && atAlice.State == GroupState.Ended
            && !aliceGroupView.CanWrite
            && aliceGroupView.EndedOrLeftText == UiText.GroupEndedBanner);

        UiHarness.Flush();
        Assert.False(aliceGroupView.CanWrite);
        Assert.False(aliceGroupView.CanLeave);
        Assert.Equal(UiText.GroupEndedBanner, aliceGroupView.EndedOrLeftText);
        Assert.True(aliceGroupView.IsInactiveGroup);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, InactiveDeleteQuestionFormat, "Squad"), aliceGroupView.DeleteChatQuestion);

        aliceGroupView.AskDeleteChatCommand.Execute(null);
        await aliceGroupView.DeleteChatCommand.ExecuteAsync(null);
        Assert.Equal(UiText.GroupInactiveChatDeleted, aliceGroupView.DeletedText);

        var aliceContacts = aliceHarness.Shell.Pages.ContactList();
        var aliceListView = new ContactListView { DataContext = aliceContacts };
        aliceHarness.Show(aliceListView);
        UiHarness.Flush();
        var endedGroupItem = Assert.Single(aliceContacts.Groups);
        Assert.Equal(UiText.GroupItemEnded, endedGroupItem.StateText);

        await using var bobHarness = await UiHarness.StartAsync("BobUi");
        var friend = await bobHarness.ConnectPeerAsync("Owner");
        var friendGroup = await friend.CreateGroupAsync("Trip", [bobHarness.Messenger.Identity.Keys.IdentityKey], CancellationToken.None);
        var tripId = friendGroup.Group!.Id;
        await UiHarness.WaitUntilAsync(() => bobHarness.Messenger.Groups.TryGet(tripId, out var g) && g.State == GroupState.Created);

        var bobConversation = bobHarness.Shell.Pages.GroupConversation(tripId);
        var bobView = new GroupConversationView { DataContext = bobConversation };
        bobHarness.Shell.Push(bobConversation);
        bobHarness.Show(bobView);
        UiHarness.Flush();

        bobConversation.ToggleMembersCommand.Execute(null);
        UiHarness.Flush();
        Assert.All(bobConversation.Members, static member => Assert.False(member.IsRemoveButtonVisible));
        Assert.False(bobConversation.NeedsSuccessor);
        bobConversation.AskLeaveCommand.Execute(null);
        UiHarness.Flush();
        Assert.False(UiHarness.Find<ComboBox>(bobView, "SuccessorPicker").IsEffectivelyVisible);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, LeaveQuestionFormat, "Trip"), UiHarness.Find<TextBlock>(bobView, "LeaveGroupQuestionText").Text);

        await bobConversation.LeaveGroupCommand.ExecuteAsync(null);
        UiHarness.Flush();
        Assert.False(bobConversation.CanWrite);
        Assert.False(UiHarness.Find<TextBox>(bobView, "DraftBox").IsEffectivelyVisible);
        Assert.Equal(UiText.GroupLeftBanner, UiHarness.Find<TextBlock>(bobView, "EndedOrLeftText").Text);
        Assert.Equal(UiText.GroupItemLeft, Assert.Single(bobHarness.Shell.Pages.ContactList().Groups).StateText);

        await UiHarness.StopPeerAsync(bob);
        await UiHarness.StopPeerAsync(carol);
        await UiHarness.StopPeerAsync(friend);
        aliceHarness.Messenger.Identity.Dispose();
        bobHarness.Messenger.Identity.Dispose();
    }
}
