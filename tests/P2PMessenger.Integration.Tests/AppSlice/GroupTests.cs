using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Groups;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Integration.Tests.Connections;

namespace P2PMessenger.Integration.Tests.AppSlice;

public sealed class GroupTests
{
    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-12")]
    [Trait("Story", "US-11")]
    public async Task ThreeMembersWhoAddedEachOther_CreateAGroupAndChat_EveryoneSeesTheSameMembersAndEachMessageWithItsAuthor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await AppPeer.StartAsync("Alice", cancellationToken);
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        await using var carol = await AppPeer.StartAsync("Carol", cancellationToken);
        await AddEachOtherAsync(cancellationToken, alice, bob, carol);

        var created = await alice.Messenger.CreateGroupAsync("Field team", [Key(bob), Key(carol)], cancellationToken);
        Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
        var groupId = created.Group!.Id;

        await Eventually.HoldsAsync(() => StateAt(alice, groupId) == GroupState.Created && StateAt(bob, groupId) == GroupState.Created && StateAt(carol, groupId) == GroupState.Created, cancellationToken);
        foreach (var peer in new[] { alice, bob, carol })
        {
            Assert.True(peer.Messenger.Groups.TryGet(groupId, out var group));
            Assert.Equal("Field team", group.Name.Value);
            Assert.Equal(
                new[] { Key(alice), Key(bob), Key(carol) }.Select(static key => Convert.ToHexString(key.Bytes)).Order(StringComparer.Ordinal),
                group.Members.Select(static member => Convert.ToHexString(member.IdentityKey.Bytes)));
            Assert.Equal(created.Group.Record, group.Record);
        }

        var sent = await alice.Messenger.SendToGroupAsync(groupId, "Meet at the bridge at six", cancellationToken);
        Assert.Equal(GroupSendOutcome.Queued, sent.Outcome);
        Assert.Equal(2, sent.Message!.RecipientCount);
        Assert.Equal(0, sent.Message.ReceivedCount);

        await Eventually.HoldsAsync(() => Single(alice, groupId).ReceivedCount == 2, cancellationToken);
        foreach (var peer in new[] { bob, carol })
        {
            var received = Assert.Single(peer.Messenger.GroupChats.MessagesIn(groupId));
            Assert.Equal("Meet at the bridge at six", received.Text);
            Assert.Equal(Key(alice), received.AuthorKey);
            Assert.Equal(MessageDirection.Incoming, received.Direction);
            Assert.Equal(sent.Message.Id, received.Id);
            Assert.Equal(1, peer.Messenger.GroupChats.UnreadCount(groupId));
        }

        Assert.Equal(GroupSendOutcome.Queued, (await bob.Messenger.SendToGroupAsync(groupId, "On my way", cancellationToken)).Outcome);
        await Eventually.HoldsAsync(() => carol.Messenger.GroupChats.MessagesIn(groupId).Count == 2 && alice.Messenger.GroupChats.MessagesIn(groupId).Count == 2, cancellationToken);
        Assert.Equal(Key(bob), carol.Messenger.GroupChats.MessagesIn(groupId)[1].AuthorKey);
        Assert.Empty(alice.Messenger.Chats.MessagesWith(Key(bob)));
        Assert.Empty(carol.Messenger.Chats.MessagesWith(Key(bob)));
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-12")]
    public async Task InviteeWhoHasNotAddedAMember_IsShownWhomToAddAndJoinsOnceTheyHave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await AppPeer.StartAsync("Alice", cancellationToken);
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        await using var carol = await AppPeer.StartAsync("Carol", cancellationToken);
        await AddEachOtherAsync(cancellationToken, alice, bob);
        await AddEachOtherAsync(cancellationToken, alice, carol);

        var groupId = (await alice.Messenger.CreateGroupAsync("Hikers", [Key(bob), Key(carol)], cancellationToken)).Group!.Id;
        await Eventually.HoldsAsync(() => bob.Messenger.Groups.TryGet(groupId, out _) && carol.Messenger.Groups.TryGet(groupId, out _), cancellationToken);

