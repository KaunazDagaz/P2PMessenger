using System.Net;
using P2PMessenger.Application.Background;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Delivery;
using P2PMessenger.Application.Groups;
using P2PMessenger.Application.Tests.Background;
using P2PMessenger.Application.Tests.Delivery;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Tests.Shared;
using static P2PMessenger.Application.Tests.Groups.TestGroups;

namespace P2PMessenger.Application.Tests.Groups;

public sealed class GroupPassOnTests
{
    [Fact]
    [Trait("Story", "US-13")]
    [Trait("Story", "US-21")]
    public async Task SummaryFor_IncludesStoredOwnAndDeletedMessagesMerged_AndEmptyGroupHasNoAuthors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        using var key = GroupKey.Create();
        await AddContactAsync(alice, bob, "Bob", cancellationToken);
        var record = await InvitedByAsync(alice, bob, "Bob", "Field team", key, cancellationToken);
        Assert.True(alice.Groups.TryGet(record.GroupId, out var group));

        var empty = alice.GroupChats.SummaryFor(group);
        Assert.Empty(empty.Authors);
        Assert.Equal(record.GroupId, empty.GroupId);
        Assert.Equal(1, empty.Epoch);

        Assert.NotNull(await alice.GroupChats.ReceiveAsync(bob.IdentityKey, From(bob, record, key, 1, "one"), cancellationToken));
        Assert.NotNull(await alice.GroupChats.ReceiveAsync(bob.IdentityKey, From(bob, record, key, 2, "two"), cancellationToken));
        Assert.True((await alice.SendToGroupAsync(record.GroupId, "mine one", cancellationToken)).IsQueued);
        var dropped = (await alice.SendToGroupAsync(record.GroupId, "mine drop", cancellationToken)).Message!;
        Assert.True(await alice.DeleteWaitingGroupMessageAsync(record.GroupId, dropped.Id, cancellationToken));
        Assert.True((await alice.SendToGroupAsync(record.GroupId, "mine three", cancellationToken)).IsQueued);
        Assert.Equal(4, await alice.DeleteGroupChatAsync(record.GroupId, cancellationToken));
        Assert.NotNull(await alice.GroupChats.ReceiveAsync(bob.IdentityKey, From(bob, record, key, 4, "four"), cancellationToken));

        var summary = alice.GroupChats.SummaryFor(group);

        Assert.True(summary.Contains(alice.Identity.Keys.IdentityKey, 1));
        Assert.True(summary.Contains(alice.Identity.Keys.IdentityKey, 2));
        Assert.True(summary.Contains(alice.Identity.Keys.IdentityKey, 3));
        Assert.True(summary.Contains(bob.IdentityKey, 1));
        Assert.True(summary.Contains(bob.IdentityKey, 2));
        Assert.False(summary.Contains(bob.IdentityKey, 3));
        Assert.True(summary.Contains(bob.IdentityKey, 4));
        alice.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-13")]
    [Trait("Story", "US-21")]
    public async Task SummaryFor_WithDeletedRangeFromNonMemberAuthor_OmitsNonMemberAndKeepsCurrentMembers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seen = new InMemorySeenMessageIds();
        using var groupStore = new InMemoryGroupStore(seen);
        using var aliceIdentity = TestKeys.NewIdentity("Alice");
        using var bob = KeySet.Create();
        using var removed = KeySet.Create();
        using var key = GroupKey.Create();
        GroupMember[] members =
        [
            new(aliceIdentity.Keys.IdentityKey, aliceIdentity.Name),
            new(bob.IdentityKey, TestKeys.Name("Bob")),
        ];
        var record = GroupRecord.Create(GroupId.Create(), 2, TestKeys.Name("Field team"), key, members, bob);
        GroupMemberStatus[] statuses =
        [
            new(aliceIdentity.Keys.IdentityKey, aliceIdentity.Name, MemberJoin.Joined, NoticeState.None, NoticeState.None),
            new(bob.IdentityKey, TestKeys.Name("Bob"), MemberJoin.Joined, NoticeState.None, NoticeState.None),
        ];
        await groupStore.AddGroupAsync(record, key, statuses, cancellationToken);
        AuthorSequenceRange[] deleted =
        [
            .. new AuthorSequenceRange[]
            {
                new(bob.IdentityKey, 1, 2),
                new(removed.IdentityKey, 1, 5),
            }.OrderBy(static range => Convert.ToHexString(range.AuthorKey.Bytes), StringComparer.Ordinal),
        ];
        await groupStore.DeleteChatAsync(record.GroupId, deleted, cancellationToken);

