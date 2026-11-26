using System.Net;
using Microsoft.Data.Sqlite;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Delivery;
using P2PMessenger.Application.Groups;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Application.Tests.Delivery;
using P2PMessenger.Application.Tests.LocalData;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Tests.Shared;
using static P2PMessenger.Application.Tests.Groups.TestGroups;

namespace P2PMessenger.Application.Tests.Groups;

public sealed class GroupAcknowledgementTests
{
    [Fact]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-07")]
    public async Task ReceiveAcknowledgement_OwnMessageDirectAndRelayed_MarksMemberReceivedAndRepliesOnlyWhenRelayed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        using var carol = KeySet.Create();
        using var key = GroupKey.Create();
        var record = await CreateThreeMemberGroupAsync(alice, bob, "Bob", carol, "Carol", key, cancellationToken);

        var sent = (await alice.SendToGroupAsync(record.GroupId, "Meet at six", cancellationToken)).Message!;
        var changes = new List<GroupChatChangedEventArgs>();
        alice.GroupChats.Changed += (_, e) => changes.Add(e);

        var bobAck = GroupAcknowledgement.Create(record.GroupId, record.Epoch, sent.Id, bob);
        var direct = await alice.GroupChats.ReceiveAcknowledgementAsync(bob.IdentityKey, bobAck, cancellationToken);
        Assert.False(direct.ReplyWithNoticeAck);
        Assert.True(direct.MarksSenderHasMessage);

        var afterBob = Assert.Single(alice.GroupChats.MessagesIn(record.GroupId));
        Assert.Equal(1, afterBob.ReceivedCount);
        Assert.Equal(DeliveryState.Received, afterBob.Deliveries[bob.IdentityKey]);
        Assert.Equal(DeliveryState.Waiting, afterBob.Deliveries[carol.IdentityKey]);
        Assert.Single(changes);

        var carolAck = GroupAcknowledgement.Create(record.GroupId, record.Epoch, sent.Id, carol);
        var relayed = await alice.GroupChats.ReceiveAcknowledgementAsync(bob.IdentityKey, carolAck, cancellationToken);
        Assert.True(relayed.ReplyWithNoticeAck);
        Assert.False(relayed.MarksSenderHasMessage);

        var afterCarol = Assert.Single(alice.GroupChats.MessagesIn(record.GroupId));
        Assert.Equal(2, afterCarol.ReceivedCount);
        Assert.Equal(DeliveryState.Received, afterCarol.Deliveries[bob.IdentityKey]);
        Assert.Equal(DeliveryState.Received, afterCarol.Deliveries[carol.IdentityKey]);
        Assert.Equal(2, changes.Count);

        var duplicateRelayed = await alice.GroupChats.ReceiveAcknowledgementAsync(bob.IdentityKey, carolAck, cancellationToken);
        Assert.True(duplicateRelayed.ReplyWithNoticeAck);
        Assert.False(duplicateRelayed.MarksSenderHasMessage);
        Assert.Equal(2, changes.Count);
        alice.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-07")]
    [Trait("NFR", "NFR-09")]
    public async Task ReceiveAcknowledgement_ForgedOrMismatched_IsDiscardedWithoutUpdatingDeliveryOrReplying()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        using var carol = KeySet.Create();
        using var dave = KeySet.Create();
        using var key = GroupKey.Create();
        var record = await CreateThreeMemberGroupAsync(alice, bob, "Bob", carol, "Carol", key, cancellationToken);

        var sent = (await alice.SendToGroupAsync(record.GroupId, "Check signature", cancellationToken)).Message!;
        var carolValid = GroupAcknowledgement.Create(record.GroupId, record.Epoch, sent.Id, carol);

        var tamperedBytes = carolValid.ToBytes();
        tamperedBytes[^1] ^= 1;
        Assert.True(GroupAcknowledgement.TryParse(tamperedBytes, out var tampered));

        var wrongSignerBytes = GroupAcknowledgement.Create(record.GroupId, record.Epoch, sent.Id, bob).ToBytes();
        carol.IdentityKey.Bytes.CopyTo(wrongSignerBytes.AsSpan(GroupId.Length + sizeof(uint) + MessageId.Length));
        Assert.True(GroupAcknowledgement.TryParse(wrongSignerBytes, out var wrongSigner));

