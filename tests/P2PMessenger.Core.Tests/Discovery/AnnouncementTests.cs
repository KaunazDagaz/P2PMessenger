using System.Buffers.Binary;
using System.Text;
using P2PMessenger.Core.Discovery;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Tests.Sessions;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Core.Tests.Discovery;

public sealed class AnnouncementTests
{
    private const int Port = 50_000;
    private const string Nonce = "000102030405060708090a0b0c0d0e0f";
    private const string SlotAliceToBob = "bdb1876cafa386dc";
    private const string SlotBobToAlice = "1d00175325ba2382";
    private const string AliceToBobAtPositionFive =
        "000102030405060708090a0b0c0d0e0fc350" + "0000000000000000" + "0000000000000000" + "0000000000000000" + "0000000000000000" + "0000000000000000"
        + SlotAliceToBob;

    [Fact]
    public void Length_IsTheFormatOfAdr0004()
    {
        Assert.Equal(274, Announcement.Length);
        Assert.Equal(18, Announcement.SlotsOffset);
        Assert.Equal(32, Announcement.SlotCount);
        Assert.Equal(8, Announcement.SlotLength);
    }

    [Fact]
    [Trait("Story", "US-06")]
    [Trait("NFR", "NFR-14")]
    public void ComputeSlot_PublishedTestKeys_MatchesTheIndependentImplementation()
    {
        using var peers = SessionPeers.Create();
        var header = Header(Convert.FromHexString(Nonce), Port);
        var slot = new byte[Announcement.SlotLength];

        Announcement.ComputeSlot(peers.BobForAlice, ownIsSender: true, header, slot);
        Assert.Equal(SlotAliceToBob, Convert.ToHexStringLower(slot));
        Announcement.ComputeSlot(peers.AliceForBob, ownIsSender: false, header, slot);
        Assert.Equal(SlotAliceToBob, Convert.ToHexStringLower(slot));

        Announcement.ComputeSlot(peers.AliceForBob, ownIsSender: true, header, slot);
        Assert.Equal(SlotBobToAlice, Convert.ToHexStringLower(slot));
        Announcement.ComputeSlot(peers.BobForAlice, ownIsSender: false, header, slot);
        Assert.Equal(SlotBobToAlice, Convert.ToHexStringLower(slot));
    }

    [Fact]
    [Trait("Story", "US-06")]
    public void Write_PublishedTestKeys_MatchesTheIndependentImplementationByteForByte()
    {
        using var peers = SessionPeers.Create();
        var announcement = new byte[Announcement.Length];

        Announcement.Write([peers.BobForAlice], Port, Convert.FromHexString(Nonce), [5], announcement);

        var expected = Convert.FromHexString(AliceToBobAtPositionFive.PadRight(Announcement.Length * 2, '0'));
        Assert.Equal(expected, announcement);
        Assert.True(Announcement.TryRecognize(expected, [peers.AliceForBob], out var sender, out var port));
        Assert.Same(peers.AliceForBob, sender);
        Assert.Equal(Port, port);
    }