        await using var alice = await Messenger.StartAsync(
            aliceIdentity,
            MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback, ListenPort = 0, SeenMessageIds = seen, GroupStore = groupStore },
            cancellationToken);
        Assert.True(alice.Groups.TryGet(record.GroupId, out var group));

        var summary = alice.GroupChats.SummaryFor(group);

        var onlyAuthor = Assert.Single(summary.Authors);
        Assert.Equal(bob.IdentityKey, onlyAuthor.AuthorKey);
        Assert.True(summary.Contains(bob.IdentityKey, 2));
        Assert.False(summary.Contains(removed.IdentityKey, 1));
    }

    [Fact]
    [Trait("Story", "US-13")]
    public async Task MissingFrom_SkipsPeersOwnMessagesOwnWaitingMessagesAndCoveredSequences()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = await TestMessengers.StartAsync("Bob", cancellationToken);
        using var alice = KeySet.Create();
        using var carol = KeySet.Create();
        using var key = GroupKey.Create();
        await AddContactAsync(bob, alice, "Alice", cancellationToken);
        await AddContactAsync(bob, carol, "Carol", cancellationToken);
        GroupMember[] members =
        [
            new(alice.IdentityKey, TestKeys.Name("Alice")),
            new(bob.Identity.Keys.IdentityKey, bob.Identity.Name),
            new(carol.IdentityKey, TestKeys.Name("Carol")),
        ];
        var record = GroupRecord.Create(GroupId.Create(), 1, TestKeys.Name("Field team"), key, members, alice);
        Assert.True(await bob.Groups.ReceiveInvitationAsync(alice.IdentityKey, GroupInvitation.Create(key, record), cancellationToken));
        Assert.True(await bob.Groups.ReceiveJoinAsync(carol.IdentityKey, GroupJoin.Create(record), cancellationToken));
        Assert.True(bob.Groups.TryGet(record.GroupId, out var group));

        Assert.NotNull(await bob.GroupChats.ReceiveAsync(alice.IdentityKey, From(alice, record, key, 1, "alice 1"), cancellationToken));
        Assert.NotNull(await bob.GroupChats.ReceiveAsync(alice.IdentityKey, From(alice, record, key, 2, "alice 2"), cancellationToken));
        Assert.NotNull(await bob.GroupChats.ReceiveAsync(carol.IdentityKey, From(carol, record, key, 1, "carol 1"), cancellationToken));
        Assert.True((await bob.SendToGroupAsync(record.GroupId, "bob waiting", cancellationToken)).IsQueued);
        var carolSummary = GroupSummary.Create(record.GroupId, 1, [new GroupSummaryAuthor(alice.IdentityKey, [new SequenceRange(1, 1)])]);

        var missing = bob.GroupChats.MissingFrom(group, carol.IdentityKey, carolSummary);

        var only = Assert.Single(missing);
        Assert.Equal("alice 2", only.Text);
        Assert.Equal(alice.IdentityKey, only.AuthorKey);
        bob.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-13")]
    [Trait("Story", "US-19")]
    public async Task PassedOnMessage_TriggersGroupNotificationWithOriginalAuthorAndUnreadCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        var aliceNode = hub.CreateNode();
        var bobNode = hub.CreateNode();
        var carolNode = hub.CreateNode();
        await using var alice = await TestMessengers.StartAsync("Alice", aliceNode, cancellationToken);
        await using var bob = await TestMessengers.StartAsync("Bob", bobNode, cancellationToken);
        await using var carol = await TestMessengers.StartAsync("Carol", carolNode, cancellationToken);
        var groupId = await CreateThreeWayGroupAsync(alice, bob, carol, cancellationToken);
        var notifier = new FakeNotifier();
        var settings = new BackgroundSettings(new MemorySettings());
        using var alerts = new MessageAlerts(carol, notifier, settings, () => false);

        await TakeAwayAsync(carol, carolNode, [alice, bob], cancellationToken);
        Assert.Equal(GroupSendOutcome.Queued, (await alice.SendToGroupAsync(groupId, "Meet at the bridge", cancellationToken)).Outcome);
        await TestMessengers.WaitUntilAsync(() => bob.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);

        await TakeAwayAsync(alice, aliceNode, [bob, carol], cancellationToken);
        carolNode.InRange = true;

        await TestMessengers.WaitUntilAsync(() => carol.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);
        var arrived = Assert.Single(carol.GroupChats.MessagesIn(groupId));
        Assert.Equal("Meet at the bridge", arrived.Text);
        Assert.Equal(alice.Identity.Keys.IdentityKey, arrived.AuthorKey);
        Assert.Equal(1, carol.GroupChats.UnreadCount(groupId));
        var notice = Assert.Single(notifier.ShownGroups);
        Assert.Equal(groupId, notice.GroupId);
        Assert.Equal("Field team", notice.GroupName);
        Assert.Equal("Alice", notice.AuthorName);
        Assert.Null(notice.Preview);
        alice.Identity.Dispose();
        bob.Identity.Dispose();
        carol.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-13")]
    [Trait("Story", "US-21")]
    public async Task PassOn_RaceWithGroupChatDeletion_WaitsForDeliveryTurnAndNeverSendsDeletedMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        var aliceNode = hub.CreateNode();
        var bobNode = hub.CreateNode();
        var carolNode = hub.CreateNode();
        await using var alice = await TestMessengers.StartAsync("Alice", aliceNode, cancellationToken);
        await using var bob = await TestMessengers.StartAsync("Bob", bobNode, cancellationToken);
        await using var carol = await TestMessengers.StartAsync("Carol", carolNode, cancellationToken);
        var groupId = await CreateThreeWayGroupAsync(alice, bob, carol, cancellationToken);

        await TakeAwayAsync(carol, carolNode, [alice, bob], cancellationToken);
        Assert.Equal(GroupSendOutcome.Queued, (await alice.SendToGroupAsync(groupId, "Do not pass after delete", cancellationToken)).Outcome);
        await TestMessengers.WaitUntilAsync(() => bob.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);
        await TakeAwayAsync(alice, aliceNode, [bob, carol], cancellationToken);

        var turn = await bob.Chats.EnterDeliveryAsync(carol.Identity.Keys.IdentityKey, cancellationToken);
        var deleting = bob.DeleteGroupChatAsync(groupId, cancellationToken);
        carolNode.InRange = true;
        await TestMessengers.WaitUntilAsync(() => bob.Connections.IsConnected(carol.Identity.Keys.IdentityKey), cancellationToken);
        await Task.Delay(300, cancellationToken);
        Assert.False(deleting.IsCompleted);
        turn.Dispose();

        Assert.Equal(1, await deleting);
        await Task.Delay(400, cancellationToken);
        Assert.Empty(carol.GroupChats.MessagesIn(groupId));
        alice.Identity.Dispose();
        bob.Identity.Dispose();
        carol.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-04")]
    public async Task PassOn_BatchSizeAndRoundLimit_YieldsToOwnMessagesAndContinuesOnWakeUp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        var aliceNode = hub.CreateNode();
        var bobNode = hub.CreateNode();
        var carolNode = hub.CreateNode();
        var bobWakeUps = new ManualWakeUps();
        await using var alice = await TestMessengers.StartAsync("Alice", aliceNode, cancellationToken);
        await using var bob = await Messenger.StartAsync(
            TestKeys.NewIdentity("Bob"),
            MessengerSetup.InMemory() with
            {
                ListenAddress = IPAddress.Loopback,
                ListenPort = 0,
                Connection = TestMessengers.Fast,
                Discovery = bobNode.Setup,
                WakeUps = bobWakeUps,
                Delivery = new DeliveryOptions { PassOnBatchSize = 3, PassOnRoundLimit = 4 },
            },
            cancellationToken);
        await using var carol = await TestMessengers.StartAsync("Carol", carolNode, cancellationToken);
        var groupId = await CreateThreeWayGroupAsync(alice, bob, carol, cancellationToken);

        await TakeAwayAsync(carol, carolNode, [alice, bob], cancellationToken);
        for (var number = 1; number <= 6; number++)
        {
            Assert.Equal(GroupSendOutcome.Queued, (await alice.SendToGroupAsync(groupId, $"m{number}", cancellationToken)).Outcome);
        }

        await TestMessengers.WaitUntilAsync(() => bob.GroupChats.MessagesIn(groupId).Count == 6, cancellationToken);
        await TakeAwayAsync(alice, aliceNode, [bob, carol], cancellationToken);

        using (await bob.Chats.EnterDeliveryAsync(carol.Identity.Keys.IdentityKey, cancellationToken))
        {
            carolNode.InRange = true;
            await TestMessengers.WaitUntilAsync(() => bob.Connections.IsConnected(carol.Identity.Keys.IdentityKey), cancellationToken);
            await Task.Delay(300, cancellationToken);
        }

        await TestMessengers.WaitUntilAsync(() => carol.GroupChats.MessagesIn(groupId).Count == 4, cancellationToken);
        await Task.Delay(300, cancellationToken);
        Assert.Equal(4, carol.GroupChats.MessagesIn(groupId).Count);

        Assert.Equal(SendOutcome.Queued, (await bob.SendAsync(carol.Identity.Keys.IdentityKey, "Direct priority", cancellationToken)).Outcome);
        await TestMessengers.WaitUntilAsync(() => carol.Chats.MessagesWith(bob.Identity.Keys.IdentityKey).Count == 1, cancellationToken);
        await Task.Delay(300, cancellationToken);
        Assert.Equal(4, carol.GroupChats.MessagesIn(groupId).Count);

        bobWakeUps.Fire();
        await TestMessengers.WaitUntilAsync(() => carol.GroupChats.MessagesIn(groupId).Count == 6, cancellationToken);
        Assert.Equal(["m1", "m2", "m3", "m4", "m5", "m6"], carol.GroupChats.MessagesIn(groupId).Select(static message => message.Text));
        alice.Identity.Dispose();
        bob.Identity.Dispose();
        carol.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-13")]
    [Trait("Story", "US-09")]
    public async Task PassOn_OverTorRoute_ReachesAbsentMemberThroughAnotherMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fastTor = TestMessengers.Fast with
        {
            TorDialTimeout = TimeSpan.FromSeconds(5),
            TorRetryDelayMin = TimeSpan.FromMilliseconds(200),
            TorRetryDelayMax = TimeSpan.FromSeconds(1),
        };
        await using var alice = await Messenger.StartAsync(TestKeys.NewIdentity("Alice"), MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback, ListenPort = 0, Connection = fastTor }, cancellationToken);
        await using var bob = await Messenger.StartAsync(TestKeys.NewIdentity("Bob"), MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback, ListenPort = 0, Connection = fastTor }, cancellationToken);
        await using var carol = await Messenger.StartAsync(TestKeys.NewIdentity("Carol"), MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback, ListenPort = 0, Connection = fastTor }, cancellationToken);
        await using var tor = new FakeTorRoute();
        tor.Join(alice);
        tor.Join(bob);
        tor.Join(carol);
        var groupId = await CreateThreeWayGroupAsync(alice, bob, carol, cancellationToken);

        carol.Connections.UseTorProxy(null);
        tor.Proxy.Answer(carol.Identity.Keys.OnionAddress, 0xF2);
        tor.Proxy.CutAll();
        await TestMessengers.WaitUntilAsync(() => !bob.Connections.IsConnected(carol.Identity.Keys.IdentityKey) && !alice.Connections.IsConnected(carol.Identity.Keys.IdentityKey), cancellationToken);
        await TestMessengers.WaitUntilAsync(() => alice.Connections.IsConnected(bob.Identity.Keys.IdentityKey), cancellationToken);

        Assert.Equal(GroupSendOutcome.Queued, (await alice.SendToGroupAsync(groupId, "Via Bob over Tor", cancellationToken)).Outcome);
        await TestMessengers.WaitUntilAsync(() => bob.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);

        alice.Connections.UseTorProxy(null);
        tor.Proxy.Answer(alice.Identity.Keys.OnionAddress, 0xF2);
        tor.Proxy.CutAll();
        await TestMessengers.WaitUntilAsync(() => !bob.Connections.IsConnected(alice.Identity.Keys.IdentityKey), cancellationToken);

        tor.Join(carol);

        await TestMessengers.WaitUntilAsync(() => carol.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);
        var arrived = Assert.Single(carol.GroupChats.MessagesIn(groupId));
        Assert.Equal("Via Bob over Tor", arrived.Text);
        Assert.Equal(alice.Identity.Keys.IdentityKey, arrived.AuthorKey);
        alice.Identity.Dispose();
        bob.Identity.Dispose();
        carol.Identity.Dispose();
    }

    private static async Task<GroupId> CreateThreeWayGroupAsync(Messenger alice, Messenger bob, Messenger carol, CancellationToken cancellationToken)
    {
        Messenger[] all = [alice, bob, carol];
        for (var first = 0; first < all.Length; first++)
        {
            for (var second = first + 1; second < all.Length; second++)
            {
                Assert.True((await all[first].Contacts.AddAsync(all[first].Contacts.Check(all[second].Identity.CodeText), cancellationToken)).IsSaved);
                Assert.True((await all[second].Contacts.AddAsync(all[second].Contacts.Check(all[first].Identity.CodeText), cancellationToken)).IsSaved);
            }
        }

        await TestMessengers.WaitUntilAsync(
            () => all.All(peer => all.Where(other => other != peer).All(other => peer.Contacts.TryGet(other.Identity.Keys.IdentityKey, out var contact) && contact.IsAdded)),
            cancellationToken);

        var created = await alice.CreateGroupAsync("Field team", [bob.Identity.Keys.IdentityKey, carol.Identity.Keys.IdentityKey], cancellationToken);
        Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
        var groupId = created.Group!.Id;
        await TestMessengers.WaitUntilAsync(
            () => all.All(peer => peer.Groups.TryGet(groupId, out var group) && group.State == GroupState.Created),
            cancellationToken);
        return groupId;
    }

    private static async Task TakeAwayAsync(Messenger leaving, LoopbackDiscovery.Node leavingNode, Messenger[] others, CancellationToken cancellationToken)
    {
        leavingNode.InRange = false;
        var leavingKey = leaving.Identity.Keys.IdentityKey;
        await TestMessengers.WaitUntilAsync(
            () => others.All(other => !other.Connections.IsHeard(leavingKey) && !leaving.Connections.IsHeard(other.Identity.Keys.IdentityKey)),
            cancellationToken);
        foreach (var other in others)
        {
            if (other.Connections.TryGetConnection(leavingKey, out var connection))
            {
                connection.Close();
            }
        }

        await TestMessengers.WaitUntilAsync(
            () => others.All(other => !other.Connections.IsConnected(leavingKey) && !leaving.Connections.IsConnected(other.Identity.Keys.IdentityKey)),
            cancellationToken);
    }
}