        var cases = new (Ed25519PublicKey Sender, GroupAcknowledgement Ack)[]
        {
            (bob.IdentityKey, tampered),
            (bob.IdentityKey, wrongSigner),
            (bob.IdentityKey, GroupAcknowledgement.Create(record.GroupId, record.Epoch, sent.Id, dave)),
            (bob.IdentityKey, GroupAcknowledgement.Create(GroupId.Create(), record.Epoch, sent.Id, carol)),
            (bob.IdentityKey, GroupAcknowledgement.Create(record.GroupId, record.Epoch + 1, sent.Id, carol)),
            (bob.IdentityKey, GroupAcknowledgement.Create(record.GroupId, record.Epoch, MessageId.Create(), carol)),
            (bob.IdentityKey, GroupAcknowledgement.Create(record.GroupId, record.Epoch, sent.Id, alice.Identity.Keys)),
            (dave.IdentityKey, carolValid),
        };

        foreach ((var sender, var candidate) in cases)
        {
            var result = await alice.GroupChats.ReceiveAcknowledgementAsync(sender, candidate, cancellationToken);
            Assert.False(result.ReplyWithNoticeAck);
            Assert.False(result.MarksSenderHasMessage);
        }

        var unchanged = Assert.Single(alice.GroupChats.MessagesIn(record.GroupId));
        Assert.Equal(0, unchanged.ReceivedCount);
        Assert.Equal(DeliveryState.Waiting, unchanged.Deliveries[bob.IdentityKey]);
        Assert.Equal(DeliveryState.Waiting, unchanged.Deliveries[carol.IdentityKey]);
        alice.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-09")]
    public async Task ReceiveAcknowledgement_IncomingMessageFromAnotherMember_StoresDeduplicatesAndDeletesOnAuthorConfirmation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = await TestMessengers.StartAsync("Bob", cancellationToken);
        using var alice = KeySet.Create();
        using var carol = KeySet.Create();
        using var dave = KeySet.Create();
        using var key = GroupKey.Create();
        var record = await CreateFourMemberGroupAsync(bob, alice, "Alice", carol, "Carol", dave, "Dave", key, cancellationToken);

        var fromAlice = From(alice, record, key, 1, "Pass my ack back");
        var messageId = await bob.GroupChats.ReceiveAsync(alice.IdentityKey, fromAlice, cancellationToken);
        Assert.NotNull(messageId);

        var arrived = new List<GroupAcknowledgementArrivedEventArgs>();
        bob.GroupChats.AcknowledgementArrived += (_, e) => arrived.Add(e);

        var carolAck = GroupAcknowledgement.Create(record.GroupId, record.Epoch, messageId, carol);
        var direct = await bob.GroupChats.ReceiveAcknowledgementAsync(carol.IdentityKey, carolAck, cancellationToken);
        Assert.True(direct.ReplyWithNoticeAck);
        Assert.True(direct.MarksSenderHasMessage);
        Assert.Single(arrived);
        Assert.Single(bob.GroupChats.RelayedAcknowledgementsForAuthor(alice.IdentityKey));
        Assert.Single(bob.GroupChats.RelayedAcknowledgementsIn(record.GroupId));
        Assert.Equal([alice.IdentityKey], bob.GroupChats.AuthorsWithRelayedAcknowledgements());
        Assert.True(bob.GroupChats.TryGetRelayedAcknowledgement(carolAck.NoticeId, out var stored));
        Assert.Equal(carolAck, stored.Acknowledgement);

        var duplicateDirect = await bob.GroupChats.ReceiveAcknowledgementAsync(carol.IdentityKey, carolAck, cancellationToken);
        Assert.True(duplicateDirect.ReplyWithNoticeAck);
        Assert.True(duplicateDirect.MarksSenderHasMessage);
        Assert.Single(arrived);

        var duplicateRelayed = await bob.GroupChats.ReceiveAcknowledgementAsync(dave.IdentityKey, carolAck, cancellationToken);
        Assert.True(duplicateRelayed.ReplyWithNoticeAck);
        Assert.False(duplicateRelayed.MarksSenderHasMessage);
        Assert.Single(arrived);

        Assert.Equal(
            RelayedAckConfirmation.ConfirmedByPeer,
            await bob.GroupChats.MarkRelayedAcknowledgementConfirmedAsync(dave.IdentityKey, carolAck.NoticeId, cancellationToken));
        Assert.Single(bob.GroupChats.RelayedAcknowledgementsForAuthor(alice.IdentityKey));