        Assert.True(bob.Messenger.Groups.TryGet(groupId, out var atBob));
        Assert.Equal(GroupState.Joining, atBob.State);
        var toAdd = Assert.Single(bob.Messenger.Groups.MembersToAdd(atBob));
        Assert.Equal(Key(carol), toAdd.IdentityKey);
        Assert.Equal("Carol", toAdd.Name.Value);
        Assert.Equal(GroupState.WaitingForMembers, StateAt(alice, groupId));
        Assert.Equal(GroupSendOutcome.NotCreatedYet, (await alice.Messenger.SendToGroupAsync(groupId, "too early", cancellationToken)).Outcome);
        Assert.Equal(GroupSendOutcome.NotCreatedYet, (await bob.Messenger.SendToGroupAsync(groupId, "too early", cancellationToken)).Outcome);

        await AddEachOtherAsync(cancellationToken, bob, carol);

        await Eventually.HoldsAsync(() => StateAt(alice, groupId) == GroupState.Created && StateAt(bob, groupId) == GroupState.Created && StateAt(carol, groupId) == GroupState.Created, cancellationToken);
        Assert.True(bob.Messenger.Groups.TryGet(groupId, out atBob));
        Assert.Empty(bob.Messenger.Groups.MembersToAdd(atBob));
        Assert.Equal(GroupSendOutcome.Queued, (await carol.Messenger.SendToGroupAsync(groupId, "Joined at last", cancellationToken)).Outcome);
        await Eventually.HoldsAsync(() => alice.Messenger.GroupChats.MessagesIn(groupId).Count == 1 && bob.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-12")]
    [Trait("Story", "US-15")]
    [Trait("NFR", "NFR-03")]
    [Trait("NFR", "NFR-06")]
    [Trait("NFR", "NFR-07")]
    public async Task GroupAndAWaitingMessage_SurviveRestartsAndTheMessageGoesOutWhenTheMemberIsBack()
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
            var groupId = (await alice.Messenger.CreateGroupAsync("Saved team", [Key(bob), Key(carol)], cancellationToken)).Group!.Id;
            await Eventually.HoldsAsync(() => StateAt(alice, groupId) == GroupState.Created && StateAt(bob, groupId) == GroupState.Created && StateAt(carol, groupId) == GroupState.Created, cancellationToken);

            var carolKey = Key(carol);
            await carol.DisposeAsync();
            await Eventually.HoldsAsync(() => !alice.Messenger.Connections.IsConnected(carolKey), cancellationToken);
            var sent = (await alice.Messenger.SendToGroupAsync(groupId, "Carol, you will get this later", cancellationToken)).Message!;
            await Eventually.HoldsAsync(() => Single(alice, groupId).ReceivedCount == 1, cancellationToken);
            Assert.True(Single(alice, groupId).IsWaitingFor(carolKey));

            await alice.DisposeAsync();
            alice = await AppPeer.StartSavedAsync(paths[0], protectors[0], "Alice", cancellationToken);
            Assert.Equal(GroupState.Created, StateAt(alice, groupId));
            var restored = Single(alice, groupId);
            Assert.Equal(sent.Id, restored.Id);
            Assert.Equal(1, restored.ReceivedCount);
            Assert.True(restored.IsWaitingFor(carolKey));

            carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
            Assert.Equal(GroupState.Created, StateAt(carol, groupId));
            await Eventually.HoldsAsync(() => Single(alice, groupId).ReceivedCount == 2, cancellationToken);
            Assert.Equal("Carol, you will get this later", Single(carol, groupId).Text);

            await carol.DisposeAsync();
            carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
            Assert.Equal(sent.Id, Single(carol, groupId).Id);
            Assert.Equal(Key(alice), Single(carol, groupId).AuthorKey);
        }
        finally
        {
            await alice.DisposeAsync();
            await bob.DisposeAsync();
            await carol.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-12")]
    [Trait("NFR", "NFR-06")]
    public async Task DeletedGroupChat_StaysDeletedAfterARestart_WhileTheGroupAndTheNumbersOfYourMessagesGoOn()
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
            var groupId = (await alice.Messenger.CreateGroupAsync("Saved team", [Key(bob), Key(carol)], cancellationToken)).Group!.Id;
            await Eventually.HoldsAsync(() => StateAt(alice, groupId) == GroupState.Created && StateAt(bob, groupId) == GroupState.Created && StateAt(carol, groupId) == GroupState.Created, cancellationToken);
            await alice.Messenger.SendToGroupAsync(groupId, "one", cancellationToken);
            await alice.Messenger.SendToGroupAsync(groupId, "two", cancellationToken);
            await bob.Messenger.SendToGroupAsync(groupId, "from Bob", cancellationToken);
            await Eventually.HoldsAsync(
                () => alice.Messenger.GroupChats.MessagesIn(groupId).Count == 3
                    && alice.Messenger.GroupChats.MessagesIn(groupId).Where(static message => message.Direction == MessageDirection.Outgoing).All(static message => message.ReceivedCount == 2)
                    && carol.Messenger.GroupChats.MessagesIn(groupId).Count == 3,
                cancellationToken);

            Assert.Equal(3, await alice.Messenger.DeleteGroupChatAsync(groupId, cancellationToken));
            await alice.DisposeAsync();
            alice = await AppPeer.StartSavedAsync(paths[0], protectors[0], "Alice", cancellationToken);

            Assert.Empty(alice.Messenger.GroupChats.MessagesIn(groupId));
            Assert.Equal(GroupState.Created, StateAt(alice, groupId));
            var three = (await alice.Messenger.SendToGroupAsync(groupId, "three", cancellationToken)).Message!;
            Assert.Equal(3, three.Sequence);
            await Eventually.HoldsAsync(() => Single(alice, groupId).ReceivedCount == 2, cancellationToken);
            foreach (var member in new[] { bob, carol })
            {
                var texts = member.Messenger.GroupChats.MessagesIn(groupId);
                Assert.Equal(["from Bob", "one", "three", "two"], texts.Select(static message => message.Text).Order(StringComparer.Ordinal));
                Assert.Equal(["one", "two", "three"], texts.Where(message => message.AuthorKey.Equals(Key(alice))).Select(static message => message.Text));
            }

            Assert.Equal("three", Single(alice, groupId).Text);
        }
        finally
        {
            await alice.DisposeAsync();
            await bob.DisposeAsync();
            await carol.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-12")]
    public async Task CreateGroup_WithoutAValidNameOrMembersOrWithAContactNotYetAdded_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await AppPeer.StartAsync("Alice", cancellationToken);
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        await using var carol = await AppPeer.StartAsync("Carol", cancellationToken);
        await AddEachOtherAsync(cancellationToken, alice, bob);
        await alice.ImportAsync(carol, cancellationToken);

        Assert.Equal(CreateGroupOutcome.NameNotValid, (await alice.Messenger.CreateGroupAsync("   ", [Key(bob)], cancellationToken)).Outcome);
        Assert.Equal(CreateGroupOutcome.NoMembers, (await alice.Messenger.CreateGroupAsync("Team", [], cancellationToken)).Outcome);
        Assert.Equal(CreateGroupOutcome.NoMembers, (await alice.Messenger.CreateGroupAsync("Team", [Key(alice)], cancellationToken)).Outcome);
        Assert.Equal(CreateGroupOutcome.NotAnAddedContact, (await alice.Messenger.CreateGroupAsync("Team", [Key(bob), Key(carol)], cancellationToken)).Outcome);
        Assert.Empty(alice.Messenger.Groups.All);
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-12")]
    [Trait("NFR", "NFR-09")]
    public async Task InvitationNotSignedByItsSender_AndGroupMessagesFromOutsiders_AreNotTaken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        using var aliceKeys = KeySet.Create();
        using var carolKeys = KeySet.Create();
        using var daveKeys = KeySet.Create();
        await bob.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var bobAtAlice = TestContacts.Create(aliceKeys, bob.Identity.Keys);
        await using var alice = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAtAlice, cancellationToken);
        await Eventually.HoldsAsync(() => bob.ContactFor(aliceKeys).IsAdded, cancellationToken);

