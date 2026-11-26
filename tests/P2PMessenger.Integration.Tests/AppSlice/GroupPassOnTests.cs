using System.Diagnostics;
using P2PMessenger.Application;
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

public sealed class GroupPassOnTests
{
    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-04")]
    public async Task ThreeMembersOneAway_AuthorLeavesAndAbsentMemberMeetsNonCreator_ReceivesMessageWithin30SecondsWithOriginalAuthor()
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

            var created = await alice.Messenger.CreateGroupAsync("Field team", [Key(bob), Key(carol)], cancellationToken);
            Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
            var groupId = created.Group!.Id;
            await Eventually.HoldsAsync(() => IsCreatedAt(groupId, alice, bob, carol), cancellationToken);

            var aliceKey = Key(alice);
            var carolKey = Key(carol);
            await carol.DisposeAsync();
            await Eventually.HoldsAsync(() => !alice.Messenger.Connections.IsConnected(carolKey) && !bob.Messenger.Connections.IsConnected(carolKey), cancellationToken);

            var sent = (await alice.Messenger.SendToGroupAsync(groupId, "Bridge is clear", cancellationToken)).Message!;
            await Eventually.HoldsAsync(() => Single(alice, groupId).ReceivedCount == 1 && bob.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);

            await alice.DisposeAsync();
            await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(aliceKey), cancellationToken);

            carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
            await Eventually.HoldsAsync(() => bob.Messenger.Connections.IsConnected(carolKey), cancellationToken);
            var connected = Stopwatch.StartNew();

            await Eventually.HoldsAsync(() => carol.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);

            Assert.True(connected.Elapsed < TimeSpan.FromSeconds(30), $"Passed on after {connected.Elapsed}.");
            var arrived = Single(carol, groupId);
            Assert.Equal("Bridge is clear", arrived.Text);
            Assert.Equal(aliceKey, arrived.AuthorKey);
            Assert.Equal(sent.Id, arrived.Id);
            Assert.Equal(1, carol.Messenger.GroupChats.UnreadCount(groupId));
        }
        finally
        {
            await alice.DisposeAsync();
            await bob.DisposeAsync();
            await carol.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-13")]
    [Trait("Story", "US-16")]
    [Trait("NFR", "NFR-04")]
    [Trait("NFR", "NFR-07")]
    public async Task AuthorReconnectsAfterAnotherMemberPassedTheMessageOn_MessageIsNotShownTwiceAndAuthorsCountUpdates()
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
            var carolKey = Key(carol);
            await carol.DisposeAsync();
            await Eventually.HoldsAsync(() => !alice.Messenger.Connections.IsConnected(carolKey) && !bob.Messenger.Connections.IsConnected(carolKey), cancellationToken);

            var sent = (await alice.Messenger.SendToGroupAsync(groupId, "Once only", cancellationToken)).Message!;
            await Eventually.HoldsAsync(() => Single(alice, groupId).ReceivedCount == 1 && bob.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);
            await alice.DisposeAsync();
            await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(aliceKey), cancellationToken);

            carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
            await Eventually.HoldsAsync(() => carol.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);

            alice = await AppPeer.StartSavedAsync(paths[0], protectors[0], "Alice", cancellationToken);
            await Eventually.HoldsAsync(() => Single(alice, groupId).ReceivedCount == 2, cancellationToken);
            await Task.Delay(300, cancellationToken);

            var shown = Single(carol, groupId);
            Assert.Equal(sent.Id, shown.Id);
            Assert.Equal("Once only", shown.Text);
            Assert.Equal(1, carol.Messenger.GroupChats.UnreadCount(groupId));
        }
        finally
        {
            await alice.DisposeAsync();
            await bob.DisposeAsync();
            await carol.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-04")]
    public async Task TwoMembersAlreadyConnected_WhenThirdDeliversAMessageToOne_ItIsPassedOnWithoutReconnecting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        await using var carol = await AppPeer.StartAsync("Carol", cancellationToken);
        await AddEachOtherAsync(cancellationToken, bob, carol);

        using var aliceKeys = KeySet.Create();
        await bob.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await carol.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var bobAtAlice = TestContacts.Create(aliceKeys, bob.Identity.Keys);
        using var carolAtAlice = TestContacts.Create(aliceKeys, carol.Identity.Keys);
        await using var aliceToBob = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAtAlice, cancellationToken);
        await using (var aliceToCarol = await RawContact.OpenAsync(carol.Endpoint, aliceKeys, carolAtAlice, cancellationToken))
        {
            await Eventually.HoldsAsync(() => bob.ContactFor(aliceKeys).IsAdded && carol.ContactFor(aliceKeys).IsAdded, cancellationToken);
            using var key = GroupKey.Create();
            GroupMember[] members =
            [
                new(aliceKeys.IdentityKey, Name("Alice")),
                new(Key(bob), Name("Bob")),
                new(Key(carol), Name("Carol")),
            ];
            var record = GroupRecord.Create(GroupId.Create(), 1, Name("Field team"), key, members, aliceKeys);
            await aliceToBob.SendAsync(GroupInvitation.Create(key, record), cancellationToken);
            await aliceToCarol.SendAsync(GroupInvitation.Create(key, record), cancellationToken);
            await Eventually.HoldsAsync(() => IsCreatedAt(record.GroupId, bob, carol), cancellationToken);

            await aliceToCarol.DisposeAsync();
            await Eventually.HoldsAsync(() => !carol.Messenger.Connections.IsConnected(aliceKeys.IdentityKey), cancellationToken);
            Assert.True(bob.Messenger.Connections.IsConnected(Key(carol)));

            var message = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(1, "Relayed over open session"));
            await aliceToBob.SendAsync(message, cancellationToken);
            await WaitForGroupAckAsync(aliceToBob, OpenText(message, key).Id, Key(bob), cancellationToken);

            await Eventually.HoldsAsync(() => carol.Messenger.GroupChats.MessagesIn(record.GroupId).Count == 1, cancellationToken);
            var arrived = Single(carol, record.GroupId);
            Assert.Equal("Relayed over open session", arrived.Text);
            Assert.Equal(aliceKeys.IdentityKey, arrived.AuthorKey);
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-13")]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-06")]
    public async Task PasserRestartsAndDeletedChatAtAbsentMember_PassesOnlyNewMessagesAndNeverReturnsDeletedOnes()
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

            await alice.Messenger.SendToGroupAsync(groupId, "old one", cancellationToken);
            await bob.Messenger.SendToGroupAsync(groupId, "old two", cancellationToken);
            await Eventually.HoldsAsync(
                () => alice.Messenger.GroupChats.MessagesIn(groupId).Count == 2
                    && bob.Messenger.GroupChats.MessagesIn(groupId).Count == 2
                    && carol.Messenger.GroupChats.MessagesIn(groupId).Count == 2
                    && SingleOutgoing(alice, groupId).ReceivedCount == 2
                    && SingleOutgoing(bob, groupId).ReceivedCount == 2,
                cancellationToken);

            Assert.Equal(2, await carol.Messenger.DeleteGroupChatAsync(groupId, cancellationToken));
            await carol.DisposeAsync();
            await Eventually.HoldsAsync(() => !alice.Messenger.Connections.IsConnected(Key(carol)) && !bob.Messenger.Connections.IsConnected(Key(carol)), cancellationToken);

            await alice.Messenger.SendToGroupAsync(groupId, "new three", cancellationToken);
            await Eventually.HoldsAsync(() => bob.Messenger.GroupChats.MessagesIn(groupId).Count == 3, cancellationToken);

            await alice.DisposeAsync();
            await bob.DisposeAsync();
            bob = await AppPeer.StartSavedAsync(paths[1], protectors[1], "Bob", cancellationToken);
            carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);

            await Eventually.HoldsAsync(() => carol.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);
            await Task.Delay(400, cancellationToken);

            var only = Single(carol, groupId);
            Assert.Equal("new three", only.Text);
            Assert.Equal(Key(alice), only.AuthorKey);
            Assert.Equal(2, only.Sequence);
        }
        finally
        {
            await alice.DisposeAsync();
            await bob.DisposeAsync();
            await carol.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-09")]
    public async Task AdversarialMember_PassesForgedTamperedOrReSignedMessages_IsDiscardedAndNotAcknowledged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var carol = await AppPeer.StartAsync("Carol", cancellationToken);
        using var aliceKeys = KeySet.Create();
        using var bobKeys = KeySet.Create();
        using var outsiderKeys = KeySet.Create();
        await carol.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await carol.ImportAsync(ContactCode.Create(bobKeys, Name("Bob")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var carolAtAlice = TestContacts.Create(aliceKeys, carol.Identity.Keys);
        using var carolAtBob = TestContacts.Create(bobKeys, carol.Identity.Keys);
        await using var alice = await RawContact.OpenAsync(carol.Endpoint, aliceKeys, carolAtAlice, cancellationToken);
        await using var bob = await RawContact.OpenAsync(carol.Endpoint, bobKeys, carolAtBob, cancellationToken);
        await Eventually.HoldsAsync(() => carol.ContactFor(aliceKeys).IsAdded && carol.ContactFor(bobKeys).IsAdded, cancellationToken);

        using var key = GroupKey.Create();
        GroupMember[] members =
        [
            new(aliceKeys.IdentityKey, Name("Alice")),
            new(bobKeys.IdentityKey, Name("Bob")),
            new(Key(carol), Name("Carol")),
        ];
        var record = GroupRecord.Create(GroupId.Create(), 1, Name("Field team"), key, members, aliceKeys);
        await alice.SendAsync(GroupInvitation.Create(key, record), cancellationToken);
        await WaitForAckAsync(alice, GroupNotices.InvitationId(record), cancellationToken);
        await bob.SendAsync(GroupJoin.Create(record), cancellationToken);
        var carolJoin = await WaitForAckJoinAndSummaryAsync(bob, GroupNotices.JoinId(record.Hash, bobKeys.IdentityKey), record.GroupId, cancellationToken);
        await bob.SendAsync(Acknowledgement.Create(GroupNotices.JoinId(carolJoin.RecordHash, Key(carol))), cancellationToken);
        await Eventually.HoldsAsync(() => StateAt(carol, record.GroupId) == GroupState.Created, cancellationToken);

        var genuine = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(1, "From Alice"));
        var authorOffset = GroupId.Length + sizeof(uint);
        var ciphertextOffset = authorOffset + Ed25519PublicKey.Length + GroupKey.NonceLength;
        var tamperedBytes = genuine.ToBytes();
        tamperedBytes[ciphertextOffset] ^= 1;
        Assert.True(GroupMessage.TryParse(tamperedBytes, out var tampered));
        await bob.SendAsync(tampered, cancellationToken);

        var reSignedBytes = GroupMessage.Seal(key, record.GroupId, 1, bobKeys, Text(1, "Pretending to be Alice")).ToBytes();
        aliceKeys.IdentityKey.Bytes.CopyTo(reSignedBytes.AsSpan(authorOffset));
        Assert.True(GroupMessage.TryParse(reSignedBytes, out var reSigned));
        await bob.SendAsync(reSigned, cancellationToken);

        await bob.SendAsync(GroupMessage.Seal(key, record.GroupId, 1, outsiderKeys, Text(1, "From outsider")), cancellationToken);
        using var wrongKey = GroupKey.Create();
        await bob.SendAsync(GroupMessage.Seal(wrongKey, record.GroupId, 1, aliceKeys, Text(1, "Wrong key")), cancellationToken);

        Assert.Null(await bob.NextWithinAsync(TimeSpan.FromMilliseconds(500), cancellationToken));
        Assert.Empty(carol.Messenger.GroupChats.MessagesIn(record.GroupId));

        await bob.SendAsync(genuine, cancellationToken);
        var carolAck = await bob.NextAsync<GroupAcknowledgement>(cancellationToken);
        Assert.Equal(OpenText(genuine, key).Id, carolAck.MessageId);
        Assert.Equal(Key(carol), carolAck.RecipientKey);
        Assert.True(carolAck.HasValidSignature());
        var arrived = Single(carol, record.GroupId);
        Assert.Equal("From Alice", arrived.Text);
        Assert.Equal(aliceKeys.IdentityKey, arrived.AuthorKey);
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-09")]
    public async Task NonMemberContactAndAuthorsOwnEmptySummary_ReceiveNothingAndOutsiderCannotOpenEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        using var aliceKeys = KeySet.Create();
        using var daveKeys = KeySet.Create();
        await bob.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await bob.ImportAsync(ContactCode.Create(daveKeys, Name("Dave")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var bobAtAlice = TestContacts.Create(aliceKeys, bob.Identity.Keys);
        using var bobAtDave = TestContacts.Create(daveKeys, bob.Identity.Keys);
        await using var alice = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAtAlice, cancellationToken);
        await using var dave = await RawContact.OpenAsync(bob.Endpoint, daveKeys, bobAtDave, cancellationToken);
        await Eventually.HoldsAsync(() => bob.ContactFor(aliceKeys).IsAdded && bob.ContactFor(daveKeys).IsAdded, cancellationToken);

        using var key = GroupKey.Create();
        GroupMember[] members =
        [
            new(aliceKeys.IdentityKey, Name("Alice")),
            new(Key(bob), Name("Bob")),
        ];
        var record = GroupRecord.Create(GroupId.Create(), 1, Name("Private group"), key, members, aliceKeys);
        await alice.SendAsync(GroupInvitation.Create(key, record), cancellationToken);
        var bobJoin = await WaitForAckJoinAndSummaryAsync(alice, GroupNotices.InvitationId(record), record.GroupId, cancellationToken);
        await alice.SendAsync(Acknowledgement.Create(GroupNotices.JoinId(bobJoin.RecordHash, Key(bob))), cancellationToken);
        await Eventually.HoldsAsync(() => StateAt(bob, record.GroupId) == GroupState.Created, cancellationToken);

        var fromAlice = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(1, "Members only"));
        await alice.SendAsync(fromAlice, cancellationToken);
        await WaitForGroupAckAsync(alice, OpenText(fromAlice, key).Id, Key(bob), cancellationToken);

        await dave.SendAsync(GroupSummary.Create(record.GroupId, 1, []), cancellationToken);
        Assert.Null(await dave.NextWithinAsync(TimeSpan.FromMilliseconds(500), cancellationToken));

        using var daveKey = GroupKey.Create();
        Assert.False(fromAlice.TryOpen(daveKey, out _));

        await alice.SendAsync(GroupSummary.Create(record.GroupId, 1, []), cancellationToken);
        Assert.Null(await alice.NextWithinAsync(TimeSpan.FromMilliseconds(500), cancellationToken));
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-09")]
    public async Task EarlySummaryBeforeJoinAndInvalidSummariesAndSummarySpam_AreHandledSafely()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fastResend = new DeliveryOptions { ResendAfter = TimeSpan.FromMilliseconds(800) };
        await using var bob = await AppPeer.StartAsync(KeySet.Create(), "Bob", setup => setup with { Delivery = fastResend }, cancellationToken);
        using var aliceKeys = KeySet.Create();
        using var carolKeys = KeySet.Create();
        await bob.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await bob.ImportAsync(ContactCode.Create(carolKeys, Name("Carol")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var bobAtAlice = TestContacts.Create(aliceKeys, bob.Identity.Keys);
        using var bobAtCarol = TestContacts.Create(carolKeys, bob.Identity.Keys);
        await using var alice = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAtAlice, cancellationToken);
        await using var carol = await RawContact.OpenAsync(bob.Endpoint, carolKeys, bobAtCarol, cancellationToken);
        await Eventually.HoldsAsync(() => bob.ContactFor(aliceKeys).IsAdded && bob.ContactFor(carolKeys).IsAdded, cancellationToken);

        using var key = GroupKey.Create();
        GroupMember[] members =
        [
            new(aliceKeys.IdentityKey, Name("Alice")),
            new(Key(bob), Name("Bob")),
            new(carolKeys.IdentityKey, Name("Carol")),
        ];
        var record = GroupRecord.Create(GroupId.Create(), 1, Name("Field team"), key, members, aliceKeys);
        await alice.SendAsync(GroupInvitation.Create(key, record), cancellationToken);
        await WaitForAckAsync(alice, GroupNotices.InvitationId(record), cancellationToken);
        await Eventually.HoldsAsync(() => StateAt(bob, record.GroupId) == GroupState.WaitingForMembers, cancellationToken);

        var fromAlice = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(1, "Before Carol joined"));
        await alice.SendAsync(fromAlice, cancellationToken);
        await WaitForGroupAckAsync(alice, OpenText(fromAlice, key).Id, Key(bob), cancellationToken);

        await carol.SendAsync(GroupSummary.Create(record.GroupId, 1, []), cancellationToken);
        await carol.SendAsync(GroupSummary.Create(GroupId.Create(), 1, []), cancellationToken);
        await carol.SendAsync(GroupSummary.Create(record.GroupId, 2, []), cancellationToken);

        await carol.SendAsync(GroupJoin.Create(record), cancellationToken);
        await Eventually.HoldsAsync(() => StateAt(bob, record.GroupId) == GroupState.Created, cancellationToken);

        var passed = await carol.NextAsync<GroupMessage>(cancellationToken);
        Assert.Equal(fromAlice, passed);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await carol.SendAsync(GroupSummary.Create(record.GroupId, 1, []), cancellationToken);
        }

        await carol.SendAsync(GroupSummary.Create(record.GroupId, 1, [new GroupSummaryAuthor(carolKeys.IdentityKey, [new SequenceRange(99, 99)])]), cancellationToken);
        Assert.Null(await NextGroupMessageWithinAsync(carol, TimeSpan.FromMilliseconds(350), cancellationToken));

        await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);
        await carol.SendAsync(GroupSummary.Create(record.GroupId, 1, []), cancellationToken);
        var passedAgain = await carol.NextAsync<GroupMessage>(cancellationToken);
        Assert.Equal(fromAlice, passedAgain);

        await carol.SendAsync(GroupAcknowledgement.Create(record.GroupId, 1, OpenText(fromAlice, key).Id, carolKeys), cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);
        await carol.SendAsync(GroupSummary.Create(record.GroupId, 1, []), cancellationToken);
        Assert.Null(await NextGroupMessageWithinAsync(carol, TimeSpan.FromMilliseconds(350), cancellationToken));

        await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);
        for (var burst = 1; burst <= 4; burst++)
        {
            await carol.SendAsync(
                GroupSummary.Create(record.GroupId, 1, [new GroupSummaryAuthor(carolKeys.IdentityKey, [new SequenceRange(burst, burst)])]),
                cancellationToken);
        }

        Assert.True(TextMessage.TryCreate(MessageId.Create(), 1, DateTimeOffset.UnixEpoch.AddMinutes(1), "sync", out var syncMessage));
        await carol.SendAsync(syncMessage, cancellationToken);
        while (!(await carol.NextAsync<Acknowledgement>(cancellationToken)).Id.Equals(syncMessage.Id))
        {
        }

        var fromAlice2 = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(2, "After burst"));
        await alice.SendAsync(fromAlice2, cancellationToken);
        await WaitForGroupAckAsync(alice, OpenText(fromAlice2, key).Id, Key(bob), cancellationToken);
        await carol.SendAsync(GroupSummary.Create(record.GroupId, 1, [new GroupSummaryAuthor(aliceKeys.IdentityKey, [new SequenceRange(1, 1)])]), cancellationToken);
        Assert.Null(await NextGroupMessageWithinAsync(carol, TimeSpan.FromMilliseconds(350), cancellationToken));

        await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);
        await carol.SendAsync(GroupSummary.Create(record.GroupId, 1, [new GroupSummaryAuthor(aliceKeys.IdentityKey, [new SequenceRange(1, 1)])]), cancellationToken);
        var passedAfterRefill = await carol.NextAsync<GroupMessage>(cancellationToken);
        Assert.Equal(fromAlice2, passedAfterRefill);
        Assert.True(bob.Messenger.Connections.IsConnected(carolKeys.IdentityKey));
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-04")]
    public async Task FullyConnectedGroupOfThree_FiveMessages_SendsNoDuplicateGroupMessagesAndRelaysMissedMessageViaDelayedSummary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fastDelay = new DeliveryOptions { PassOnSummaryDelay = TimeSpan.FromMilliseconds(400) };
        await using var bob = await AppPeer.StartAsync(KeySet.Create(), "Bob", setup => setup with { Delivery = fastDelay }, cancellationToken);
        using var aliceKeys = KeySet.Create();
        using var carolKeys = KeySet.Create();
        await bob.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await bob.ImportAsync(ContactCode.Create(carolKeys, Name("Carol")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var bobAtAlice = TestContacts.Create(aliceKeys, bob.Identity.Keys);
        using var bobAtCarol = TestContacts.Create(carolKeys, bob.Identity.Keys);
        await using var alice = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAtAlice, cancellationToken);
        await using var carol = await RawContact.OpenAsync(bob.Endpoint, carolKeys, bobAtCarol, cancellationToken);
        await Eventually.HoldsAsync(() => bob.ContactFor(aliceKeys).IsAdded && bob.ContactFor(carolKeys).IsAdded, cancellationToken);

        using var key = GroupKey.Create();
        GroupMember[] members =
        [
            new(aliceKeys.IdentityKey, Name("Alice")),
            new(Key(bob), Name("Bob")),
            new(carolKeys.IdentityKey, Name("Carol")),
        ];
        var record = GroupRecord.Create(GroupId.Create(), 1, Name("Field team"), key, members, aliceKeys);
        await alice.SendAsync(GroupInvitation.Create(key, record), cancellationToken);
        await WaitForAckAsync(alice, GroupNotices.InvitationId(record), cancellationToken);
        await carol.SendAsync(GroupJoin.Create(record), cancellationToken);
        var bobJoin = await WaitForAckJoinAndSummaryAsync(carol, GroupNotices.JoinId(record.Hash, carolKeys.IdentityKey), record.GroupId, cancellationToken);
        await carol.SendAsync(Acknowledgement.Create(GroupNotices.JoinId(bobJoin.RecordHash, Key(bob))), cancellationToken);
        await Eventually.HoldsAsync(
            () => StateAt(bob, record.GroupId) == GroupState.Created
                && bob.Messenger.Groups.TryGet(record.GroupId, out var group)
                && group.Find(carolKeys.IdentityKey)?.OwnJoin == NoticeState.Received,
            cancellationToken);

        for (var sequence = 1; sequence <= 5; sequence++)
        {
            var burstMessage = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(sequence, $"Message {sequence}"));
            await alice.SendAsync(burstMessage, cancellationToken);
            await WaitForGroupAckAsync(alice, OpenText(burstMessage, key).Id, Key(bob), cancellationToken);
        }

        List<SessionMessage> fromBobDuringBurst = [];
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromMilliseconds(1_200))
        {
            var next = await carol.NextWithinAsync(TimeSpan.FromMilliseconds(1_200) - deadline.Elapsed, cancellationToken);
            if (next is null)
            {
                break;
            }

            if (next is not KeepAlive)
            {
                fromBobDuringBurst.Add(next);
            }
        }

        Assert.DoesNotContain(fromBobDuringBurst, static message => message is GroupMessage);
        Assert.NotEmpty(fromBobDuringBurst);
        Assert.True(fromBobDuringBurst.Count < 5);
        Assert.All(fromBobDuringBurst, static message => Assert.IsType<GroupSummary>(message));
        var burstSummary = Assert.IsType<GroupSummary>(fromBobDuringBurst[^1]);
        for (var sequence = 1; sequence <= 5; sequence++)
        {
            Assert.True(burstSummary.Contains(aliceKeys.IdentityKey, sequence));
        }

        var missed = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(6, "Missed while partitioned"));
        await alice.SendAsync(missed, cancellationToken);
        await WaitForGroupAckAsync(alice, OpenText(missed, key).Id, Key(bob), cancellationToken);

        var nextSummary = await carol.NextAsync<GroupSummary>(cancellationToken);
        Assert.True(nextSummary.Contains(aliceKeys.IdentityKey, 6));
        await carol.SendAsync(GroupSummary.Create(record.GroupId, 1, [new GroupSummaryAuthor(aliceKeys.IdentityKey, [new SequenceRange(1, 5)])]), cancellationToken);

        var relayed = await carol.NextAsync<GroupMessage>(cancellationToken);
        Assert.Equal(missed, relayed);
        Assert.Null(await NextGroupMessageWithinAsync(carol, TimeSpan.FromMilliseconds(350), cancellationToken));
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-13")]
    [Trait("Story", "US-15")]
    public async Task Outbox_OwnWaitingTextAndGroupMessage_AreSentBeforeGroupSummary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        using var aliceKeys = KeySet.Create();
        await bob.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var bobAtAlice = TestContacts.Create(aliceKeys, bob.Identity.Keys);
        using var key = GroupKey.Create();
        GroupMember[] members =
        [
            new(aliceKeys.IdentityKey, Name("Alice")),
            new(Key(bob), Name("Bob")),
        ];
        var record = GroupRecord.Create(GroupId.Create(), 1, Name("Pair group"), key, members, aliceKeys);

        await using (var alice = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAtAlice, cancellationToken))
        {
            await Eventually.HoldsAsync(() => bob.ContactFor(aliceKeys).IsAdded, cancellationToken);
            await alice.SendAsync(GroupInvitation.Create(key, record), cancellationToken);
            var join = await WaitForAckJoinAndSummaryAsync(alice, GroupNotices.InvitationId(record), record.GroupId, cancellationToken);
            await alice.SendAsync(Acknowledgement.Create(GroupNotices.JoinId(join.RecordHash, Key(bob))), cancellationToken);
            await Eventually.HoldsAsync(
                () => StateAt(bob, record.GroupId) == GroupState.Created
                    && bob.Messenger.Groups.TryGet(record.GroupId, out var group)
                    && group.Find(aliceKeys.IdentityKey)?.OwnJoin == NoticeState.Received,
                cancellationToken);
        }

        await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(aliceKeys.IdentityKey), cancellationToken);
        Assert.Equal(SendOutcome.Queued, (await bob.Messenger.SendAsync(aliceKeys.IdentityKey, "Direct first", cancellationToken)).Outcome);
        Assert.Equal(GroupSendOutcome.Queued, (await bob.Messenger.SendToGroupAsync(record.GroupId, "Own group second", cancellationToken)).Outcome);

        await using var reconnected = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAtAlice, cancellationToken);
        List<SessionMessage> order = [];
        while (order.Count < 3)
        {
            var next = await reconnected.NextWithinAsync(TimeSpan.FromSeconds(5), cancellationToken);
            Assert.NotNull(next);
            if (next is not KeepAlive)
            {
                order.Add(next);
            }
        }

        var direct = Assert.IsType<TextMessage>(order[0]);
        Assert.Equal("Direct first", direct.Text);
        Assert.IsType<GroupMessage>(order[1]);
        Assert.IsType<GroupSummary>(order[2]);
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-09")]
    public async Task SummaryWithUnknownAuthorEntry_IgnoresUnknownAuthorAndStillPassesCurrentMembersMissingMessages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        using var aliceKeys = KeySet.Create();
        using var carolKeys = KeySet.Create();
        using var removedKeys = KeySet.Create();
        await bob.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await bob.ImportAsync(ContactCode.Create(carolKeys, Name("Carol")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var bobAtAlice = TestContacts.Create(aliceKeys, bob.Identity.Keys);
        using var bobAtCarol = TestContacts.Create(carolKeys, bob.Identity.Keys);
        await using var alice = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAtAlice, cancellationToken);
        await using var carol = await RawContact.OpenAsync(bob.Endpoint, carolKeys, bobAtCarol, cancellationToken);
        await Eventually.HoldsAsync(() => bob.ContactFor(aliceKeys).IsAdded && bob.ContactFor(carolKeys).IsAdded, cancellationToken);

        using var key = GroupKey.Create();
        GroupMember[] members =
        [
            new(aliceKeys.IdentityKey, Name("Alice")),
            new(Key(bob), Name("Bob")),
            new(carolKeys.IdentityKey, Name("Carol")),
        ];
        var record = GroupRecord.Create(GroupId.Create(), 1, Name("Field team"), key, members, aliceKeys);
        await alice.SendAsync(GroupInvitation.Create(key, record), cancellationToken);
        await WaitForAckAsync(alice, GroupNotices.InvitationId(record), cancellationToken);
        await carol.SendAsync(GroupJoin.Create(record), cancellationToken);
        var bobJoin = await WaitForAckJoinAndSummaryAsync(carol, GroupNotices.JoinId(record.Hash, carolKeys.IdentityKey), record.GroupId, cancellationToken);
        await carol.SendAsync(Acknowledgement.Create(GroupNotices.JoinId(bobJoin.RecordHash, Key(bob))), cancellationToken);
        await Eventually.HoldsAsync(() => StateAt(bob, record.GroupId) == GroupState.Created, cancellationToken);

        var first = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(1, "Already held by Carol"));
        var second = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(2, "Missing at Carol"));
        await alice.SendAsync(first, cancellationToken);
        await WaitForGroupAckAsync(alice, OpenText(first, key).Id, Key(bob), cancellationToken);
        await alice.SendAsync(second, cancellationToken);
        await WaitForGroupAckAsync(alice, OpenText(second, key).Id, Key(bob), cancellationToken);

        var summaryWithRemovedAuthor = GroupSummary.Create(
            record.GroupId,
            1,
            [
                new GroupSummaryAuthor(removedKeys.IdentityKey, [new SequenceRange(1, 3)]),
                new GroupSummaryAuthor(aliceKeys.IdentityKey, [new SequenceRange(1, 1)]),
            ]);
        await carol.SendAsync(summaryWithRemovedAuthor, cancellationToken);

        var passed = await carol.NextAsync<GroupMessage>(cancellationToken);
        Assert.Equal(second, passed);
        Assert.Null(await NextGroupMessageWithinAsync(carol, TimeSpan.FromMilliseconds(350), cancellationToken));
    }

    private static Ed25519PublicKey Key(AppPeer peer) => peer.Identity.Keys.IdentityKey;

    private static GroupState? StateAt(AppPeer peer, GroupId groupId) =>
        peer.Messenger.Groups.TryGet(groupId, out var group) ? group.State : null;

    private static bool IsCreatedAt(GroupId groupId, params AppPeer[] peers) =>
        peers.All(peer => StateAt(peer, groupId) == GroupState.Created);

    private static GroupChatMessage Single(AppPeer peer, GroupId groupId) => Assert.Single(peer.Messenger.GroupChats.MessagesIn(groupId));

    private static GroupChatMessage SingleOutgoing(AppPeer peer, GroupId groupId) =>
        Assert.Single(peer.Messenger.GroupChats.MessagesIn(groupId), static message => message.Direction == MessageDirection.Outgoing);

    private static DisplayName Name(string value) => DisplayName.TryCreate(value, out var name) ? name : throw new InvalidOperationException();

    private static GroupText Text(long sequence, string text) =>
        GroupText.TryCreate(MessageId.Create(), sequence, DateTimeOffset.UnixEpoch.AddMinutes(sequence), text, out var groupText) ? groupText : throw new InvalidOperationException();

    private static GroupText OpenText(GroupMessage message, GroupKey key) =>
        message.TryOpen(key, out var text) ? text : throw new InvalidOperationException();

    private static async Task WaitForAckAsync(RawContact contact, MessageId expectedId, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        var limit = TimeSpan.FromSeconds(20);
        while (deadline.Elapsed < limit)
        {
            var remaining = limit - deadline.Elapsed;
            var message = await contact.NextWithinAsync(remaining, cancellationToken);
            if (message is null)
            {
                break;
            }

            if (message is Acknowledgement ack && ack.Id.Equals(expectedId))
            {
                return;
            }
        }

        Assert.Fail($"Expected acknowledgement for {expectedId} within 20 s.");
    }

    private static async Task WaitForGroupAckAsync(
        RawContact contact,
        MessageId expectedMessageId,
        Ed25519PublicKey expectedRecipientKey,
        CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        var limit = TimeSpan.FromSeconds(20);
        while (deadline.Elapsed < limit)
        {
            var remaining = limit - deadline.Elapsed;
            var message = await contact.NextWithinAsync(remaining, cancellationToken);
            if (message is null)
            {
                break;
            }

            if (message is GroupAcknowledgement ack
                && ack.MessageId.Equals(expectedMessageId)
                && ack.RecipientKey.Equals(expectedRecipientKey))
            {
                Assert.True(ack.HasValidSignature());
                return;
            }
        }

        Assert.Fail($"Expected signed group acknowledgement for {expectedMessageId} within 20 s.");
    }

    private static async Task<GroupJoin> WaitForAckJoinAndSummaryAsync(RawContact contact, MessageId expectedAckId, GroupId groupId, CancellationToken cancellationToken)
    {
        var seenAck = false;
        GroupJoin? join = null;
        var seenSummary = false;
        var deadline = Stopwatch.StartNew();
        var limit = TimeSpan.FromSeconds(20);
        while (!(seenAck && join is not null && seenSummary) && deadline.Elapsed < limit)
        {
            var remaining = limit - deadline.Elapsed;
            var message = await contact.NextWithinAsync(remaining, cancellationToken);
            if (message is null)
            {
                break;
            }

            switch (message)
            {
                case Acknowledgement ack when ack.Id.Equals(expectedAckId):
                    seenAck = true;
                    break;
                case GroupJoin groupJoin when groupJoin.GroupId.Equals(groupId):
                    join = groupJoin;
                    break;
                case GroupSummary summary when summary.GroupId.Equals(groupId):
                    seenSummary = true;
                    break;
            }
        }

        Assert.True(seenAck, $"Expected acknowledgement for {expectedAckId} within 20 s.");
        Assert.NotNull(join);
        Assert.True(seenSummary, "Expected initial GroupSummary within 20 s.");
        return join;
    }

    private static async Task<GroupMessage?> NextGroupMessageWithinAsync(RawContact contact, TimeSpan limit, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < limit)
        {
            var remaining = limit - deadline.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            var message = await contact.NextWithinAsync(remaining, cancellationToken);
            if (message is null)
            {
                return null;
            }

            if (message is GroupMessage groupMessage)
            {
                return groupMessage;
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
