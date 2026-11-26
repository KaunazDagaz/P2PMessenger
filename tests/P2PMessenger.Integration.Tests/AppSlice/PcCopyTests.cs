using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using P2PMessenger.Application.Copy;
using P2PMessenger.Application.Groups;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Identity;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Integration.Tests.AppSlice;

public sealed class PcCopyTests
{
    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-10")]
    [Trait("NFR", "NFR-11")]
    public async Task SavedPhoneAndPc_CopyRecopyRestartAndDelete_SatisfyAllAcceptanceCriteria()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var phoneProtector = new TestKeyProtector();
        var bobProtector = new TestKeyProtector();
        var pcProtector = new TestKeyProtector();
        var phoneDb = folder.DatabasePath("phone");
        var bobDb = folder.DatabasePath("bob");
        var pcDb = folder.DatabasePath("pc");

        await using var phone = await AppPeer.StartSavedAsync(phoneDb, phoneProtector, "Alice", cancellationToken);
        await using var bob = await AppPeer.StartSavedAsync(bobDb, bobProtector, "Bob", cancellationToken);

        await phone.ImportAsync(bob, cancellationToken);
        await bob.ImportAsync(phone, cancellationToken);
        await Eventually.HoldsAsync(() => phone.ContactFor(bob).IsAdded && bob.ContactFor(phone).IsAdded, cancellationToken);

        Assert.True((await phone.Messenger.SendAsync(bob.Identity.Keys.IdentityKey, "Phone to Bob", cancellationToken)).IsQueued);
        Assert.True((await bob.Messenger.SendAsync(phone.Identity.Keys.IdentityKey, "Bob reply 1", cancellationToken)).IsQueued);
        await Eventually.HoldsAsync(() => phone.Messenger.Chats.MessagesWith(bob.Identity.Keys.IdentityKey).Count == 2, cancellationToken);

        var createdGroup = await phone.Messenger.CreateGroupAsync("Trip", [bob.Identity.Keys.IdentityKey], cancellationToken);
        Assert.True(createdGroup.IsCreated);
        await Eventually.HoldsAsync(
            () => phone.Messenger.Groups.TryGet(createdGroup.Group.Id, out var g) && g.State == GroupState.Created,
            cancellationToken);
        Assert.True((await phone.Messenger.SendToGroupAsync(createdGroup.Group.Id, "Pack snacks", cancellationToken)).IsQueued);

        var bobOrigin = CopyOriginId.ForContact(phone.Identity.Keys.IdentityKey, bob.Identity.Keys.IdentityKey);
        var groupOrigin = CopyOriginId.ForGroup(phone.Identity.Keys.IdentityKey, createdGroup.Group.Id);

        await using (var pc = await AppPeer.StartSavedAsync(pcDb, pcProtector, "AlicePC", cancellationToken))
        {
            var started = await pc.Messenger.PcCopy.StartReceivingAsync(cancellationToken);
            Assert.True(started.Started);
            await using var receiver = started.Receiver;
            var receiveTask = receiver.RunAsync(null, cancellationToken);

            using (var rogue = new TcpClient())
            {
                await rogue.ConnectAsync(System.Net.IPAddress.Loopback, receiver.ListeningPort, cancellationToken);
                await rogue.GetStream().WriteAsync(RandomNumberGenerator.GetBytes(64), cancellationToken);
            }

            Assert.True(PcCopyService.TryParsePairingCode(receiver.PairingCodeText, out var parsed, out _));
            var connected = await phone.Messenger.PcCopy.ConnectAsync(parsed, cancellationToken);
            Assert.True(connected.Connected);
            await using var sender = connected.Sender;
            var sent = await sender.SendAsync([bobOrigin, groupOrigin], null, cancellationToken);
            var received = await receiveTask;

            Assert.Equal(PcCopySendOutcome.Completed, sent.Outcome);
            Assert.Equal(2, sent.CopiedCount);
            Assert.Equal(PcCopyReceivePhase.Completed, received.Outcome);
            Assert.Equal(2, received.SavedCount);
        }

        Assert.True((await bob.Messenger.SendAsync(phone.Identity.Keys.IdentityKey, "Bob reply after copy", cancellationToken)).IsQueued);
        await Eventually.HoldsAsync(() => phone.Messenger.Chats.MessagesWith(bob.Identity.Keys.IdentityKey).Count == 3, cancellationToken);