        Assert.Equal(
            RelayedAckConfirmation.None,
            await bob.GroupChats.MarkRelayedAcknowledgementConfirmedAsync(alice.IdentityKey, MessageId.Create(), cancellationToken));

        Assert.Equal(
            RelayedAckConfirmation.ConfirmedByAuthor,
            await bob.GroupChats.MarkRelayedAcknowledgementConfirmedAsync(alice.IdentityKey, carolAck.NoticeId, cancellationToken));
        Assert.Empty(bob.GroupChats.RelayedAcknowledgementsForAuthor(alice.IdentityKey));
        Assert.Empty(bob.GroupChats.RelayedAcknowledgementsIn(record.GroupId));
        Assert.Empty(bob.GroupChats.AuthorsWithRelayedAcknowledgements());
        Assert.False(bob.GroupChats.TryGetRelayedAcknowledgement(carolAck.NoticeId, out _));
        bob.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-09")]
    public async Task ReceiveAcknowledgement_IncomingMessage_RefusesForgedSelfAuthorOwnRecipientAndUnknownMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = await TestMessengers.StartAsync("Bob", cancellationToken);
        using var alice = KeySet.Create();
        using var carol = KeySet.Create();
        using var key = GroupKey.Create();
        var record = await CreateThreeMemberGroupAsync(bob, alice, "Alice", carol, "Carol", key, cancellationToken);

        var fromAlice = From(alice, record, key, 1, "Anchored");
        var messageId = await bob.GroupChats.ReceiveAsync(alice.IdentityKey, fromAlice, cancellationToken);
        Assert.NotNull(messageId);

        var forgedBytes = GroupAcknowledgement.Create(record.GroupId, record.Epoch, messageId, carol).ToBytes();
        forgedBytes[^1] ^= 1;
        Assert.True(GroupAcknowledgement.TryParse(forgedBytes, out var forged));

        GroupAcknowledgement[] invalid =
        [
            forged,
            GroupAcknowledgement.Create(record.GroupId, record.Epoch, messageId, alice),
            GroupAcknowledgement.Create(record.GroupId, record.Epoch, messageId, bob.Identity.Keys),
            GroupAcknowledgement.Create(record.GroupId, record.Epoch, MessageId.Create(), carol),
            GroupAcknowledgement.Create(record.GroupId, record.Epoch + 1, messageId, carol),
        ];

        foreach (var candidate in invalid)
        {
            var result = await bob.GroupChats.ReceiveAcknowledgementAsync(carol.IdentityKey, candidate, cancellationToken);
            Assert.False(result.ReplyWithNoticeAck);
            Assert.False(result.MarksSenderHasMessage);
        }

        Assert.Empty(bob.GroupChats.RelayedAcknowledgementsIn(record.GroupId));
        bob.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-09")]
    public async Task ReceiveAcknowledgement_MessageFromDeletedChat_ConfirmsValidRelayedAckWithoutStoringAndRefusesForged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        using var carol = KeySet.Create();
        using var key = GroupKey.Create();
        var record = await CreateThreeMemberGroupAsync(alice, bob, "Bob", carol, "Carol", key, cancellationToken);

        var sent = (await alice.SendToGroupAsync(record.GroupId, "Will delete", cancellationToken)).Message!;
        Assert.Equal(1, await alice.DeleteGroupChatAsync(record.GroupId, cancellationToken));

        var carolAck = GroupAcknowledgement.Create(record.GroupId, record.Epoch, sent.Id, carol);
        var forgedBytes = carolAck.ToBytes();
        forgedBytes[^1] ^= 1;
        Assert.True(GroupAcknowledgement.TryParse(forgedBytes, out var forged));

        var forgedResult = await alice.GroupChats.ReceiveAcknowledgementAsync(bob.IdentityKey, forged, cancellationToken);
        Assert.False(forgedResult.ReplyWithNoticeAck);
        Assert.False(forgedResult.MarksSenderHasMessage);

