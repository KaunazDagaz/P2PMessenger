using System.Net;
using System.Security.Cryptography;
using P2PMessenger.Application.Copy;
using P2PMessenger.Application.Groups;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Identity;
using P2PMessenger.Storage;
using P2PMessenger.Storage.Protection;
using P2PMessenger.Tests.Shared;
using P2PMessenger.Transport.Copy;

namespace P2PMessenger.Application.Tests.Copy;

public sealed class PcCopyServiceTests
{
    private static readonly Ipv4Address Loopback = CreateLoopback();

    [Fact]
    [Trait("Story", "US-22")]
    public async Task ListCandidates_IncludesOnlyAddedContactsAndCreatedGroupsWithMessageCounts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        await using var phone = await StartPeerAsync("Alice", hub.CreateNode(), cancellationToken);
        await using var bob = await StartPeerAsync("Bob", hub.CreateNode(), cancellationToken);
        using var waitingKey = KeySet.Create();

        Assert.True(DisplayName.TryCreate("Waiting", out var waitingName));
        Assert.True((await phone.Contacts.AddAsync(phone.Contacts.Check(ContactCode.Create(waitingKey, waitingName)), cancellationToken)).IsSaved);
        await ConnectAsync(phone, bob, cancellationToken);
        Assert.True((await phone.SendAsync(bob.Identity.Keys.IdentityKey, "Hello Bob", cancellationToken)).IsQueued);
        var groupCreated = await phone.CreateGroupAsync("Study", [bob.Identity.Keys.IdentityKey], cancellationToken);
        Assert.True(groupCreated.IsCreated);
        await TestMessengers.WaitUntilAsync(
            () => phone.Groups.TryGet(groupCreated.Group.Id, out var group) && group.State == GroupState.Created,
            cancellationToken);
        Assert.True((await phone.SendToGroupAsync(groupCreated.Group.Id, "Group hi", cancellationToken)).IsQueued);