        await using (var restartedPc = await AppPeer.StartSavedAsync(pcDb, pcProtector, "AlicePC", cancellationToken))
        {
            Assert.Empty(restartedPc.Messenger.Contacts.All);
            Assert.Equal(2, restartedPc.Messenger.CopiedChats.All.Count);
            Assert.True(restartedPc.Messenger.CopiedChats.TryGet(bobOrigin, out var bobCopy));
            Assert.Equal(2, bobCopy.Messages.Count);
            Assert.DoesNotContain(bobCopy.Messages, static m => m.Text == "Bob reply after copy");
            Assert.True(restartedPc.Messenger.CopiedChats.TryGet(groupOrigin, out var groupCopy));
            Assert.Equal(["Pack snacks"], groupCopy.Messages.Select(static m => m.Text));

            var started = await restartedPc.Messenger.PcCopy.StartReceivingAsync(cancellationToken);
            Assert.True(started.Started);
            await using var receiver = started.Receiver;
            var receiveTask = receiver.RunAsync(null, cancellationToken);
            var connected = await phone.Messenger.PcCopy.ConnectAsync(receiver.PairingCode, cancellationToken);
            Assert.True(connected.Connected);
            await using var sender = connected.Sender;
            Assert.Equal(PcCopySendOutcome.Completed, (await sender.SendAsync([bobOrigin], null, cancellationToken)).Outcome);
            Assert.Equal(PcCopyReceivePhase.Completed, (await receiveTask).Outcome);

            Assert.True(restartedPc.Messenger.CopiedChats.TryGet(bobOrigin, out var updatedBob));
            Assert.Equal(3, updatedBob.Messages.Count);
            Assert.Equal("Bob reply after copy", updatedBob.Messages[2].Text);

            Assert.True(await restartedPc.Messenger.DeleteCopiedChatAsync(bobOrigin, cancellationToken));
            Assert.Single(restartedPc.Messenger.CopiedChats.All);
        }

        Assert.Equal(3, phone.Messenger.Chats.MessagesWith(bob.Identity.Keys.IdentityKey).Count);