        var validResult = await alice.GroupChats.ReceiveAcknowledgementAsync(bob.IdentityKey, carolAck, cancellationToken);
        Assert.True(validResult.ReplyWithNoticeAck);
        Assert.False(validResult.MarksSenderHasMessage);
        Assert.Empty(alice.GroupChats.RelayedAcknowledgementsIn(record.GroupId));
        Assert.Empty(alice.GroupChats.MessagesIn(record.GroupId));
        alice.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-06")]
    public async Task DatabaseLocalData_RelayedAcknowledgement_SurvivesRestartClearsOnDeleteAndDamagedRowReportsDamaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new LocalDataFolder();
        var protector = new TestKeyProtector();
        using var alice = KeySet.Create();
        using var carol = KeySet.Create();
        using var key = GroupKey.Create();

        GroupId groupId;
        GroupAcknowledgement carolAck;
        await using (var localData = new DatabaseLocalData(folder.DatabasePath, protector))
        {
            var created = await localData.CreateAsync(null, cancellationToken);
            Assert.True(created.IsOpen);
            var bobIdentity = (await new IdentityService(created.Stores.Identity).CreateAsync("Bob", cancellationToken)).Identity!;
            using (bobIdentity)
            {
                await created.Stores.Contacts.SaveAsync(new Contact(TestKeys.Code(alice, "Alice"), ContactState.Added, KeyComparison.CheckedByYou), cancellationToken);
                await created.Stores.Contacts.SaveAsync(new Contact(TestKeys.Code(carol, "Carol"), ContactState.Added, KeyComparison.CheckedByYou), cancellationToken);

                GroupMember[] members =
                [
                    new(alice.IdentityKey, TestKeys.Name("Alice")),
                    new(bobIdentity.Keys.IdentityKey, bobIdentity.Name),
                    new(carol.IdentityKey, TestKeys.Name("Carol")),
                ];
                var record = GroupRecord.Create(GroupId.Create(), 1, TestKeys.Name("Field team"), key, members, alice);
                groupId = record.GroupId;
                GroupMemberStatus[] statuses =
                [
                    .. record.Members.Select(static member => new GroupMemberStatus(member.IdentityKey, member.Name, MemberJoin.Joined, NoticeState.None, NoticeState.None)),
                ];
                await created.Stores.Groups.AddGroupAsync(record, key, statuses, cancellationToken);

                var text = Text(1, "Relayed across restart");
                var envelope = GroupMessage.Seal(key, record.GroupId, record.Epoch, alice, text);
                var message = new GroupChatMessage(envelope, text.Id, 1, text.WrittenAt, text.Text, DateTimeOffset.UnixEpoch, new Dictionary<Ed25519PublicKey, DeliveryState>());
                Assert.True(await created.Stores.Groups.AddMessageAsync(message, cancellationToken));

                carolAck = GroupAcknowledgement.Create(record.GroupId, record.Epoch, text.Id, carol);
                Assert.True(await created.Stores.Groups.AddAcknowledgementAsync(carolAck, cancellationToken));
            }
        }

        await using (var reopened = new DatabaseLocalData(folder.DatabasePath, protector))
        {
            var open = await reopened.OpenAsync(cancellationToken);
            Assert.True(open.IsOpen);
            var loaded = Assert.Single(await open.Stores.Groups.LoadAcknowledgementsAsync(cancellationToken));
            Assert.Equal(carolAck, loaded);
        }

        await ExecuteAsync(
            folder.DatabasePath,
            "UPDATE group_acknowledgement SET ack_enc = zeroblob(length(ack_enc));",
            cancellationToken);

        await using (var damaged = new DatabaseLocalData(folder.DatabasePath, protector))
        {
            var open = await damaged.OpenAsync(cancellationToken);
            Assert.False(open.IsOpen);
            Assert.Equal(LocalDataProblem.Damaged, open.Problem);
        }

