using System.Diagnostics;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Delivery;
using P2PMessenger.Application.Groups;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Integration.Tests.Connections;

namespace P2PMessenger.Integration.Tests.AppSlice;

public sealed class GroupDeliveryTests
{
    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-17")]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-04")]
    [Trait("NFR", "NFR-07")]
    [Trait("NFR", "NFR-11")]
    public async Task RelayedSignedAck_AcrossPasserRestart_MarksAbsentMemberReceivedWithoutMeetingAuthor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var protectors = new[] { new TestKeyProtector(), new TestKeyProtector(), new TestKeyProtector() };
        var paths = new[] { folder.DatabasePath("alice"), folder.DatabasePath("bob"), folder.DatabasePath("carol") };
        var alice = await AppPeer.StartSavedAsync(paths[0], protectors[0], "Alice", cancellationToken);
        var bob = await AppPeer.StartSavedAsync(paths[1], protectors[1], "Bob", cancellationToken);
        var carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
        try
        {
            await AddEachOtherAsync(cancellationToken, alice, bob, carol);
            var groupId = await CreateGroupAsync(alice, [bob, carol], cancellationToken);

            var aliceKey = Key(alice);
            var bobKey = Key(bob);
            var carolKey = Key(carol);
            await carol.DisposeAsync();
            await Eventually.HoldsAsync(() => !alice.Messenger.Connections.IsConnected(carolKey) && !bob.Messenger.Connections.IsConnected(carolKey), cancellationToken);

            var sent = (await alice.Messenger.SendToGroupAsync(groupId, "Bridge is clear", cancellationToken)).Message!;
            await Eventually.HoldsAsync(() => Single(alice, groupId).ReceivedCount == 1 && bob.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);
            Assert.Equal(DeliveryState.Received, Single(alice, groupId).Deliveries[bobKey]);
            Assert.Equal(DeliveryState.Waiting, Single(alice, groupId).Deliveries[carolKey]);

            await alice.DisposeAsync();
            await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(aliceKey), cancellationToken);

            carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
            await Eventually.HoldsAsync(() => carol.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);
            await Task.Delay(300, cancellationToken);

            await carol.DisposeAsync();
            await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(carolKey), cancellationToken);

            await bob.DisposeAsync();
            bob = await AppPeer.StartSavedAsync(paths[1], protectors[1], "Bob", cancellationToken);
            alice = await AppPeer.StartSavedAsync(paths[0], protectors[0], "Alice", cancellationToken);
            Assert.Equal(DeliveryState.Waiting, Single(alice, groupId).Deliveries[carolKey]);

            await Eventually.HoldsAsync(() => Single(alice, groupId).ReceivedCount == 2, cancellationToken);
            var updated = Single(alice, groupId);
            Assert.Equal(sent.Id, updated.Id);
            Assert.Equal(DeliveryState.Received, updated.Deliveries[bobKey]);
            Assert.Equal(DeliveryState.Received, updated.Deliveries[carolKey]);
            Assert.False(alice.Messenger.Connections.IsConnected(carolKey));
        }
        finally
        {
            await alice.DisposeAsync();
            await bob.DisposeAsync();
            await carol.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-07")]
    public async Task MultiHopRelayedAck_FourMembers_ReachesAuthorThroughTwoIntermediaries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var protectors = new[] { new TestKeyProtector(), new TestKeyProtector(), new TestKeyProtector(), new TestKeyProtector() };
        var paths = new[] { folder.DatabasePath("alice"), folder.DatabasePath("bob"), folder.DatabasePath("carol"), folder.DatabasePath("dave") };
        var alice = await AppPeer.StartSavedAsync(paths[0], protectors[0], "Alice", cancellationToken);
        var bob = await AppPeer.StartSavedAsync(paths[1], protectors[1], "Bob", cancellationToken);
        var carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
        var dave = await AppPeer.StartSavedAsync(paths[3], protectors[3], "Dave", cancellationToken);
        try
        {
            await AddEachOtherAsync(cancellationToken, alice, bob, carol, dave);
            var groupId = await CreateGroupAsync(alice, [bob, carol, dave], cancellationToken);

            var aliceKey = Key(alice);
            var bobKey = Key(bob);
            var carolKey = Key(carol);
            var daveKey = Key(dave);

            await carol.DisposeAsync();
            await dave.DisposeAsync();
            await Eventually.HoldsAsync(
                () => !alice.Messenger.Connections.IsConnected(carolKey)
                    && !alice.Messenger.Connections.IsConnected(daveKey)
                    && !bob.Messenger.Connections.IsConnected(carolKey)
                    && !bob.Messenger.Connections.IsConnected(daveKey),
                cancellationToken);

            await alice.Messenger.SendToGroupAsync(groupId, "Four-member relay", cancellationToken);
            await Eventually.HoldsAsync(() => Single(alice, groupId).ReceivedCount == 1 && bob.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);

            await alice.DisposeAsync();
            await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(aliceKey), cancellationToken);

            carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
            await Eventually.HoldsAsync(() => carol.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);
            await Task.Delay(300, cancellationToken);

            await bob.DisposeAsync();
            await Eventually.HoldsAsync(() => !carol.Messenger.Connections.IsConnected(bobKey), cancellationToken);

            dave = await AppPeer.StartSavedAsync(paths[3], protectors[3], "Dave", cancellationToken);
            await Eventually.HoldsAsync(() => dave.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);
            await Task.Delay(300, cancellationToken);

            await dave.DisposeAsync();
            await Eventually.HoldsAsync(() => !carol.Messenger.Connections.IsConnected(daveKey), cancellationToken);

            bob = await AppPeer.StartSavedAsync(paths[1], protectors[1], "Bob", cancellationToken);
            await Eventually.HoldsAsync(() => bob.Messenger.Connections.IsConnected(carolKey), cancellationToken);
            await Task.Delay(800, cancellationToken);

            await carol.DisposeAsync();
            await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(carolKey), cancellationToken);

            alice = await AppPeer.StartSavedAsync(paths[0], protectors[0], "Alice", cancellationToken);
            await Eventually.HoldsAsync(() => Single(alice, groupId).ReceivedCount == 3, cancellationToken);

            var atAlice = Single(alice, groupId);
            Assert.Equal(DeliveryState.Received, atAlice.Deliveries[bobKey]);
            Assert.Equal(DeliveryState.Received, atAlice.Deliveries[carolKey]);
            Assert.Equal(DeliveryState.Received, atAlice.Deliveries[daveKey]);
        }
        finally
        {
            await alice.DisposeAsync();
            await bob.DisposeAsync();
            await carol.DisposeAsync();
            await dave.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-07")]
    [Trait("NFR", "NFR-09")]
    [Trait("NFR", "NFR-11")]
    public async Task AdversarialMember_SendsUnsignedOrForgedGroupAcknowledgements_RejectedUntilValidSignedAckArrives()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await AppPeer.StartAsync("Alice", cancellationToken);
        using var bobKeys = KeySet.Create();
        using var carolKeys = KeySet.Create();
        using var outsiderKeys = KeySet.Create();
        await alice.ImportAsync(ContactCode.Create(bobKeys, Name("Bob")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await alice.ImportAsync(ContactCode.Create(carolKeys, Name("Carol")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var aliceAtBob = TestContacts.Create(bobKeys, alice.Identity.Keys);
        using var aliceAtCarol = TestContacts.Create(carolKeys, alice.Identity.Keys);
        await using var bob = await RawContact.OpenAsync(alice.Endpoint, bobKeys, aliceAtBob, cancellationToken);
        await using var carol = await RawContact.OpenAsync(alice.Endpoint, carolKeys, aliceAtCarol, cancellationToken);
        await Eventually.HoldsAsync(() => alice.ContactFor(bobKeys).IsAdded && alice.ContactFor(carolKeys).IsAdded, cancellationToken);

        var created = await alice.Messenger.CreateGroupAsync("Field team", [bobKeys.IdentityKey, carolKeys.IdentityKey], cancellationToken);
        Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
        var groupId = created.Group!.Id;
        var bobInvite = await bob.NextAsync<GroupInvitation>(cancellationToken);
        var carolInvite = await carol.NextAsync<GroupInvitation>(cancellationToken);
        await bob.SendAsync(Acknowledgement.Create(bobInvite.NoticeId), cancellationToken);
        await carol.SendAsync(Acknowledgement.Create(carolInvite.NoticeId), cancellationToken);
        await bob.SendAsync(GroupJoin.Create(bobInvite.Record), cancellationToken);
        await carol.SendAsync(GroupJoin.Create(carolInvite.Record), cancellationToken);
        await Eventually.HoldsAsync(() => StateAt(alice, groupId) == GroupState.Created, cancellationToken);

        var sent = (await alice.Messenger.SendToGroupAsync(groupId, "Must be signed", cancellationToken)).Message!;
        var bobMessage = await NextGroupMessageAsync(bob, sent.Envelope, cancellationToken);
        Assert.Equal(sent.Envelope, bobMessage);
        _ = await NextGroupMessageAsync(carol, sent.Envelope, cancellationToken);

        var bobAck = GroupAcknowledgement.Create(groupId, 1, sent.Id, bobKeys);
        await bob.SendAsync(bobAck, cancellationToken);
        await Eventually.HoldsAsync(() => Single(alice, groupId).Deliveries[bobKeys.IdentityKey] == DeliveryState.Received, cancellationToken);
        Assert.Equal(DeliveryState.Waiting, Single(alice, groupId).Deliveries[carolKeys.IdentityKey]);
        Assert.Null(await NextAcknowledgementWithinAsync(bob, TimeSpan.FromMilliseconds(350), cancellationToken));

        await bob.SendAsync(Acknowledgement.Create(sent.Id), cancellationToken);
        await carol.SendAsync(Acknowledgement.Create(sent.Id), cancellationToken);

        var wrongSignerBytes = GroupAcknowledgement.Create(groupId, 1, sent.Id, bobKeys).ToBytes();
        carolKeys.IdentityKey.Bytes.CopyTo(wrongSignerBytes.AsSpan(GroupId.Length + sizeof(uint) + MessageId.Length));
        Assert.True(GroupAcknowledgement.TryParse(wrongSignerBytes, out var wrongSigner));
        await bob.SendAsync(wrongSigner, cancellationToken);

        var genuineCarolAck = GroupAcknowledgement.Create(groupId, 1, sent.Id, carolKeys);
        var tamperedBytes = genuineCarolAck.ToBytes();
        tamperedBytes[^1] ^= 1;
        Assert.True(GroupAcknowledgement.TryParse(tamperedBytes, out var tampered));
        await bob.SendAsync(tampered, cancellationToken);

        await bob.SendAsync(GroupAcknowledgement.Create(groupId, 1, sent.Id, outsiderKeys), cancellationToken);
        await bob.SendAsync(GroupAcknowledgement.Create(GroupId.Create(), 1, sent.Id, carolKeys), cancellationToken);
        await bob.SendAsync(GroupAcknowledgement.Create(groupId, 2, sent.Id, carolKeys), cancellationToken);
        await bob.SendAsync(GroupAcknowledgement.Create(groupId, 1, MessageId.Create(), carolKeys), cancellationToken);

        Assert.Null(await NextAcknowledgementWithinAsync(bob, TimeSpan.FromMilliseconds(500), cancellationToken));
        Assert.Equal(1, Single(alice, groupId).ReceivedCount);
        Assert.Equal(DeliveryState.Waiting, Single(alice, groupId).Deliveries[carolKeys.IdentityKey]);
        Assert.True(alice.Messenger.Connections.IsConnected(bobKeys.IdentityKey));

        await bob.SendAsync(genuineCarolAck, cancellationToken);
        var noticeAck = await NextAcknowledgementAsync(bob, genuineCarolAck.NoticeId, cancellationToken);
        Assert.Equal(genuineCarolAck.NoticeId, noticeAck.Id);
        await Eventually.HoldsAsync(() => Single(alice, groupId).ReceivedCount == 2, cancellationToken);
        Assert.Equal(DeliveryState.Received, Single(alice, groupId).Deliveries[carolKeys.IdentityKey]);
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-17")]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-09")]
    public async Task Passer_DeletesStoredRelayedAckOnAuthorConfirmAndDeletedChatAuthorConfirmsViaSeen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var bobProtector = new TestKeyProtector();
        var bobPath = folder.DatabasePath("bob");
        var bob = await AppPeer.StartSavedAsync(bobPath, bobProtector, "Bob", cancellationToken);
        using var aliceKeys = KeySet.Create();
        using var carolKeys = KeySet.Create();
        try
        {
            await bob.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
            await bob.ImportAsync(ContactCode.Create(carolKeys, Name("Carol")).ToText(), CodeCheckOutcome.New, cancellationToken);
            using var bobAtAlice = TestContacts.Create(aliceKeys, bob.Identity.Keys);
            using var bobAtCarol = TestContacts.Create(carolKeys, bob.Identity.Keys);

            using var key = GroupKey.Create();
            GroupMember[] members =
            [
                new(aliceKeys.IdentityKey, Name("Alice")),
                new(Key(bob), Name("Bob")),
                new(carolKeys.IdentityKey, Name("Carol")),
            ];
            var record = GroupRecord.Create(GroupId.Create(), 1, Name("Field team"), key, members, aliceKeys);
            var fromAlice = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(1, "Relayed once"));
            var messageId = OpenText(fromAlice, key).Id;

            await using (var alice = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAtAlice, cancellationToken))
            await using (var carol = await RawContact.OpenAsync(bob.Endpoint, carolKeys, bobAtCarol, cancellationToken))
            {
                await Eventually.HoldsAsync(() => bob.ContactFor(aliceKeys).IsAdded && bob.ContactFor(carolKeys).IsAdded, cancellationToken);
                await alice.SendAsync(GroupInvitation.Create(key, record), cancellationToken);
                await NextAcknowledgementAsync(alice, GroupNotices.InvitationId(record), cancellationToken);
                await carol.SendAsync(GroupJoin.Create(record), cancellationToken);
                await NextAcknowledgementAsync(carol, GroupNotices.JoinId(record.Hash, carolKeys.IdentityKey), cancellationToken);
                await Eventually.HoldsAsync(() => StateAt(bob, record.GroupId) == GroupState.Created, cancellationToken);

                await alice.SendAsync(fromAlice, cancellationToken);
                await WaitForGroupAckAsync(alice, messageId, Key(bob), cancellationToken);
            }

            await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(aliceKeys.IdentityKey) && !bob.Messenger.Connections.IsConnected(carolKeys.IdentityKey), cancellationToken);

            var carolAck = GroupAcknowledgement.Create(record.GroupId, 1, messageId, carolKeys);
            await using (var carol = await RawContact.OpenAsync(bob.Endpoint, carolKeys, bobAtCarol, cancellationToken))
            {
                await carol.SendAsync(carolAck, cancellationToken);
                await Task.Delay(300, cancellationToken);
            }

            await bob.DisposeAsync();
            bob = await AppPeer.StartSavedAsync(bobPath, bobProtector, "Bob", cancellationToken);

            await using (var alice = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAtAlice, cancellationToken))
            {
                var relayed = await WaitForGroupAckAsync(alice, messageId, carolKeys.IdentityKey, cancellationToken);
                Assert.Equal(carolAck, relayed);
                await alice.SendAsync(Acknowledgement.Create(relayed.NoticeId), cancellationToken);
                await Task.Delay(300, cancellationToken);
            }

            await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(aliceKeys.IdentityKey), cancellationToken);
            await bob.DisposeAsync();
            bob = await AppPeer.StartSavedAsync(bobPath, bobProtector, "Bob", cancellationToken);

            await using (var alice = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAtAlice, cancellationToken))
            {
                Assert.Null(await NextGroupAckWithinAsync(alice, TimeSpan.FromMilliseconds(500), cancellationToken));
            }
        }
        finally
        {
            await bob.DisposeAsync();
        }

        await using var author = await AppPeer.StartAsync("Alice", cancellationToken);
        using var passerKeys = KeySet.Create();
        using var absentKeys = KeySet.Create();
        await author.ImportAsync(ContactCode.Create(passerKeys, Name("Bob")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await author.ImportAsync(ContactCode.Create(absentKeys, Name("Carol")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var authorAtPasser = TestContacts.Create(passerKeys, author.Identity.Keys);
        using var authorAtAbsent = TestContacts.Create(absentKeys, author.Identity.Keys);
        await using var passer = await RawContact.OpenAsync(author.Endpoint, passerKeys, authorAtPasser, cancellationToken);
        await using var absent = await RawContact.OpenAsync(author.Endpoint, absentKeys, authorAtAbsent, cancellationToken);
        await Eventually.HoldsAsync(() => author.ContactFor(passerKeys).IsAdded && author.ContactFor(absentKeys).IsAdded, cancellationToken);

        var group = (await author.Messenger.CreateGroupAsync("Field team", [passerKeys.IdentityKey, absentKeys.IdentityKey], cancellationToken)).Group!;
        var passerInvite = await passer.NextAsync<GroupInvitation>(cancellationToken);
        var absentInvite = await absent.NextAsync<GroupInvitation>(cancellationToken);
        await passer.SendAsync(Acknowledgement.Create(passerInvite.NoticeId), cancellationToken);
        await absent.SendAsync(Acknowledgement.Create(absentInvite.NoticeId), cancellationToken);
        await passer.SendAsync(GroupJoin.Create(passerInvite.Record), cancellationToken);
        await absent.SendAsync(GroupJoin.Create(absentInvite.Record), cancellationToken);
        await Eventually.HoldsAsync(() => StateAt(author, group.Id) == GroupState.Created, cancellationToken);

        var sent = (await author.Messenger.SendToGroupAsync(group.Id, "Delete before ack", cancellationToken)).Message!;
        _ = await NextGroupMessageAsync(passer, sent.Envelope, cancellationToken);
        Assert.Equal(1, await author.Messenger.DeleteGroupChatAsync(group.Id, cancellationToken));

        var validLateAck = GroupAcknowledgement.Create(group.Id, 1, sent.Id, absentKeys);
        var forgedBytes = validLateAck.ToBytes();
        forgedBytes[^1] ^= 1;
        Assert.True(GroupAcknowledgement.TryParse(forgedBytes, out var forgedLateAck));
        await passer.SendAsync(forgedLateAck, cancellationToken);
        Assert.Null(await NextAcknowledgementWithinAsync(passer, TimeSpan.FromMilliseconds(400), cancellationToken));

        await passer.SendAsync(validLateAck, cancellationToken);
        var lateConfirm = await NextAcknowledgementAsync(passer, validLateAck.NoticeId, cancellationToken);
        Assert.Equal(validLateAck.NoticeId, lateConfirm.Id);
        Assert.Empty(author.Messenger.GroupChats.MessagesIn(group.Id));
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-07")]
    public async Task MultiHopRelayedAck_AlreadyConnectedPeerHoldingMessage_ReceivesAckWithoutReconnecting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var carol = await AppPeer.StartAsync("Carol", cancellationToken);
        using var aliceKeys = KeySet.Create();
        using var bobKeys = KeySet.Create();
        using var daveKeys = KeySet.Create();
        await carol.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await carol.ImportAsync(ContactCode.Create(bobKeys, Name("Bob")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await carol.ImportAsync(ContactCode.Create(daveKeys, Name("Dave")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var carolAtAlice = TestContacts.Create(aliceKeys, carol.Identity.Keys);
        using var carolAtBob = TestContacts.Create(bobKeys, carol.Identity.Keys);
        using var carolAtDave = TestContacts.Create(daveKeys, carol.Identity.Keys);
        await using var alice = await RawContact.OpenAsync(carol.Endpoint, aliceKeys, carolAtAlice, cancellationToken);
        await using var bob = await RawContact.OpenAsync(carol.Endpoint, bobKeys, carolAtBob, cancellationToken);
        await using var dave = await RawContact.OpenAsync(carol.Endpoint, daveKeys, carolAtDave, cancellationToken);
        await Eventually.HoldsAsync(
            () => carol.ContactFor(aliceKeys).IsAdded && carol.ContactFor(bobKeys).IsAdded && carol.ContactFor(daveKeys).IsAdded,
            cancellationToken);

        using var key = GroupKey.Create();
        GroupMember[] members =
        [
            new(aliceKeys.IdentityKey, Name("Alice")),
            new(bobKeys.IdentityKey, Name("Bob")),
            new(Key(carol), Name("Carol")),
            new(daveKeys.IdentityKey, Name("Dave")),
        ];
        var record = GroupRecord.Create(GroupId.Create(), 1, Name("Field team"), key, members, aliceKeys);
        await alice.SendAsync(GroupInvitation.Create(key, record), cancellationToken);
        await NextAcknowledgementAsync(alice, GroupNotices.InvitationId(record), cancellationToken);
        await bob.SendAsync(GroupJoin.Create(record), cancellationToken);
        await NextAcknowledgementAsync(bob, GroupNotices.JoinId(record.Hash, bobKeys.IdentityKey), cancellationToken);
        await dave.SendAsync(GroupJoin.Create(record), cancellationToken);
        await NextAcknowledgementAsync(dave, GroupNotices.JoinId(record.Hash, daveKeys.IdentityKey), cancellationToken);
        await Eventually.HoldsAsync(() => StateAt(carol, record.GroupId) == GroupState.Created, cancellationToken);

        var fromAlice = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(1, "Live multi-hop ack"));
        var msgId = OpenText(fromAlice, key).Id;
        await alice.SendAsync(fromAlice, cancellationToken);
        await WaitForGroupAckAsync(alice, msgId, Key(carol), cancellationToken);

        await bob.SendAsync(
            GroupSummary.Create(record.GroupId, 1, [new GroupSummaryAuthor(aliceKeys.IdentityKey, [new SequenceRange(1, 1)])]),
            cancellationToken);
        Assert.True(TextMessage.TryCreate(MessageId.Create(), 1, DateTimeOffset.UnixEpoch.AddMinutes(1), "sync", out var syncMessage));
        await bob.SendAsync(syncMessage, cancellationToken);
        await NextAcknowledgementAsync(bob, syncMessage.Id, cancellationToken);

        var daveAck = GroupAcknowledgement.Create(record.GroupId, 1, msgId, daveKeys);
        await dave.SendAsync(daveAck, cancellationToken);

        var forwardedToBob = await WaitForGroupAckAsync(bob, msgId, daveKeys.IdentityKey, cancellationToken);
        Assert.Equal(daveAck, forwardedToBob);
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-07")]
    public async Task MultiHopRelayedAck_UnconfirmedOnLiveSession_RetriesOnWakeUpUntilPeerOrAuthorConfirms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var carol = await AppPeer.StartAsync(
            KeySet.Create(),
            "Carol",
            setup => setup with
            {
                Delivery = new DeliveryOptions
                {
                    ResendAfter = TimeSpan.FromMilliseconds(500),
                    WakeUpInterval = TimeSpan.FromMilliseconds(100),
                },
            },
            cancellationToken);
        using var aliceKeys = KeySet.Create();
        using var bobKeys = KeySet.Create();
        using var daveKeys = KeySet.Create();
        await carol.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await carol.ImportAsync(ContactCode.Create(bobKeys, Name("Bob")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await carol.ImportAsync(ContactCode.Create(daveKeys, Name("Dave")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var carolAtAlice = TestContacts.Create(aliceKeys, carol.Identity.Keys);
        using var carolAtBob = TestContacts.Create(bobKeys, carol.Identity.Keys);
        using var carolAtDave = TestContacts.Create(daveKeys, carol.Identity.Keys);
        await using var alice = await RawContact.OpenAsync(carol.Endpoint, aliceKeys, carolAtAlice, cancellationToken);
        await using var bob = await RawContact.OpenAsync(carol.Endpoint, bobKeys, carolAtBob, cancellationToken);
        await using var dave = await RawContact.OpenAsync(carol.Endpoint, daveKeys, carolAtDave, cancellationToken);
        await Eventually.HoldsAsync(
            () => carol.ContactFor(aliceKeys).IsAdded && carol.ContactFor(bobKeys).IsAdded && carol.ContactFor(daveKeys).IsAdded,
            cancellationToken);

        using var key = GroupKey.Create();
        GroupMember[] members =
        [
            new(aliceKeys.IdentityKey, Name("Alice")),
            new(bobKeys.IdentityKey, Name("Bob")),
            new(Key(carol), Name("Carol")),
            new(daveKeys.IdentityKey, Name("Dave")),
        ];
        var record = GroupRecord.Create(GroupId.Create(), 1, Name("Field team"), key, members, aliceKeys);
        await alice.SendAsync(GroupInvitation.Create(key, record), cancellationToken);
        await NextAcknowledgementAsync(alice, GroupNotices.InvitationId(record), cancellationToken);
        await bob.SendAsync(GroupJoin.Create(record), cancellationToken);
        await NextAcknowledgementAsync(bob, GroupNotices.JoinId(record.Hash, bobKeys.IdentityKey), cancellationToken);
        await dave.SendAsync(GroupJoin.Create(record), cancellationToken);
        await NextAcknowledgementAsync(dave, GroupNotices.JoinId(record.Hash, daveKeys.IdentityKey), cancellationToken);
        await Eventually.HoldsAsync(() => StateAt(carol, record.GroupId) == GroupState.Created, cancellationToken);

        var first = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(1, "Retry first"));
        var second = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(2, "Retry second"));
        var firstId = OpenText(first, key).Id;
        var secondId = OpenText(second, key).Id;
        await alice.SendAsync(first, cancellationToken);
        await WaitForGroupAckAsync(alice, firstId, Key(carol), cancellationToken);
        await alice.SendAsync(second, cancellationToken);
        await WaitForGroupAckAsync(alice, secondId, Key(carol), cancellationToken);

        await bob.SendAsync(
            GroupSummary.Create(record.GroupId, 1, [new GroupSummaryAuthor(aliceKeys.IdentityKey, [new SequenceRange(1, 2)])]),
            cancellationToken);
        Assert.True(TextMessage.TryCreate(MessageId.Create(), 1, DateTimeOffset.UnixEpoch.AddMinutes(1), "sync", out var syncMessage));
        await bob.SendAsync(syncMessage, cancellationToken);
        await NextAcknowledgementAsync(bob, syncMessage.Id, cancellationToken);

        var daveAck1 = GroupAcknowledgement.Create(record.GroupId, 1, firstId, daveKeys);
        await dave.SendAsync(daveAck1, cancellationToken);

        Assert.Equal(daveAck1, await WaitForGroupAckAsync(bob, firstId, daveKeys.IdentityKey, cancellationToken));
        Assert.Equal(daveAck1, await WaitForGroupAckAsync(bob, firstId, daveKeys.IdentityKey, cancellationToken));

        await bob.SendAsync(Acknowledgement.Create(daveAck1.NoticeId), cancellationToken);
        Assert.True(TextMessage.TryCreate(MessageId.Create(), 2, DateTimeOffset.UnixEpoch.AddMinutes(2), "sync-bob", out var syncBob));
        await bob.SendAsync(syncBob, cancellationToken);
        await NextAcknowledgementAsync(bob, syncBob.Id, cancellationToken);
        Assert.Null(await NextGroupAckWithinAsync(bob, TimeSpan.FromMilliseconds(800), cancellationToken));

        Assert.Equal(daveAck1, await WaitForGroupAckAsync(alice, firstId, daveKeys.IdentityKey, cancellationToken));
        await alice.SendAsync(Acknowledgement.Create(daveAck1.NoticeId), cancellationToken);
        Assert.True(TextMessage.TryCreate(MessageId.Create(), 1, DateTimeOffset.UnixEpoch.AddMinutes(2), "sync-alice-1", out var syncAlice1));
        await alice.SendAsync(syncAlice1, cancellationToken);
        await NextAcknowledgementAsync(alice, syncAlice1.Id, cancellationToken);

        var daveAck2 = GroupAcknowledgement.Create(record.GroupId, 1, secondId, daveKeys);
        await dave.SendAsync(daveAck2, cancellationToken);

        Assert.Equal(daveAck2, await WaitForGroupAckAsync(bob, secondId, daveKeys.IdentityKey, cancellationToken));
        Assert.Equal(daveAck2, await WaitForGroupAckAsync(alice, secondId, daveKeys.IdentityKey, cancellationToken));
        await alice.SendAsync(Acknowledgement.Create(daveAck2.NoticeId), cancellationToken);
        Assert.True(TextMessage.TryCreate(MessageId.Create(), 2, DateTimeOffset.UnixEpoch.AddMinutes(3), "sync-alice-2", out var syncAlice2));
        await alice.SendAsync(syncAlice2, cancellationToken);
        await NextAcknowledgementAsync(alice, syncAlice2.Id, cancellationToken);
        while (await NextGroupAckWithinAsync(bob, TimeSpan.FromMilliseconds(50), cancellationToken) is not null)
        {
        }

        Assert.Null(await NextGroupAckWithinAsync(bob, TimeSpan.FromMilliseconds(800), cancellationToken));
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-07")]
    public async Task PassedOnMessage_ConnectionDropsBeforeAckReachesPasser_ReSendsSignedAckOnReconnect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var carol = await AppPeer.StartAsync("Carol", cancellationToken);
        using var aliceKeys = KeySet.Create();
        using var bobKeys = KeySet.Create();
        await carol.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await carol.ImportAsync(ContactCode.Create(bobKeys, Name("Bob")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var carolAtAlice = TestContacts.Create(aliceKeys, carol.Identity.Keys);
        using var carolAtBob = TestContacts.Create(bobKeys, carol.Identity.Keys);
        await using (var alice = await RawContact.OpenAsync(carol.Endpoint, aliceKeys, carolAtAlice, cancellationToken))
        await using (var bob = await RawContact.OpenAsync(carol.Endpoint, bobKeys, carolAtBob, cancellationToken))
        {
            await Eventually.HoldsAsync(
                () => carol.ContactFor(aliceKeys).IsAdded && carol.ContactFor(bobKeys).IsAdded,
                cancellationToken);

            using var key = GroupKey.Create();
            GroupMember[] members =
            [
                new(aliceKeys.IdentityKey, Name("Alice")),
                new(bobKeys.IdentityKey, Name("Bob")),
                new(Key(carol), Name("Carol")),
            ];
            var record = GroupRecord.Create(GroupId.Create(), 1, Name("Field team"), key, members, aliceKeys);
            await alice.SendAsync(GroupInvitation.Create(key, record), cancellationToken);
            await NextAcknowledgementAsync(alice, GroupNotices.InvitationId(record), cancellationToken);
            await bob.SendAsync(GroupJoin.Create(record), cancellationToken);
            await NextAcknowledgementAsync(bob, GroupNotices.JoinId(record.Hash, bobKeys.IdentityKey), cancellationToken);
            await Eventually.HoldsAsync(() => StateAt(carol, record.GroupId) == GroupState.Created, cancellationToken);

            var fromAlice = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(1, "Passed before drop"));
            var msgId = OpenText(fromAlice, key).Id;
            await bob.SendAsync(fromAlice, cancellationToken);
            await Eventually.HoldsAsync(() => carol.Messenger.GroupChats.MessagesIn(record.GroupId).Count == 1, cancellationToken);

            await bob.DisposeAsync();
            await Eventually.HoldsAsync(() => !carol.Messenger.Connections.IsConnected(bobKeys.IdentityKey), cancellationToken);

            await using var bobReconnected = await RawContact.OpenAsync(carol.Endpoint, bobKeys, carolAtBob, cancellationToken);
            var resentAck = await WaitForGroupAckAsync(bobReconnected, msgId, Key(carol), cancellationToken);
            Assert.Equal(record.GroupId, resentAck.GroupId);

            await bobReconnected.SendAsync(Acknowledgement.Create(resentAck.NoticeId), cancellationToken);
            Assert.True(TextMessage.TryCreate(MessageId.Create(), 1, DateTimeOffset.UnixEpoch.AddMinutes(1), "sync-own-ack", out var syncMessage));
            await bobReconnected.SendAsync(syncMessage, cancellationToken);
            await NextAcknowledgementAsync(bobReconnected, syncMessage.Id, cancellationToken);
        }

        await Eventually.HoldsAsync(() => !carol.Messenger.Connections.IsConnected(bobKeys.IdentityKey), cancellationToken);
        await using var bobFinal = await RawContact.OpenAsync(carol.Endpoint, bobKeys, carolAtBob, cancellationToken);
        Assert.Null(await NextGroupAckWithinAsync(bobFinal, TimeSpan.FromMilliseconds(500), cancellationToken));
    }

    private static Ed25519PublicKey Key(AppPeer peer) => peer.Identity.Keys.IdentityKey;

    private static GroupState? StateAt(AppPeer peer, GroupId groupId) =>
        peer.Messenger.Groups.TryGet(groupId, out var group) ? group.State : null;

    private static bool IsCreatedAt(GroupId groupId, params AppPeer[] peers) =>
        peers.All(peer => StateAt(peer, groupId) == GroupState.Created);

    private static GroupChatMessage Single(AppPeer peer, GroupId groupId) => Assert.Single(peer.Messenger.GroupChats.MessagesIn(groupId));

    private static DisplayName Name(string value) => DisplayName.TryCreate(value, out var name) ? name : throw new InvalidOperationException();

    private static GroupText Text(long sequence, string text) =>
        GroupText.TryCreate(MessageId.Create(), sequence, DateTimeOffset.UnixEpoch.AddMinutes(sequence), text, out var groupText) ? groupText : throw new InvalidOperationException();

    private static GroupText OpenText(GroupMessage message, GroupKey key) =>
        message.TryOpen(key, out var text) ? text : throw new InvalidOperationException();

    private static async Task<GroupMessage> NextGroupMessageAsync(RawContact contact, GroupMessage expected, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        var limit = TimeSpan.FromSeconds(20);
        while (deadline.Elapsed < limit)
        {
            var next = await contact.NextWithinAsync(limit - deadline.Elapsed, cancellationToken);
            if (next is GroupMessage groupMessage && groupMessage.Equals(expected))
            {
                return groupMessage;
            }
        }

        Assert.Fail("Expected GroupMessage within 20 s.");
        throw new InvalidOperationException();
    }

    private static async Task<Acknowledgement> NextAcknowledgementAsync(RawContact contact, MessageId expectedId, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        var limit = TimeSpan.FromSeconds(20);
        while (deadline.Elapsed < limit)
        {
            var next = await contact.NextWithinAsync(limit - deadline.Elapsed, cancellationToken);
            if (next is Acknowledgement ack && ack.Id.Equals(expectedId))
            {
                return ack;
            }
        }

        Assert.Fail($"Expected Acknowledgement for {expectedId} within 20 s.");
        throw new InvalidOperationException();
    }

    private static async Task<Acknowledgement?> NextAcknowledgementWithinAsync(RawContact contact, TimeSpan limit, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < limit)
        {
            var remaining = limit - deadline.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            var next = await contact.NextWithinAsync(remaining, cancellationToken);
            if (next is null)
            {
                return null;
            }

            if (next is Acknowledgement ack)
            {
                return ack;
            }
        }

        return null;
    }

    private static async Task<GroupAcknowledgement> WaitForGroupAckAsync(
        RawContact contact,
        MessageId expectedMessageId,
        Ed25519PublicKey expectedRecipientKey,
        CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        var limit = TimeSpan.FromSeconds(20);
        while (deadline.Elapsed < limit)
        {
            var next = await contact.NextWithinAsync(limit - deadline.Elapsed, cancellationToken);
            if (next is GroupAcknowledgement ack
                && ack.MessageId.Equals(expectedMessageId)
                && ack.RecipientKey.Equals(expectedRecipientKey))
            {
                Assert.True(ack.HasValidSignature());
                return ack;
            }
        }

        Assert.Fail($"Expected GroupAcknowledgement for {expectedMessageId} within 20 s.");
        throw new InvalidOperationException();
    }

    private static async Task<GroupAcknowledgement?> NextGroupAckWithinAsync(RawContact contact, TimeSpan limit, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < limit)
        {
            var remaining = limit - deadline.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            var next = await contact.NextWithinAsync(remaining, cancellationToken);
            if (next is null)
            {
                return null;
            }

            if (next is GroupAcknowledgement ack)
            {
                return ack;
            }
        }

        return null;
    }

    private static async Task AddEachOtherAsync(CancellationToken cancellationToken, params AppPeer[] peers)
    {
        for (var first = 0; first < peers.Length; first++)
        {
            for (var second = first + 1; second < peers.Length; second++)
            {
                await peers[first].ImportAsync(peers[second], cancellationToken);
                await peers[second].ImportAsync(peers[first], cancellationToken);
            }
        }

        await Eventually.HoldsAsync(
            () => peers.All(peer => peers.Where(other => other != peer).All(other => peer.ContactFor(other).IsAdded)),
            cancellationToken);
    }

    private static async Task<GroupId> CreateGroupAsync(AppPeer creator, AppPeer[] others, CancellationToken cancellationToken)
    {
        var created = await creator.Messenger.CreateGroupAsync("Field team", [.. others.Select(Key)], cancellationToken);
        Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
        var groupId = created.Group!.Id;
        await Eventually.HoldsAsync(() => IsCreatedAt(groupId, [creator, .. others]), cancellationToken);
        return groupId;
    }
}
