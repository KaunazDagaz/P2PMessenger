using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Groups;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using static P2PMessenger.Application.Tests.Groups.TestGroups;

namespace P2PMessenger.Application.Tests.Groups;

public sealed class GroupDeletionTests
{
    [Fact]
    [Trait("Story", "US-21")]
    public async Task DeleteGroupChat_ReceivedAndOwnMessages_AreRemovedWhileTheGroupStaysAndNewMessagesStillArrive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        using var key = GroupKey.Create();
        await AddContactAsync(alice, bob, "Bob", cancellationToken);
        var record = await InvitedByAsync(alice, bob, "Bob", "Field team", key, cancellationToken);
        Assert.NotNull(await alice.GroupChats.ReceiveAsync(bob.IdentityKey, From(bob, record, key, 1, "one"), cancellationToken));
        Assert.NotNull(await alice.GroupChats.ReceiveAsync(bob.IdentityKey, From(bob, record, key, 2, "two"), cancellationToken));
        Assert.True((await alice.SendToGroupAsync(record.GroupId, "mine", cancellationToken)).IsQueued);
        var changes = new List<GroupChatChangedEventArgs>();
        alice.GroupChats.Changed += (_, e) => changes.Add(e);

        Assert.Equal(3, await alice.DeleteGroupChatAsync(record.GroupId, cancellationToken));

        Assert.Empty(alice.GroupChats.MessagesIn(record.GroupId));
        Assert.Equal(0, alice.GroupChats.UnreadCount(record.GroupId));
        Assert.True(Assert.Single(changes).IsCleared);
        Assert.True(alice.Groups.TryGet(record.GroupId, out var group));
        Assert.Equal(GroupState.Created, group.State);
        Assert.Empty(alice.GroupChats.WaitingFor(bob.IdentityKey));
        Assert.NotNull(await alice.GroupChats.ReceiveAsync(bob.IdentityKey, From(bob, record, key, 3, "three"), cancellationToken));
        Assert.Equal("three", Assert.Single(alice.GroupChats.MessagesIn(record.GroupId)).Text);
        Assert.Equal(1, alice.GroupChats.UnreadCount(record.GroupId));
        var next = (await alice.SendToGroupAsync(record.GroupId, "after", cancellationToken)).Message!;
        Assert.Equal(2, next.Sequence);
        alice.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-09")]
    public async Task DeleteGroupChat_ThenTheSameMessageAgain_IsAcknowledgedButNotShown_AndAnotherWithADeletedNumberIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        using var key = GroupKey.Create();
        await AddContactAsync(alice, bob, "Bob", cancellationToken);
        var record = await InvitedByAsync(alice, bob, "Bob", "Field team", key, cancellationToken);
        var first = From(bob, record, key, 1, "one");
        Assert.Equal(IdOf(first, key), await alice.GroupChats.ReceiveAsync(bob.IdentityKey, first, cancellationToken));
        Assert.Equal(1, await alice.DeleteGroupChatAsync(record.GroupId, cancellationToken));

        Assert.Equal(IdOf(first, key), await alice.GroupChats.ReceiveAsync(bob.IdentityKey, first, cancellationToken));
        Assert.Null(await alice.GroupChats.ReceiveAsync(bob.IdentityKey, From(bob, record, key, 1, "another one"), cancellationToken));

        Assert.Empty(alice.GroupChats.MessagesIn(record.GroupId));
        Assert.Equal(0, alice.GroupChats.UnreadCount(record.GroupId));
        Assert.NotNull(await alice.GroupChats.ReceiveAsync(bob.IdentityKey, From(bob, record, key, 2, "two"), cancellationToken));
        Assert.Equal("two", Assert.Single(alice.GroupChats.MessagesIn(record.GroupId)).Text);
        alice.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-21")]
    public async Task DeleteWaitingGroupMessage_NoMemberHasIt_IsRemovedAndNotWaitingAnyMoreAndTheNumbersGoOn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        using var key = GroupKey.Create();
        await AddContactAsync(alice, bob, "Bob", cancellationToken);
        var record = await InvitedByAsync(alice, bob, "Bob", "Field team", key, cancellationToken);
        var dropped = (await alice.SendToGroupAsync(record.GroupId, "drop", cancellationToken)).Message!;
        var kept = (await alice.SendToGroupAsync(record.GroupId, "keep", cancellationToken)).Message!;
        var changes = new List<GroupChatChangedEventArgs>();
        alice.GroupChats.Changed += (_, e) => changes.Add(e);