        using var cleanFolder = new LocalDataFolder();
        var cleanProtector = new TestKeyProtector();
        await using (var localData = new DatabaseLocalData(cleanFolder.DatabasePath, cleanProtector))
        {
            var created = await localData.CreateAsync(null, cancellationToken);
            Assert.True(created.IsOpen);
            var bobIdentity = (await new IdentityService(created.Stores.Identity).CreateAsync("Bob", cancellationToken)).Identity!;
            await using var bob = await Messenger.StartAsync(
                bobIdentity,
                MessengerSetup.InMemory() with
                {
                    ListenAddress = IPAddress.Loopback,
                    ListenPort = 0,
                    ContactStore = created.Stores.Contacts,
                    ChatStore = created.Stores.Chats,
                    SeenMessageIds = created.Stores.SeenMessageIds,
                    GroupStore = created.Stores.Groups,
                },
                cancellationToken);
            await AddContactAsync(bob, alice, "Alice", cancellationToken);
            await AddContactAsync(bob, carol, "Carol", cancellationToken);
            GroupMember[] members =
            [
                new(alice.IdentityKey, TestKeys.Name("Alice")),
                new(bob.Identity.Keys.IdentityKey, bob.Identity.Name),
                new(carol.IdentityKey, TestKeys.Name("Carol")),
            ];
            var record = GroupRecord.Create(groupId, 1, TestKeys.Name("Field team"), key, members, alice);
            Assert.True(await bob.Groups.ReceiveInvitationAsync(alice.IdentityKey, GroupInvitation.Create(key, record), cancellationToken));
            Assert.True(await bob.Groups.ReceiveJoinAsync(carol.IdentityKey, GroupJoin.Create(record), cancellationToken));
            var fromAlice = From(alice, record, key, 1, "Will clear");
            var msgId = await bob.GroupChats.ReceiveAsync(alice.IdentityKey, fromAlice, cancellationToken);
            Assert.NotNull(msgId);
            var ack = GroupAcknowledgement.Create(record.GroupId, record.Epoch, msgId, carol);
            await bob.GroupChats.ReceiveAcknowledgementAsync(carol.IdentityKey, ack, cancellationToken);
            Assert.Single(bob.GroupChats.RelayedAcknowledgementsForAuthor(alice.IdentityKey));

            Assert.Equal(1, await bob.DeleteGroupChatAsync(record.GroupId, cancellationToken));
            Assert.Empty(bob.GroupChats.RelayedAcknowledgementsForAuthor(alice.IdentityKey));
            Assert.Empty(await created.Stores.Groups.LoadAcknowledgementsAsync(cancellationToken));
            bob.Identity.Dispose();
        }
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-04")]
    public async Task Outbox_RelayedAcknowledgementsForAuthor_RespectsRoundLimitYieldsToOwnMessagesAndContinuesOnWakeUp()
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
                Delivery = new DeliveryOptions { PassOnBatchSize = 2, PassOnRoundLimit = 3 },
            },
            cancellationToken);
        await using var carol = await TestMessengers.StartAsync("Carol", carolNode, cancellationToken);
        var groupId = await CreateConnectedThreeWayGroupAsync(alice, bob, carol, cancellationToken);

        await TakeAwayAsync(carol, carolNode, [alice, bob], cancellationToken);
        for (var number = 1; number <= 5; number++)
        {
            Assert.Equal(GroupSendOutcome.Queued, (await alice.SendToGroupAsync(groupId, $"m{number}", cancellationToken)).Outcome);
        }

        await TestMessengers.WaitUntilAsync(() => bob.GroupChats.MessagesIn(groupId).Count == 5, cancellationToken);
        await TakeAwayAsync(alice, aliceNode, [bob, carol], cancellationToken);

        carolNode.InRange = true;
        await TestMessengers.WaitUntilAsync(() => carol.GroupChats.MessagesIn(groupId).Count == 3, cancellationToken);
        bobWakeUps.Fire();
        await TestMessengers.WaitUntilAsync(
            () => carol.GroupChats.MessagesIn(groupId).Count == 5
                && bob.GroupChats.RelayedAcknowledgementsForAuthor(alice.Identity.Keys.IdentityKey).Count == 5,
            cancellationToken);
        await TakeAwayAsync(carol, carolNode, [alice, bob], cancellationToken);

        using (await bob.Chats.EnterDeliveryAsync(alice.Identity.Keys.IdentityKey, cancellationToken))
        {
            aliceNode.InRange = true;
            await TestMessengers.WaitUntilAsync(() => bob.Connections.IsConnected(alice.Identity.Keys.IdentityKey), cancellationToken);
            await Task.Delay(300, cancellationToken);
        }

        await TestMessengers.WaitUntilAsync(
            () => alice.GroupChats.MessagesIn(groupId).Count(message => message.Deliveries[carol.Identity.Keys.IdentityKey] == DeliveryState.Received) == 3,
            cancellationToken);
        await Task.Delay(300, cancellationToken);
        Assert.Equal(3, alice.GroupChats.MessagesIn(groupId).Count(message => message.Deliveries[carol.Identity.Keys.IdentityKey] == DeliveryState.Received));

        Assert.Equal(SendOutcome.Queued, (await bob.SendAsync(alice.Identity.Keys.IdentityKey, "Direct priority", cancellationToken)).Outcome);
        await TestMessengers.WaitUntilAsync(() => alice.Chats.MessagesWith(bob.Identity.Keys.IdentityKey).Count == 1, cancellationToken);
        await Task.Delay(300, cancellationToken);
        Assert.Equal(3, alice.GroupChats.MessagesIn(groupId).Count(message => message.Deliveries[carol.Identity.Keys.IdentityKey] == DeliveryState.Received));

        bobWakeUps.Fire();
        await TestMessengers.WaitUntilAsync(
            () => alice.GroupChats.MessagesIn(groupId).All(message => message.Deliveries[carol.Identity.Keys.IdentityKey] == DeliveryState.Received)
                && bob.GroupChats.RelayedAcknowledgementsForAuthor(alice.Identity.Keys.IdentityKey).Count == 0,
            cancellationToken);

        alice.Identity.Dispose();
        bob.Identity.Dispose();
        carol.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-07")]
    public async Task FullyConnectedThreeMemberGroup_StoresZeroRelayedAcknowledgements()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        var aliceNode = hub.CreateNode();
        var bobNode = hub.CreateNode();
        var carolNode = hub.CreateNode();
        await using var alice = await TestMessengers.StartAsync("Alice", aliceNode, cancellationToken);
        await using var bob = await TestMessengers.StartAsync("Bob", bobNode, cancellationToken);
        await using var carol = await TestMessengers.StartAsync("Carol", carolNode, cancellationToken);
        var groupId = await CreateConnectedThreeWayGroupAsync(alice, bob, carol, cancellationToken);

        Assert.Equal(GroupSendOutcome.Queued, (await alice.SendToGroupAsync(groupId, "Direct to all", cancellationToken)).Outcome);
        await TestMessengers.WaitUntilAsync(
            () => alice.GroupChats.MessagesIn(groupId).Count == 1
                && alice.GroupChats.MessagesIn(groupId)[0].ReceivedCount == 2
                && bob.GroupChats.MessagesIn(groupId).Count == 1
                && carol.GroupChats.MessagesIn(groupId).Count == 1,
            cancellationToken);
        await Task.Delay(300, cancellationToken);

        Assert.Empty(alice.GroupChats.RelayedAcknowledgementsIn(groupId));
        Assert.Empty(bob.GroupChats.RelayedAcknowledgementsIn(groupId));
        Assert.Empty(carol.GroupChats.RelayedAcknowledgementsIn(groupId));
        alice.Identity.Dispose();
        bob.Identity.Dispose();
        carol.Identity.Dispose();
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-17")]
    [Trait("Story", "US-09")]
    public async Task RelayedAcknowledgement_OverTorRoute_MarksAbsentMemberReceived()
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
        var groupId = await CreateConnectedThreeWayGroupAsync(alice, bob, carol, cancellationToken);

        carol.Connections.UseTorProxy(null);
        tor.Proxy.Answer(carol.Identity.Keys.OnionAddress, 0xF2);
        tor.Proxy.CutAll();
        await TestMessengers.WaitUntilAsync(() => !alice.Connections.IsConnected(carol.Identity.Keys.IdentityKey) && !bob.Connections.IsConnected(carol.Identity.Keys.IdentityKey), cancellationToken);
        await TestMessengers.WaitUntilAsync(() => alice.Connections.IsConnected(bob.Identity.Keys.IdentityKey), cancellationToken);

        Assert.Equal(GroupSendOutcome.Queued, (await alice.SendToGroupAsync(groupId, "Ack over Tor", cancellationToken)).Outcome);
        await TestMessengers.WaitUntilAsync(() => bob.GroupChats.MessagesIn(groupId).Count == 1 && alice.GroupChats.MessagesIn(groupId)[0].ReceivedCount == 1, cancellationToken);

        alice.Connections.UseTorProxy(null);
        tor.Proxy.Answer(alice.Identity.Keys.OnionAddress, 0xF2);
        tor.Proxy.CutAll();
        await TestMessengers.WaitUntilAsync(() => !bob.Connections.IsConnected(alice.Identity.Keys.IdentityKey), cancellationToken);

        tor.Join(carol);
        await TestMessengers.WaitUntilAsync(() => carol.GroupChats.MessagesIn(groupId).Count == 1 && bob.GroupChats.RelayedAcknowledgementsForAuthor(alice.Identity.Keys.IdentityKey).Count == 1, cancellationToken);

        carol.Connections.UseTorProxy(null);
        tor.Proxy.Answer(carol.Identity.Keys.OnionAddress, 0xF2);
        tor.Proxy.CutAll();
        await TestMessengers.WaitUntilAsync(() => !bob.Connections.IsConnected(carol.Identity.Keys.IdentityKey), cancellationToken);

        tor.Join(alice);
        await TestMessengers.WaitUntilAsync(() => alice.GroupChats.MessagesIn(groupId)[0].ReceivedCount == 2 && bob.GroupChats.RelayedAcknowledgementsForAuthor(alice.Identity.Keys.IdentityKey).Count == 0, cancellationToken);
        Assert.Equal(DeliveryState.Received, alice.GroupChats.MessagesIn(groupId)[0].Deliveries[carol.Identity.Keys.IdentityKey]);
        alice.Identity.Dispose();
        bob.Identity.Dispose();
        carol.Identity.Dispose();
    }

    private static async Task<GroupRecord> CreateThreeMemberGroupAsync(
        Messenger self,
        KeySet first,
        string firstName,
        KeySet second,
        string secondName,
        GroupKey key,
        CancellationToken cancellationToken)
    {
        await AddContactAsync(self, first, firstName, cancellationToken);
        await AddContactAsync(self, second, secondName, cancellationToken);
        GroupMember[] members =
        [
            new(self.Identity.Keys.IdentityKey, self.Identity.Name),
            new(first.IdentityKey, TestKeys.Name(firstName)),
            new(second.IdentityKey, TestKeys.Name(secondName)),
        ];
        var record = GroupRecord.Create(GroupId.Create(), 1, TestKeys.Name("Field team"), key, members, first);
        Assert.True(await self.Groups.ReceiveInvitationAsync(first.IdentityKey, GroupInvitation.Create(key, record), cancellationToken));
        Assert.True(await self.Groups.ReceiveJoinAsync(second.IdentityKey, GroupJoin.Create(record), cancellationToken));
        Assert.True(self.Groups.TryGet(record.GroupId, out var group));
        Assert.Equal(GroupState.Created, group.State);
        return record;
    }

    private static async Task<GroupRecord> CreateFourMemberGroupAsync(
        Messenger self,
        KeySet first,
        string firstName,
        KeySet second,
        string secondName,
        KeySet third,
        string thirdName,
        GroupKey key,
        CancellationToken cancellationToken)
    {
        await AddContactAsync(self, first, firstName, cancellationToken);
        await AddContactAsync(self, second, secondName, cancellationToken);
        await AddContactAsync(self, third, thirdName, cancellationToken);
        GroupMember[] members =
        [
            new(self.Identity.Keys.IdentityKey, self.Identity.Name),
            new(first.IdentityKey, TestKeys.Name(firstName)),
            new(second.IdentityKey, TestKeys.Name(secondName)),
            new(third.IdentityKey, TestKeys.Name(thirdName)),
        ];
        var record = GroupRecord.Create(GroupId.Create(), 1, TestKeys.Name("Field team"), key, members, first);
        Assert.True(await self.Groups.ReceiveInvitationAsync(first.IdentityKey, GroupInvitation.Create(key, record), cancellationToken));
        Assert.True(await self.Groups.ReceiveJoinAsync(second.IdentityKey, GroupJoin.Create(record), cancellationToken));
        Assert.True(await self.Groups.ReceiveJoinAsync(third.IdentityKey, GroupJoin.Create(record), cancellationToken));
        Assert.True(self.Groups.TryGet(record.GroupId, out var group));
        Assert.Equal(GroupState.Created, group.State);
        return record;
    }

    private static async Task<GroupId> CreateConnectedThreeWayGroupAsync(Messenger alice, Messenger bob, Messenger carol, CancellationToken cancellationToken)
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

    private static async Task ExecuteAsync(string databasePath, string statement, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false };
        await using var connection = new SqliteConnection(connectionString.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = statement;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