        await using var thirdPc = await AppPeer.StartSavedAsync(pcDb, pcProtector, "AlicePC", cancellationToken);
        Assert.False(thirdPc.Messenger.CopiedChats.TryGet(bobOrigin, out _));
        Assert.True(thirdPc.Messenger.CopiedChats.TryGet(groupOrigin, out _));
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-10")]
    [Trait("NFR", "NFR-11")]
    public async Task PcCopyFiles_CopiedToAnotherPcWithoutCredentials_RevealNothingAndCannotOpen()
    {
        const string TestContactName = "Kestrel Falken";
        const string TestMessageText = "Classified diary phrase obsidian-falcon-8491";

        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var phoneProtector = new TestKeyProtector();
        var bobProtector = new TestKeyProtector();
        var originalPcProtector = new TestKeyProtector();
        var secondPcProtector = new TestKeyProtector();
        var pcDb = folder.DatabasePath("pc-original");
        var stolenDb = folder.DatabasePath("pc-another");

        byte[] phoneExported = new byte[KeySet.ExportLength];
        try
        {
            await using var phone = await AppPeer.StartSavedAsync(folder.DatabasePath("phone"), phoneProtector, "Alice", cancellationToken);
            await using var bob = await AppPeer.StartSavedAsync(folder.DatabasePath("bob"), bobProtector, TestContactName, cancellationToken);
            Assert.True(phone.Identity.Keys.TryExport(phoneExported, out _));

            await phone.ImportAsync(bob, cancellationToken);
            await bob.ImportAsync(phone, cancellationToken);
            await Eventually.HoldsAsync(() => phone.ContactFor(bob).IsAdded && bob.ContactFor(phone).IsAdded, cancellationToken);
            Assert.True((await phone.Messenger.SendAsync(bob.Identity.Keys.IdentityKey, TestMessageText, cancellationToken)).IsQueued);
            await Eventually.HoldsAsync(() => bob.Messenger.Chats.MessagesWith(phone.Identity.Keys.IdentityKey).Count == 1, cancellationToken);

            var origin = CopyOriginId.ForContact(phone.Identity.Keys.IdentityKey, bob.Identity.Keys.IdentityKey);
            await using (var pc = await AppPeer.StartSavedAsync(pcDb, originalPcProtector, "AlicePC", cancellationToken))
            {
                var started = await pc.Messenger.PcCopy.StartReceivingAsync(cancellationToken);
                Assert.True(started.Started);
                await using var receiver = started.Receiver;
                var receiveTask = receiver.RunAsync(null, cancellationToken);
                var connected = await phone.Messenger.PcCopy.ConnectAsync(receiver.PairingCode, cancellationToken);
                Assert.True(connected.Connected);
                await using var sender = connected.Sender;
                Assert.Equal(PcCopySendOutcome.Completed, (await sender.SendAsync([origin], null, cancellationToken)).Outcome);
                Assert.Equal(PcCopyReceivePhase.Completed, (await receiveTask).Outcome);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(stolenDb)!);
            var copiedFiles = new List<byte[]>();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
            {
                var source = pcDb + suffix;
                if (!File.Exists(source))
                {
                    continue;
                }

                var destination = stolenDb + suffix;
                File.Copy(source, destination);
                copiedFiles.Add(await File.ReadAllBytesAsync(destination, cancellationToken));
            }

            Assert.NotEmpty(copiedFiles);
            byte[][] forbidden =
            [
                Encoding.UTF8.GetBytes(TestContactName),
                Encoding.Unicode.GetBytes(TestContactName),
                Encoding.UTF8.GetBytes(TestMessageText),
                Encoding.Unicode.GetBytes(TestMessageText),
                Encoding.UTF8.GetBytes("obsidian-falcon"),
                phoneExported.AsSpan(1, 32).ToArray(),
                phoneExported.AsSpan(33, 32).ToArray(),
                phoneExported.AsSpan(65, 32).ToArray(),
            ];
            foreach (var needle in forbidden)
            {
                Assert.All(copiedFiles, bytes => Assert.Equal(-1, bytes.AsSpan().IndexOf(needle)));
            }

            await using var anotherPcLocalData = new DatabaseLocalData(stolenDb, secondPcProtector);
            var opened = await anotherPcLocalData.OpenAsync(cancellationToken);
            Assert.Equal(LocalDataStatus.Failed, opened.Status);
            Assert.Equal(LocalDataProblem.KeyNotFound, opened.Problem);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(phoneExported);
        }
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-11")]
    public async Task PassphraseProtectedPcCopy_CopiedToAnotherPcWithoutPassphrase_CannotOpen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var kdf = new PassphraseKdf(16 * 1024, 1);
        var pcDb = folder.DatabasePath("pc-pass");
        var stolenDb = folder.DatabasePath("pc-pass-copy");

        await using var phone = await AppPeer.StartAsync("Alice", cancellationToken);
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        await phone.ImportAsync(bob, cancellationToken);
        await bob.ImportAsync(phone, cancellationToken);
        await Eventually.HoldsAsync(() => phone.ContactFor(bob).IsAdded, cancellationToken);
        Assert.True((await phone.Messenger.SendAsync(bob.Identity.Keys.IdentityKey, "Passphrase protected copy text", cancellationToken)).IsQueued);
        var origin = CopyOriginId.ForContact(phone.Identity.Keys.IdentityKey, bob.Identity.Keys.IdentityKey);

        await using (var pc = await AppPeer.StartSavedAsync(new DatabaseLocalData(pcDb, null, kdf), "AlicePC", "amber velvet compass", cancellationToken))
        {
            var started = await pc.Messenger.PcCopy.StartReceivingAsync(cancellationToken);
            Assert.True(started.Started);
            await using var receiver = started.Receiver;
            var receiveTask = receiver.RunAsync(null, cancellationToken);
            var connected = await phone.Messenger.PcCopy.ConnectAsync(receiver.PairingCode, cancellationToken);
            Assert.True(connected.Connected);
            await using var sender = connected.Sender;
            Assert.Equal(PcCopySendOutcome.Completed, (await sender.SendAsync([origin], null, cancellationToken)).Outcome);
            Assert.Equal(PcCopyReceivePhase.Completed, (await receiveTask).Outcome);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(stolenDb)!);
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            if (File.Exists(pcDb + suffix))
            {
                File.Copy(pcDb + suffix, stolenDb + suffix);
            }
        }

        await using var secondPc = new DatabaseLocalData(stolenDb, null, kdf);
        var open = await secondPc.OpenAsync(cancellationToken);
        Assert.Equal(LocalDataStatus.PassphraseNeeded, open.Status);
        var wrong = await secondPc.UnlockAsync("wrong passphrase guess", cancellationToken);
        Assert.Equal(LocalDataStatus.WrongPassphrase, wrong.Status);
    }
}