        using var key = GroupKey.Create();
        GroupMember[] members = [new(aliceKeys.IdentityKey, Name("Alice")), new(Key(bob), Name("Bob")), new(carolKeys.IdentityKey, Name("Carol"))];
        var signedByCarol = GroupRecord.Create(GroupId.Create(), 1, Name("Forged"), key, members, carolKeys);
        var forged = GroupInvitation.Create(key, signedByCarol);
        await alice.SendAsync(forged, cancellationToken);
        Assert.Equal(forged.NoticeId, (await alice.NextAsync<Acknowledgement>(cancellationToken)).Id);
        Assert.Empty(bob.Messenger.Groups.All);

        var record = GroupRecord.Create(GroupId.Create(), 1, Name("Real"), key, members, aliceKeys);
        var invitation = GroupInvitation.Create(key, record);
        await alice.SendAsync(invitation, cancellationToken);
        Assert.Equal(invitation.NoticeId, (await alice.NextAsync<Acknowledgement>(cancellationToken)).Id);
        Assert.True(bob.Messenger.Groups.TryGet(record.GroupId, out var group));
        Assert.Equal(GroupState.Joining, group.State);

        var fromDave = GroupMessage.Seal(key, record.GroupId, 1, daveKeys, Text(1, "I am not a member"));
        await alice.SendAsync(fromDave, cancellationToken);
        var fromAlice = GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(1, "Welcome"));
        var welcome = OpenText(fromAlice, key);
        await alice.SendAsync(fromAlice, cancellationToken);
        Assert.Equal(welcome.Id, (await alice.NextAsync<Acknowledgement>(cancellationToken)).Id);

        using var otherKey = GroupKey.Create();
        await alice.SendAsync(GroupMessage.Seal(otherKey, record.GroupId, 1, aliceKeys, Text(2, "under another key")), cancellationToken);
        await alice.SendAsync(GroupMessage.Seal(key, GroupId.Create(), 1, aliceKeys, Text(3, "in an unknown group")), cancellationToken);
        await alice.SendAsync(GroupMessage.Seal(key, record.GroupId, 1, aliceKeys, Text(1, "the same sequence again")), cancellationToken);
        await alice.SendAsync(fromAlice, cancellationToken);
        Assert.Equal(welcome.Id, (await alice.NextAsync<Acknowledgement>(cancellationToken)).Id);
        Assert.Null(await alice.NextWithinAsync(TimeSpan.FromMilliseconds(500), cancellationToken));

        var shown = Assert.Single(bob.Messenger.GroupChats.MessagesIn(record.GroupId));
        Assert.Equal("Welcome", shown.Text);
        Assert.Equal(aliceKeys.IdentityKey, shown.AuthorKey);
        Assert.Equal(1, bob.Messenger.GroupChats.UnreadCount(record.GroupId));
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-12")]
    [Trait("NFR", "NFR-09")]
    public async Task CreatorWhoSentDifferentMemberListsToDifferentMembers_LeavesTheGroupUncreatedAndTheListsShownAsDifferent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        using var aliceKeys = KeySet.Create();
        using var carolKeys = KeySet.Create();
        using var eveKeys = KeySet.Create();
        await bob.ImportAsync(ContactCode.Create(aliceKeys, Name("Alice")).ToText(), CodeCheckOutcome.New, cancellationToken);
        await bob.ImportAsync(ContactCode.Create(carolKeys, Name("Carol")).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var bobAtAlice = TestContacts.Create(aliceKeys, bob.Identity.Keys);
        using var bobAtCarol = TestContacts.Create(carolKeys, bob.Identity.Keys);
        await using var alice = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAtAlice, cancellationToken);
        await using var carol = await RawContact.OpenAsync(bob.Endpoint, carolKeys, bobAtCarol, cancellationToken);
        await Eventually.HoldsAsync(() => bob.ContactFor(aliceKeys).IsAdded && bob.ContactFor(carolKeys).IsAdded, cancellationToken);

        using var key = GroupKey.Create();
        var groupId = GroupId.Create();
        var forBob = GroupRecord.Create(groupId, 1, Name("Team"), key, [new(aliceKeys.IdentityKey, Name("Alice")), new(Key(bob), Name("Bob")), new(carolKeys.IdentityKey, Name("Carol"))], aliceKeys);
        var forCarol = GroupRecord.Create(groupId, 1, Name("Team"), key, [new(aliceKeys.IdentityKey, Name("Alice")), new(Key(bob), Name("Bob")), new(carolKeys.IdentityKey, Name("Carol")), new(eveKeys.IdentityKey, Name("Eve"))], aliceKeys);
        await alice.SendAsync(GroupInvitation.Create(key, forBob), cancellationToken);
        await alice.NextAsync<Acknowledgement>(cancellationToken);
        var bobsJoin = await alice.NextAsync<GroupJoin>(cancellationToken);
        Assert.True(bobsJoin.RecordHash.SequenceEqual(forBob.Hash));

        await carol.SendAsync(GroupJoin.Create(forCarol), cancellationToken);
        Assert.Equal(GroupNotices.JoinId(forCarol.Hash, carolKeys.IdentityKey), (await carol.NextAsync<Acknowledgement>(cancellationToken)).Id);

        await Eventually.HoldsAsync(() => StateAt(bob, groupId) == GroupState.ListsDiffer, cancellationToken);
        Assert.True(bob.Messenger.Groups.TryGet(groupId, out var group));
        Assert.Equal(MemberJoin.JoinedAnotherList, group.Find(carolKeys.IdentityKey)!.Joined);
        Assert.Equal(GroupSendOutcome.NotCreatedYet, (await bob.Messenger.SendToGroupAsync(groupId, "Hello?", cancellationToken)).Outcome);
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-12")]
    public async Task JoinThatArrivesBeforeTheInvitation_CountsOnceTheInvitationArrives()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
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
        var record = GroupRecord.Create(GroupId.Create(), 1, Name("Team"), key, [new(aliceKeys.IdentityKey, Name("Alice")), new(Key(bob), Name("Bob")), new(carolKeys.IdentityKey, Name("Carol"))], aliceKeys);

        await carol.SendAsync(GroupJoin.Create(record), cancellationToken);
        Assert.Null(await carol.NextWithinAsync(TimeSpan.FromMilliseconds(500), cancellationToken));
        await alice.SendAsync(GroupInvitation.Create(key, record), cancellationToken);
        await alice.NextAsync<Acknowledgement>(cancellationToken);

        await Eventually.HoldsAsync(() => StateAt(bob, record.GroupId) == GroupState.Created, cancellationToken);
        Assert.True((await carol.NextAsync<GroupJoin>(cancellationToken)).RecordHash.SequenceEqual(record.Hash));
        await carol.SendAsync(GroupJoin.Create(record), cancellationToken);
        Assert.Equal(GroupNotices.JoinId(record.Hash, carolKeys.IdentityKey), (await carol.NextAsync<Acknowledgement>(cancellationToken)).Id);
    }

    private static Ed25519PublicKey Key(AppPeer peer) => peer.Identity.Keys.IdentityKey;

    private static GroupState? StateAt(AppPeer peer, GroupId groupId) =>
        peer.Messenger.Groups.TryGet(groupId, out var group) ? group.State : null;

    private static GroupChatMessage Single(AppPeer peer, GroupId groupId) => Assert.Single(peer.Messenger.GroupChats.MessagesIn(groupId));

    private static DisplayName Name(string value) => DisplayName.TryCreate(value, out var name) ? name : throw new InvalidOperationException();

    private static GroupText Text(long sequence, string text) =>
        GroupText.TryCreate(MessageId.Create(), sequence, DateTimeOffset.UnixEpoch, text, out var groupText) ? groupText : throw new InvalidOperationException();

    private static GroupText OpenText(GroupMessage message, GroupKey key) =>
        message.TryOpen(key, out var text) ? text : throw new InvalidOperationException();

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
}