    [Theory]
    [Trait("Story", "US-06")]
    [Trait("NFR", "NFR-14")]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(32)]
    public void Build_AnyNumberOfRecipientsUpTo32_HasTheSameLengthAndEveryRecipientRecognizesIt(int count)
    {
        using var sender = KeySet.Create();
        var recipients = Enumerable.Range(0, count).Select(_ => KeySet.Create()).ToList();
        var forSender = recipients.Select(recipient => SessionPeers.Contact(sender, recipient)).ToList();
        var forRecipients = recipients.Select(recipient => SessionPeers.Contact(recipient, sender)).ToList();
        try
        {
            var announcement = Announcement.Build(forSender, Port);

            Assert.Equal(Announcement.Length, announcement.Length);
            Assert.Equal(Port, BinaryPrimitives.ReadUInt16BigEndian(announcement.AsSpan(Announcement.NonceLength, Announcement.PortLength)));
            foreach (var contact in forRecipients)
            {
                Assert.True(Announcement.TryRecognize(announcement, [contact], out var recognized, out var port));
                Assert.Same(contact, recognized);
                Assert.Equal(Port, port);
            }
        }
        finally
        {
            forSender.ForEach(static contact => contact.Dispose());
            forRecipients.ForEach(static contact => contact.Dispose());
            recipients.ForEach(static keys => keys.Dispose());
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("Story", "US-06")]
    public void TryRecognize_OtherContactsOfTheSenderAndStrangers_RecognizeNothing()
    {
        using var alice = KeySet.Create();
        using var bob = KeySet.Create();
        using var carol = KeySet.Create();
        using var stranger = KeySet.Create();
        using var bobForAlice = SessionPeers.Contact(alice, bob);
        using var aliceForCarol = SessionPeers.Contact(carol, alice);
        using var bobForStranger = SessionPeers.Contact(stranger, bob);
        using var aliceForStranger = SessionPeers.Contact(stranger, alice);

        var announcement = Announcement.Build([bobForAlice], Port);

        Assert.False(Announcement.TryRecognize(announcement, [aliceForCarol], out _, out _));
        Assert.False(Announcement.TryRecognize(announcement, [bobForStranger, aliceForStranger], out var sender, out var port));
        Assert.Null(sender);
        Assert.Equal(0, port);
    }

    [Fact]
    [Trait("Story", "US-06")]
    public void TryRecognize_TheSendersOwnAnnouncement_IsNotTakenForTheContacts()
    {
        using var peers = SessionPeers.Create();
        var fromAlice = Announcement.Build([peers.BobForAlice], Port);
        var fromBob = Announcement.Build([peers.AliceForBob], Port);

        Assert.False(Announcement.TryRecognize(fromAlice, [peers.BobForAlice], out _, out _));
        Assert.False(Announcement.TryRecognize(fromBob, [peers.AliceForBob], out _, out _));
        Assert.True(Announcement.TryRecognize(fromAlice, [peers.AliceForBob], out _, out _));
        Assert.True(Announcement.TryRecognize(fromBob, [peers.BobForAlice], out _, out _));
    }

    [Fact]
    [Trait("Story", "US-06")]
    [Trait("NFR", "NFR-14")]
    public void Build_HoldsNoNameKeyOrContactData()
    {
        const string senderName = "alice-the-announcing-sender";
        using var sender = KeySet.Create();
        var recipients = Enumerable.Range(0, 5).Select(_ => KeySet.Create()).ToList();
        var forSender = recipients.Select(recipient => SessionPeers.Contact(sender, recipient)).ToList();
        try
        {
            var announcement = Announcement.Build(forSender, Port);

            List<byte[]> secrets = [Encoding.UTF8.GetBytes(senderName), Encoding.Unicode.GetBytes(senderName)];
            foreach (var keys in recipients.Append(sender))
            {
                secrets.Add(keys.IdentityKey.Bytes.ToArray());
                secrets.Add(keys.StaticKey.Bytes.ToArray());
                secrets.Add(keys.OnionKey.Bytes.ToArray());
                secrets.Add(keys.IdentityKey.Bytes[..8].ToArray());
                secrets.Add(keys.StaticKey.Bytes[..8].ToArray());
            }

            Assert.All(secrets, secret => Assert.Equal(-1, announcement.AsSpan().IndexOf(secret)));
        }
        finally
        {
            forSender.ForEach(static contact => contact.Dispose());
            recipients.ForEach(static keys => keys.Dispose());
        }
    }

    [Fact]
    [Trait("NFR", "NFR-14")]
    public void Build_TwoAnnouncementsOfOneDevice_ShareNoNonceAndNoSlotValue()
    {
        using var peers = SessionPeers.Create();
        var first = Announcement.Build([peers.BobForAlice], Port);
        var second = Announcement.Build([peers.BobForAlice], Port);

        Assert.False(first.AsSpan(0, Announcement.NonceLength).SequenceEqual(second.AsSpan(0, Announcement.NonceLength)));
        for (var slot = 0; slot < Announcement.SlotCount; slot++)
        {
            var value = first.AsSpan(Announcement.SlotsOffset + (slot * Announcement.SlotLength), Announcement.SlotLength).ToArray();
            Assert.Equal(-1, second.AsSpan(Announcement.SlotsOffset).IndexOf(value));
        }
    }

    [Fact]
    [Trait("Story", "US-06")]
    [Trait("NFR", "NFR-09")]
    public void TamperSweep_EverySingleBitChange_IsRefusedByEveryoneItConcerns()
    {
        using var sender = KeySet.Create();
        var recipients = Enumerable.Range(0, 5).Select(_ => KeySet.Create()).ToList();
        using var bystander = KeySet.Create();
        var forSender = recipients.Select(recipient => SessionPeers.Contact(sender, recipient)).ToList();
        var forRecipients = recipients.Select(recipient => SessionPeers.Contact(recipient, sender)).ToList();
        using var forBystander = SessionPeers.Contact(bystander, sender);
        try
        {
            var announcement = Announcement.Build(forSender, Port);
            var copy = announcement.ToArray();
            int headerFlips = 0;
            int ownSlotFlips = 0;
            int emptySlotFlips = 0;
            for (var bit = 0; bit < copy.Length * 8; bit++)
            {
                copy[bit / 8] ^= (byte)(1 << (bit % 8));
                var refusing = forRecipients.Count(contact => !Announcement.TryRecognize(copy, [contact], out _, out _));
                var outsider = Announcement.TryRecognize(copy, [forBystander], out _, out _);
                copy[bit / 8] ^= (byte)(1 << (bit % 8));

                Assert.False(outsider, $"Bit {bit} made an outsider recognize the announcement.");
                if (bit / 8 < Announcement.SlotsOffset)
                {
                    Assert.Equal(forRecipients.Count, refusing);
                    headerFlips++;
                }
                else if (refusing == 1)
                {
                    ownSlotFlips++;
                }
                else
                {
                    Assert.Equal(0, refusing);
                    emptySlotFlips++;
                }
            }

            Assert.Equal(Announcement.SlotsOffset * 8, headerFlips);
            Assert.Equal(forRecipients.Count * Announcement.SlotLength * 8, ownSlotFlips);
            Assert.Equal((Announcement.SlotCount - forRecipients.Count) * Announcement.SlotLength * 8, emptySlotFlips);
        }
        finally
        {
            forSender.ForEach(static contact => contact.Dispose());
            forRecipients.ForEach(static contact => contact.Dispose());
            recipients.ForEach(static keys => keys.Dispose());
        }
    }

    [Theory]
    [Trait("NFR", "NFR-09")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(273)]
    [InlineData(275)]
    [InlineData(512)]
    public void TryRecognize_WrongLength_IsRefused(int length)
    {
        using var peers = SessionPeers.Create();
        var announcement = Announcement.Build([peers.BobForAlice], Port);
        var datagram = new byte[length];
        announcement.AsSpan(0, Math.Min(length, announcement.Length)).CopyTo(datagram);

        Assert.False(Announcement.TryRecognize(datagram, [peers.AliceForBob], out _, out _));
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void TryRecognize_PortZero_IsRefused()
    {
        using var peers = SessionPeers.Create();
        var announcement = new byte[Announcement.Length];

        Announcement.Write([peers.BobForAlice], 0, Convert.FromHexString(Nonce), [0], announcement);

        Assert.False(Announcement.TryRecognize(announcement, [peers.AliceForBob], out _, out _));
    }

    [Fact]
    public void TryRecognize_RandomDatagrams_AreNeverRecognized()
    {
        using var peers = SessionPeers.Create();
        var datagram = new byte[Announcement.Length];

        for (var attempt = 0; attempt < 5_000; attempt++)
        {
            RandomNumberGenerator.Fill(datagram);

            Assert.False(Announcement.TryRecognize(datagram, [peers.AliceForBob, peers.BobForAlice], out _, out _));
        }
    }

    [Fact]
    [Trait("Story", "US-06")]
    public void BuildAll_MoreThan32Contacts_SplitsThemSoEachIsInExactlyOneAnnouncement()
    {
        const int count = 70;
        using var sender = KeySet.Create();
        var recipients = Enumerable.Range(0, count).Select(_ => KeySet.Create()).ToList();
        var forSender = recipients.Select(recipient => SessionPeers.Contact(sender, recipient)).ToList();
        var forRecipients = recipients.Select(recipient => SessionPeers.Contact(recipient, sender)).ToList();
        try
        {
            var announcements = Announcement.BuildAll(forSender, Port);

            Assert.Equal(3, announcements.Count);
            Assert.All(announcements, static announcement => Assert.Equal(Announcement.Length, announcement.Length));
            foreach (var contact in forRecipients)
            {
                Assert.Equal(1, announcements.Count(announcement => Announcement.TryRecognize(announcement, [contact], out _, out _)));
            }
        }
        finally
        {
            forSender.ForEach(static contact => contact.Dispose());
            forRecipients.ForEach(static contact => contact.Dispose());
            recipients.ForEach(static keys => keys.Dispose());
        }
    }

    [Fact]
    public void BuildAll_WithoutContacts_BuildsNothing()
    {
        Assert.Empty(Announcement.BuildAll([], Port));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65_536)]
    public void Build_PortOutOfRange_Throws(int port)
    {
        using var peers = SessionPeers.Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => Announcement.Build([peers.BobForAlice], port));
        Assert.Throws<ArgumentOutOfRangeException>(() => Announcement.BuildAll([peers.BobForAlice], port));
    }

    [Fact]
    public void Build_MoreThan32Recipients_Throws()
    {
        using var sender = KeySet.Create();
        var recipients = Enumerable.Range(0, 33).Select(_ => KeySet.Create()).ToList();
        var forSender = recipients.Select(recipient => SessionPeers.Contact(sender, recipient)).ToList();
        try
        {
            Assert.Throws<ArgumentException>(() => Announcement.Build(forSender, Port));
        }
        finally
        {
            forSender.ForEach(static contact => contact.Dispose());
            recipients.ForEach(static keys => keys.Dispose());
        }
    }

    [Fact]
    public void Write_SlotPositionOutOfRange_Throws()
    {
        using var peers = SessionPeers.Create();
        var announcement = new byte[Announcement.Length];

        Assert.Throws<ArgumentOutOfRangeException>(() => Announcement.Write([peers.BobForAlice], Port, Convert.FromHexString(Nonce), [32], announcement));
    }

    [Fact]
    [Trait("Story", "US-05")]
    public void PairwiseKeys_HintAndAnnouncementKeys_AreDifferent()
    {
        using var peers = SessionPeers.Create();
        var data = new byte[24];
        var byHintKey = NSec.Cryptography.MacAlgorithm.Blake2b_128.Mac(peers.BobForAlice.PairwiseKey, data);
        var byAnnouncementKey = NSec.Cryptography.MacAlgorithm.Blake2b_128.Mac(peers.BobForAlice.AnnouncementKey, data);

        Assert.NotEqual(byHintKey, byAnnouncementKey);
        Assert.Equal(byAnnouncementKey, NSec.Cryptography.MacAlgorithm.Blake2b_128.Mac(peers.AliceForBob.AnnouncementKey, data));
    }

    private static byte[] Header(byte[] nonce, int port)
    {
        var header = new byte[Announcement.SlotsOffset];
        nonce.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(Announcement.NonceLength), (ushort)port);
        return header;
    }
}