        Assert.True(await alice.DeleteWaitingGroupMessageAsync(record.GroupId, dropped.Id, cancellationToken));

        Assert.Equal(dropped.Id, Assert.Single(changes).Removed?.Id);
        Assert.Equal(kept.Id, Assert.Single(alice.GroupChats.MessagesIn(record.GroupId)).Id);
        Assert.Equal([kept.Id], alice.GroupChats.WaitingFor(bob.IdentityKey).Select(static item => item.Id));
        Assert.False(await alice.DeleteWaitingGroupMessageAsync(record.GroupId, dropped.Id, cancellationToken));
        Assert.Equal(3, (await alice.SendToGroupAsync(record.GroupId, "after", cancellationToken)).Message!.Sequence);
        Assert.Equal(2, await alice.DeleteGroupChatAsync(record.GroupId, cancellationToken));
        Assert.Equal(4, (await alice.SendToGroupAsync(record.GroupId, "later", cancellationToken)).Message!.Sequence);
        alice.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-16")]
    public async Task DeleteWaitingGroupMessage_AMemberHasItOrItIsSomeoneElsesOrUnknown_IsNotDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        using var key = GroupKey.Create();
        await AddContactAsync(alice, bob, "Bob", cancellationToken);
        var record = await InvitedByAsync(alice, bob, "Bob", "Field team", key, cancellationToken);
        var own = (await alice.SendToGroupAsync(record.GroupId, "mine", cancellationToken)).Message!;
        Assert.True(own.CanBeDeleted);
        Assert.True(await alice.GroupChats.MarkReceivedAsync(bob.IdentityKey, own.Id, cancellationToken));
        var fromBob = From(bob, record, key, 1, "hello");
        Assert.NotNull(await alice.GroupChats.ReceiveAsync(bob.IdentityKey, fromBob, cancellationToken));

        Assert.False(await alice.DeleteWaitingGroupMessageAsync(record.GroupId, own.Id, cancellationToken));
        Assert.False(await alice.DeleteWaitingGroupMessageAsync(record.GroupId, IdOf(fromBob, key), cancellationToken));
        Assert.False(await alice.DeleteWaitingGroupMessageAsync(record.GroupId, MessageId.Create(), cancellationToken));
        Assert.False(await alice.DeleteWaitingGroupMessageAsync(GroupId.Create(), own.Id, cancellationToken));
        Assert.Equal(0, await alice.DeleteGroupChatAsync(GroupId.Create(), cancellationToken));

        Assert.Equal(2, alice.GroupChats.MessagesIn(record.GroupId).Count);
        Assert.False(alice.GroupChats.MessagesIn(record.GroupId).Single(message => message.Id.Equals(own.Id)).CanBeDeleted);
        Assert.False(alice.GroupChats.MessagesIn(record.GroupId).Single(message => message.Direction == MessageDirection.Incoming).CanBeDeleted);
        alice.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-21")]
    public async Task DeleteGroupChatAndDeleteWaiting_WaitForTheDeliveryTurnOfEveryMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        using var key = GroupKey.Create();
        await AddContactAsync(alice, bob, "Bob", cancellationToken);
        var record = await InvitedByAsync(alice, bob, "Bob", "Field team", key, cancellationToken);
        var first = (await alice.SendToGroupAsync(record.GroupId, "first", cancellationToken)).Message!;
        await alice.SendToGroupAsync(record.GroupId, "second", cancellationToken);

        var turn = await alice.Chats.EnterDeliveryAsync(bob.IdentityKey, cancellationToken);
        var deletingOne = alice.DeleteWaitingGroupMessageAsync(record.GroupId, first.Id, cancellationToken);
        await Task.Delay(200, cancellationToken);
        Assert.False(deletingOne.IsCompleted);
        Assert.Equal(2, alice.GroupChats.MessagesIn(record.GroupId).Count);
        turn.Dispose();
        Assert.True(await deletingOne);

        turn = await alice.Chats.EnterDeliveryAsync(bob.IdentityKey, cancellationToken);
        var deletingAll = alice.DeleteGroupChatAsync(record.GroupId, cancellationToken);
        await Task.Delay(200, cancellationToken);
        Assert.False(deletingAll.IsCompleted);
        turn.Dispose();
        Assert.Equal(1, await deletingAll);
        alice.Identity.Dispose();
    }
}