        var candidates = phone.PcCopy.ListCandidates();
        Assert.Equal(2, candidates.Count);
        Assert.Equal("Study", candidates[0].Title.Value);
        Assert.True(candidates[0].IsGroup);
        Assert.Equal(1, candidates[0].MessageCount);
        Assert.Equal(1, candidates[0].TotalMessageCount);
        Assert.False(candidates[0].IsTruncated);
        Assert.Equal("Bob", candidates[1].Title.Value);
        Assert.False(candidates[1].IsGroup);
        Assert.Equal(1, candidates[1].MessageCount);
        Assert.Equal(1, candidates[1].TotalMessageCount);
        Assert.False(candidates[1].IsTruncated);
        Assert.DoesNotContain(candidates, static item => item.Title.Value == "Waiting");
    }

    [Fact]
    [Trait("Story", "US-22")]
    public async Task Copy_WhenChatExceedsMessageOrByteCap_DisclosesTruncatedCountAndCopiesExactNewestWindow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        var capped = new CopyOptions { MaxMessagesPerConversation = 2, MaxConversationBytes = 25 };
        await using var phone = await StartPeerAsync("Alice", hub.CreateNode(), cancellationToken, capped);
        await using var bob = await StartPeerAsync("Bob", hub.CreateNode(), cancellationToken);
        await using var pc = await StartPeerAsync("AlicePC", hub.CreateNode(), cancellationToken, capped);

        await ConnectAsync(phone, bob, cancellationToken);
        Assert.True((await phone.SendAsync(bob.Identity.Keys.IdentityKey, "oldest message", cancellationToken)).IsQueued);
        Assert.True((await phone.SendAsync(bob.Identity.Keys.IdentityKey, "second msg", cancellationToken)).IsQueued);
        Assert.True((await phone.SendAsync(bob.Identity.Keys.IdentityKey, "newest msg", cancellationToken)).IsQueued);

        var candidate = Assert.Single(phone.PcCopy.ListCandidates());
        Assert.Equal(2, candidate.MessageCount);
        Assert.Equal(3, candidate.TotalMessageCount);
        Assert.True(candidate.IsTruncated);

        var started = await pc.PcCopy.StartReceivingAsync(cancellationToken);
        Assert.True(started.Started);
        await using (var receiver = started.Receiver)
        {
            var receiveTask = receiver.RunAsync(null, cancellationToken);
            var connected = await phone.PcCopy.ConnectAsync(receiver.PairingCode, cancellationToken);
            Assert.True(connected.Connected);
            await using var sender = connected.Sender;
            var sent = await sender.SendAsync([candidate.Origin], null, cancellationToken);
            var received = await receiveTask;
            Assert.Equal(PcCopySendOutcome.Completed, sent.Outcome);
            Assert.Equal(PcCopyReceivePhase.Completed, received.Outcome);
        }

        Assert.True(pc.CopiedChats.TryGet(candidate.Origin, out var copy));
        Assert.Equal(candidate.MessageCount, copy.Messages.Count);
        Assert.Equal(["second msg", "newest msg"], copy.Messages.Select(static message => message.Text));

        Assert.True((await phone.SendAsync(bob.Identity.Keys.IdentityKey, "12345678901234567890", cancellationToken)).IsQueued);
        var byteCappedCandidate = Assert.Single(phone.PcCopy.ListCandidates());
        Assert.Equal(1, byteCappedCandidate.MessageCount);
        Assert.Equal(4, byteCappedCandidate.TotalMessageCount);
        Assert.True(byteCappedCandidate.IsTruncated);
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-09")]
    public async Task Receive_ConversationExceedingMessageOrByteBudget_FailsCleanlyWithoutSavingPartial()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tight = new CopyOptions { MaxMessagesPerConversation = 2, MaxConversationBytes = 30 };
        await using var pc = await StartPeerAsync("PC", new LoopbackDiscovery().CreateNode(), cancellationToken, tight);
        using var phoneKey = KeySet.Create();
        using var bobKey = KeySet.Create();
        var bobOrigin = CopyOriginId.ForContact(phoneKey.IdentityKey, bobKey.IdentityKey);
        Assert.True(DisplayName.TryCreate("Bob", out var bobName));

        var countStart = await pc.PcCopy.StartReceivingAsync(cancellationToken);
        Assert.True(countStart.Started);
        await using (var receiver = countStart.Receiver)
        {
            var receiveTask = receiver.RunAsync(null, cancellationToken);
            var dialed = await CopyDialer.ConnectAsync(
                receiver.PairingCode,
                new CopyOptions(),
                TimeProvider.System,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<CopyChannel>.Instance,
                cancellationToken);
            Assert.True(dialed.IsConnected);
            await using (var channel = dialed.Channel)
            {
                Assert.True(CopyTransferStart.TryCreate(1, out var transferStart));
                Assert.True(await channel.SendAsync(transferStart, cancellationToken));
                Assert.True(CopyConversationStart.TryCreate(bobOrigin, bobName, 3, out var tooMany));
                Assert.True(await channel.SendAsync(tooMany, cancellationToken));
            }

            var result = await receiveTask;
            Assert.Equal(PcCopyReceivePhase.Failed, result.Outcome);
            Assert.Equal(0, result.SavedCount);
        }

        var byteStart = await pc.PcCopy.StartReceivingAsync(cancellationToken);
        Assert.True(byteStart.Started);
        await using (var receiver = byteStart.Receiver)
        {
            var receiveTask = receiver.RunAsync(null, cancellationToken);
            var dialed = await CopyDialer.ConnectAsync(
                receiver.PairingCode,
                new CopyOptions(),
                TimeProvider.System,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<CopyChannel>.Instance,
                cancellationToken);
            Assert.True(dialed.IsConnected);
            await using (var channel = dialed.Channel)
            {
                Assert.True(CopyTransferStart.TryCreate(1, out var transferStart));
                Assert.True(await channel.SendAsync(transferStart, cancellationToken));
                Assert.True(CopyConversationStart.TryCreate(bobOrigin, bobName, 2, out var convStart));
                Assert.True(await channel.SendAsync(convStart, cancellationToken));
                Assert.True(CopyConversationMessage.TryCreate(1, true, null, DateTimeOffset.UnixEpoch.AddDays(20_000), "12345678901234567890", out var first));
                Assert.True(await channel.SendAsync(first, cancellationToken));
                Assert.True(CopyConversationMessage.TryCreate(2, true, null, DateTimeOffset.UnixEpoch.AddDays(20_000), "12345678901234567890", out var second));
                Assert.True(await channel.SendAsync(second, cancellationToken));
            }

            var result = await receiveTask;
            Assert.Equal(PcCopyReceivePhase.Failed, result.Outcome);
            Assert.Equal(0, result.SavedCount);
        }

        Assert.Empty(pc.CopiedChats.All);
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-10")]
    public async Task CopyAndRecopy_TransfersChosenChatsKeepsUnselectedAndNeverSendsPrivateKeys()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = new LoopbackDiscovery();
        await using var phone = await StartPeerAsync("Alice", hub.CreateNode(), cancellationToken);
        await using var bob = await StartPeerAsync("Bob", hub.CreateNode(), cancellationToken);
        await using var carol = await StartPeerAsync("Carol", hub.CreateNode(), cancellationToken);
        await using var pc = await StartPeerAsync("AlicePC", hub.CreateNode(), cancellationToken);

        await ConnectAsync(phone, bob, cancellationToken);
        await ConnectAsync(phone, carol, cancellationToken);
        Assert.True((await phone.SendAsync(bob.Identity.Keys.IdentityKey, "Hi Bob 1", cancellationToken)).IsQueued);
        Assert.True((await bob.SendAsync(phone.Identity.Keys.IdentityKey, "Reply from Bob", cancellationToken)).IsQueued);
        Assert.True((await phone.SendAsync(carol.Identity.Keys.IdentityKey, "Hi Carol", cancellationToken)).IsQueued);
        await TestMessengers.WaitUntilAsync(() => phone.Chats.MessagesWith(bob.Identity.Keys.IdentityKey).Count == 2, cancellationToken);

        var candidates = phone.PcCopy.ListCandidates();
        Assert.Equal(2, candidates.Count);
        var bobCandidate = Assert.Single(candidates, static item => item.Title.Value == "Bob");
        var carolCandidate = Assert.Single(candidates, static item => item.Title.Value == "Carol");

        var firstReceive = await pc.PcCopy.StartReceivingAsync(cancellationToken);
        Assert.True(firstReceive.Started);
        await using (var receiver = firstReceive.Receiver)
        {
            var receiveTask = receiver.RunAsync(null, cancellationToken);
            var connectResult = await phone.PcCopy.ConnectAsync(receiver.PairingCode, cancellationToken);
            Assert.True(connectResult.Connected);
            await using var sender = connectResult.Sender;

            var nothing = await sender.SendAsync([], null, cancellationToken);
            Assert.Equal(PcCopySendOutcome.NothingSelected, nothing.Outcome);

            var sendResult = await sender.SendAsync([bobCandidate.Origin, carolCandidate.Origin], null, cancellationToken);
            var receiveResult = await receiveTask;
            Assert.Equal(PcCopySendOutcome.Completed, sendResult.Outcome);
            Assert.Equal(2, sendResult.CopiedCount);
            Assert.Equal(PcCopyReceivePhase.Completed, receiveResult.Outcome);
            Assert.Equal(2, receiveResult.SavedCount);
        }

        Assert.Empty(pc.Contacts.All);
        Assert.Equal(2, pc.CopiedChats.All.Count);
        Assert.True(pc.CopiedChats.TryGet(bobCandidate.Origin, out var bobCopy));
        Assert.Equal(["Hi Bob 1", "Reply from Bob"], bobCopy.Messages.Select(static message => message.Text));

        Assert.True((await phone.SendAsync(bob.Identity.Keys.IdentityKey, "After first copy", cancellationToken)).IsQueued);
        Assert.True(pc.CopiedChats.TryGet(bobCandidate.Origin, out var unchangedCopy));
        Assert.Equal(2, unchangedCopy.Messages.Count);

        var secondReceive = await pc.PcCopy.StartReceivingAsync(cancellationToken);
        Assert.True(secondReceive.Started);
        await using (var receiver = secondReceive.Receiver)
        {
            var receiveTask = receiver.RunAsync(null, cancellationToken);
            var connectResult = await phone.PcCopy.ConnectAsync(receiver.PairingCode, cancellationToken);
            Assert.True(connectResult.Connected);
            await using var sender = connectResult.Sender;
            var sendResult = await sender.SendAsync([bobCandidate.Origin], null, cancellationToken);
            var receiveResult = await receiveTask;
            Assert.Equal(PcCopySendOutcome.Completed, sendResult.Outcome);
            Assert.Equal(1, receiveResult.SavedCount);
        }

        Assert.Equal(2, pc.CopiedChats.All.Count);
        Assert.True(pc.CopiedChats.TryGet(bobCandidate.Origin, out var updatedBob));
        Assert.Equal(3, updatedBob.Messages.Count);
        Assert.Equal("After first copy", updatedBob.Messages[2].Text);
        Assert.True(pc.CopiedChats.TryGet(carolCandidate.Origin, out var keptCarol));
        Assert.Single(keptCarol.Messages);

        var exportedPhoneKeys = new byte[KeySet.ExportLength];
        try
        {
            Assert.True(phone.Identity.Keys.TryExport(exportedPhoneKeys, out _));
            byte[][] privateKeys =
            [
                exportedPhoneKeys.AsSpan(1, 32).ToArray(),
                exportedPhoneKeys.AsSpan(33, 32).ToArray(),
                exportedPhoneKeys.AsSpan(65, 32).ToArray(),
            ];
            var allCopiedText = string.Join("\n", pc.CopiedChats.All.SelectMany(static chat => chat.Messages.Select(static message => message.Text)));
            foreach (var key in privateKeys)
            {
                Assert.All(pc.CopiedChats.All, chat => Assert.Equal(-1, chat.Origin.Bytes.IndexOf(key)));
                Assert.DoesNotContain(Convert.ToHexStringLower(key), allCopiedText, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exportedPhoneKeys);
        }
    }

    [Fact]
    [Trait("Story", "US-22")]
    public async Task Receive_ConnectionBrokenMidTransfer_KeepsCompletedChatAndLeavesInterruptedUnsaved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var pc = await StartPeerAsync("PC", new LoopbackDiscovery().CreateNode(), cancellationToken);
        using var phoneKey = KeySet.Create();
        using var bobKey = KeySet.Create();
        using var carolKey = KeySet.Create();
        var bobOrigin = CopyOriginId.ForContact(phoneKey.IdentityKey, bobKey.IdentityKey);
        var carolOrigin = CopyOriginId.ForContact(phoneKey.IdentityKey, carolKey.IdentityKey);
        Assert.True(DisplayName.TryCreate("Bob", out var bobName));
        Assert.True(DisplayName.TryCreate("Carol", out var carolName));

        var started = await pc.PcCopy.StartReceivingAsync(cancellationToken);
        Assert.True(started.Started);
        await using (var receiver = started.Receiver)
        {
            var receiveTask = receiver.RunAsync(null, cancellationToken);
            var dialed = await CopyDialer.ConnectAsync(
                receiver.PairingCode,
                new CopyOptions(),
                TimeProvider.System,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<CopyChannel>.Instance,
                cancellationToken);
            Assert.True(dialed.IsConnected);
            await using (var channel = dialed.Channel)
            {
                Assert.True(CopyTransferStart.TryCreate(2, out var start));
                Assert.True(await channel.SendAsync(start, cancellationToken));

                Assert.True(CopyConversationStart.TryCreate(bobOrigin, bobName, 1, out var bobStart));
                Assert.True(await channel.SendAsync(bobStart, cancellationToken));
                Assert.True(CopyConversationMessage.TryCreate(1, true, null, DateTimeOffset.UnixEpoch.AddDays(20_000), "Complete chat", out var bobMsg));
                Assert.True(await channel.SendAsync(bobMsg, cancellationToken));
                Assert.True(await channel.SendAsync(CopyConversationEnd.Create(bobOrigin), cancellationToken));
                Assert.Equal(CopyConversationSaved.Create(bobOrigin), await channel.ReceiveAsync(cancellationToken));

                Assert.True(CopyConversationStart.TryCreate(carolOrigin, carolName, 2, out var carolStart));
                Assert.True(await channel.SendAsync(carolStart, cancellationToken));
                Assert.True(CopyConversationMessage.TryCreate(1, false, carolName, DateTimeOffset.UnixEpoch.AddDays(20_000), "Partial only", out var carolMsg));
                Assert.True(await channel.SendAsync(carolMsg, cancellationToken));
            }

            var result = await receiveTask;
            Assert.Equal(PcCopyReceivePhase.Disconnected, result.Outcome);
            Assert.Equal(1, result.SavedCount);
        }

        Assert.Single(pc.CopiedChats.All);
        Assert.True(pc.CopiedChats.TryGet(bobOrigin, out _));
        Assert.False(pc.CopiedChats.TryGet(carolOrigin, out _));
    }

    [Fact]
    [Trait("Story", "US-22")]
    public async Task StartReceiving_WithNoLocalNetworkAddresses_ReturnsNoLocalNetwork()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setup = MessengerSetup.InMemory() with
        {
            ListenAddress = IPAddress.Loopback,
            ListenPort = 0,
            Connection = TestMessengers.Fast,
            CopyAddressSource = static () => [],
        };
        await using var pc = await Messenger.StartAsync(TestKeys.NewIdentity("PC"), setup, cancellationToken);

        var result = await pc.PcCopy.StartReceivingAsync(cancellationToken);

        Assert.False(result.Started);
        Assert.Equal(PcCopyReceiveProblem.NoLocalNetwork, result.Problem);
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-09")]
    public async Task DatabaseLocalData_TamperedCopiedConversation_FailsClosedOnOpen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), $"p2pm-copy-tamper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var dbPath = Path.Combine(folder, DatabaseLocalData.FileName);
        var kdf = new PassphraseKdf(16 * 1024, 1);
        try
        {
            await using (var local = new DatabaseLocalData(dbPath, null, kdf))
            {
                var created = await local.CreateAsync("lantern orchard pebble", cancellationToken);
                Assert.True(created.IsOpen);
                using var owner = KeySet.Create();
                using var bob = KeySet.Create();
                Assert.True(DisplayName.TryCreate("Bob", out var bobName));
                await created.Stores.CopiedChats.SaveAsync(
                    new CopiedConversation(
                        CopyOriginId.ForContact(owner.IdentityKey, bob.IdentityKey),
                        bobName,
                        DateTimeOffset.UnixEpoch.AddDays(20_000),
                        [new CopiedMessage(1, true, null, "Tamper me", DateTimeOffset.UnixEpoch.AddDays(20_000))]),
                    cancellationToken);
            }

            var cs = SqliteDatabase.ConnectionStringFor(dbPath);
            cs.Pooling = false;
            await using (var connection = await SqliteDatabase.OpenAsync(cs, cancellationToken))
            using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT title_enc FROM copied_conversation WHERE id = 1;";
                var bytes = (byte[])(await read.ExecuteScalarAsync(cancellationToken))!;
                bytes[0] ^= 1;
                using var write = connection.CreateCommand();
                write.CommandText = "UPDATE copied_conversation SET title_enc = $v WHERE id = 1;";
                write.Parameters.AddWithValue("$v", bytes);
                Assert.Equal(1, await write.ExecuteNonQueryAsync(cancellationToken));
            }

            await using (var reopened = new DatabaseLocalData(dbPath, null, kdf))
            {
                var open = await reopened.OpenAsync(cancellationToken);
                Assert.Equal(LocalDataStatus.PassphraseNeeded, open.Status);
                var unlocked = await reopened.UnlockAsync("lantern orchard pebble", cancellationToken);
                Assert.Equal(LocalDataStatus.Failed, unlocked.Status);
                Assert.Equal(LocalDataProblem.Damaged, unlocked.Problem);
            }
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static Ipv4Address CreateLoopback()
    {
        Assert.True(Ipv4Address.TryCreate(127, 0, 0, 1, out var address));
        return address;
    }

    private static Task<Messenger> StartPeerAsync(string name, LoopbackDiscovery.Node node, CancellationToken cancellationToken, CopyOptions? copy = null) =>
        Messenger.StartAsync(
            TestKeys.NewIdentity(name),
            MessengerSetup.InMemory() with
            {
                ListenAddress = IPAddress.Loopback,
                ListenPort = 0,
                CopyListenAddress = IPAddress.Loopback,
                CopyListenPort = 0,
                CopyAddressSource = static () => [Loopback],
                Copy = copy ?? new CopyOptions(),
                Connection = TestMessengers.Fast,
                Discovery = node.Setup,
            },
            cancellationToken);

    private static async Task ConnectAsync(Messenger first, Messenger second, CancellationToken cancellationToken)
    {
        Assert.True((await first.Contacts.AddAsync(first.Contacts.Check(second.Identity.CodeText), cancellationToken)).IsSaved);
        Assert.True((await second.Contacts.AddAsync(second.Contacts.Check(first.Identity.CodeText), cancellationToken)).IsSaved);
        await TestMessengers.WaitUntilAsync(
            () => first.Contacts.TryGet(second.Identity.Keys.IdentityKey, out var contact)
                && contact.IsAdded
                && first.Connections.IsConnected(second.Identity.Keys.IdentityKey),
            cancellationToken);
    }
}
