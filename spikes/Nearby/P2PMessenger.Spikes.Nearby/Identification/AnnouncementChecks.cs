using System.Security.Cryptography;
using System.Text;
using P2PMessenger.Spikes.Nearby.Checks;

namespace P2PMessenger.Spikes.Nearby.Identification;

public static class AnnouncementChecks
{
    private const string SenderName = "alice-the-announcing-sender";
    private const int Port = 47800;
    private const long CurrentEpoch = 2_000_000;

    private static readonly int[] SizeCases = [1, 5, 32];
    private static readonly int[] EpochOffsets = [-2, -1, 0, 1, 2];

    public static void Run(CheckReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        const int recipientCount = 5;
        using TestIdentity sender = TestIdentity.FromName(SenderName);
        TestIdentity[] recipients = [.. Enumerable.Range(0, recipientCount).Select(i => TestIdentity.FromName("recipient-person-with-a-long-name-" + i))];
        using TestIdentity bystander = TestIdentity.FromName("bystander-who-is-not-a-recipient");
        using TestIdentity stranger = TestIdentity.FromName("stranger-who-knows-nobody");
        using ContactBook senderBook = ContactBook.Create(sender, [.. recipients, bystander], cacheStaticDh: false);
        ContactBook[] recipientBooks = [.. recipients.Select(recipient => ContactBook.Create(recipient, [sender, stranger], cacheStaticDh: false))];
        using ContactBook bystanderBook = ContactBook.Create(bystander, [sender], cacheStaticDh: false);
        using ContactBook strangerBook = ContactBook.Create(stranger, [recipients[0], bystander], cacheStaticDh: false);
        try
        {
            Contact[] chosen = [.. senderBook.Contacts.Take(recipientCount)];
            Setup setup = new(senderBook, recipients, recipientBooks, bystanderBook, strangerBook, chosen);
            CheckReport.Section("Private announcement for local discovery, without a clock (a fresh nonce and pairwise slots)");
            RunFormatChecks(report, setup, epoch: null);
            RunTamperChecks(report, setup, epoch: null);
            CheckReport.Section("Private announcement for local discovery, with an epoch of " + Epochs.PeriodSeconds + " s (optional hardening)");
            RunFormatChecks(report, setup, CurrentEpoch);
            RunWindowChecks(report, setup);
            RunTamperChecks(report, setup, CurrentEpoch);
            RunScaleChecks(report);
        }
        finally
        {
            foreach (ContactBook recipientBook in recipientBooks)
            {
                recipientBook.Dispose();
            }

            foreach (TestIdentity recipient in recipients)
            {
                recipient.Dispose();
            }
        }
    }

    private static void RunFormatChecks(CheckReport report, Setup setup, long? epoch)
    {
        byte[] beacon = Announcement.Build(setup.Chosen, Port, epoch);

        report.Run("size: the same for 1, 5 and 32 recipients", () =>
        {
            int[] sizes = [.. SizeCases.Select(count => Announcement.Build(CreateRecipients(setup.SenderBook, count), Port, epoch).Length)];
            return sizes.All(size => size == Announcement.Length)
                ? CheckOutcome.Pass($"{Announcement.Length} bytes: {Announcement.NonceLength} nonce, {Announcement.PortLength} port, {Announcement.SlotCount} slots of {Announcement.SlotLength}; a device with few contacts looks like one with many")
                : CheckOutcome.Fail("sizes differ: " + string.Join(", ", sizes));
        });

        report.Run("recognition: each chosen recipient recognizes the sender and the port", () =>
        {
            for (int i = 0; i < setup.RecipientBooks.Length; i++)
            {
                if (!Announcement.TryRecognize(beacon, setup.RecipientBooks[i], epoch, out RecognizedAnnouncement? recognized, out _)
                    || recognized?.Sender.Name != SenderName
                    || recognized.Port != Port
                    || recognized.Epoch != epoch)
                {
                    return CheckOutcome.Fail($"recipient {i} did not recognize the announcement");
                }
            }

            return CheckOutcome.Pass($"{setup.RecipientBooks.Length} of {setup.RecipientBooks.Length} recognized it");
        });

        report.Run("recognition: a contact who is not among the recipients, and a stranger, recognize nothing", () =>
            !Announcement.TryRecognize(beacon, setup.BystanderBook, epoch, out _, out _)
            && !Announcement.TryRecognize(beacon, setup.StrangerBook, epoch, out _, out _)
            && setup.Chosen.Length == setup.Recipients.Length
                ? CheckOutcome.Pass("not recognized")
                : CheckOutcome.Fail("recognized"));

        report.Run("privacy: no name, public key or contact data appears in the bytes", () =>
        {
            List<byte[]> secrets = [Encoding.UTF8.GetBytes(SenderName), Encoding.Unicode.GetBytes(SenderName)];
            foreach (TestIdentity recipient in setup.Recipients)
            {
                secrets.Add(Encoding.UTF8.GetBytes(recipient.Name));
                secrets.Add(Encoding.Unicode.GetBytes(recipient.Name));
                secrets.Add(recipient.PublicKey.ToArray());
            }

            secrets.Add(setup.SenderBook.Self.PublicKey.ToArray());
            int found = secrets.Count(secret => beacon.AsSpan().IndexOf(secret) >= 0);
            return found == 0
                ? CheckOutcome.Pass($"none of {secrets.Count} names and keys found; the whole announcement is random-looking bytes")
                : CheckOutcome.Fail($"{found} found");
        });

        report.Run("two announcements from one device share nothing an observer could link", () =>
        {
            byte[] other = Announcement.Build(setup.Chosen, Port, epoch);
            bool sameNonce = beacon.AsSpan(0, Announcement.NonceLength).SequenceEqual(other.AsSpan(0, Announcement.NonceLength));
            int equalSlots = 0;
            for (int slot = 0; slot < Announcement.SlotCount; slot++)
            {
                ReadOnlySpan<byte> first = beacon.AsSpan(Announcement.SlotsOffset + (slot * Announcement.SlotLength), Announcement.SlotLength);
                for (int otherSlot = 0; otherSlot < Announcement.SlotCount; otherSlot++)
                {
                    if (first.SequenceEqual(other.AsSpan(Announcement.SlotsOffset + (otherSlot * Announcement.SlotLength), Announcement.SlotLength)))
                    {
                        equalSlots++;
                    }
                }
            }

            return !sameNonce && equalSlots == 0
                ? CheckOutcome.Pass("another nonce and no slot value in common, in any position")
                : CheckOutcome.Fail($"same nonce: {sameNonce}; equal slots: {equalSlots}");
        });

        report.Run("replay: a recorded announcement is recognized again", () =>
            Announcement.TryRecognize(beacon, setup.RecipientBooks[0], epoch, out _, out _) && Announcement.TryRecognize(beacon, setup.RecipientBooks[0], epoch, out _, out _)
                ? CheckOutcome.Info(epoch is null
                    ? "recognized again, at any time and place: it is only a hint to dial, and the Noise handshake still needs the contact's private key; a replay can make a device dial an address the replayer chose"
                    : "recognized again inside the window: it is only a hint to dial; the window limits how long a replayer can use it, and a wrong or manipulated clock moves the window")
                : CheckOutcome.Fail("not recognized"));
    }

    private static void RunWindowChecks(CheckReport report, Setup setup)
    {
        byte[] beacon = Announcement.Build(setup.Chosen, Port, CurrentEpoch);
        report.Run("epoch window: accepted one epoch before and after, refused two away", () =>
        {
            bool[] accepted = [.. EpochOffsets.Select(offset => Announcement.TryRecognize(beacon, setup.RecipientBooks[0], CurrentEpoch + offset, out _, out _))];
            bool withoutClock = Announcement.TryRecognize(beacon, setup.RecipientBooks[0], null, out _, out _);
            return accepted.SequenceEqual([false, true, true, true, false]) && !withoutClock
                ? CheckOutcome.Pass("accepted at -1, 0 and +1; refused at -2 and +2, and by a receiver that uses no clock: a clock that is off by more than one epoch breaks discovery")
                : CheckOutcome.Fail("window " + string.Join(", ", accepted) + "; without a clock: " + withoutClock);
        });
    }

    private static void RunTamperChecks(CheckReport report, Setup setup, long? epoch)
    {
        byte[] beacon = Announcement.Build(setup.Chosen, Port, epoch);
        report.Run("tamper sweep: every single-bit change of the announcement", () =>
        {
            int headerFlips = 0;
            int slotFlipsAffecting = 0;
            int slotFlipsNoEffect = 0;
            byte[] copy = [.. beacon];
            ContactBook[] books = setup.RecipientBooks;
            for (int bit = 0; bit < copy.Length * 8; bit++)
            {
                copy[bit / 8] ^= (byte)(1 << (bit % 8));
                int failing = books.Count(recipientBook => !Announcement.TryRecognize(copy, recipientBook, epoch, out _, out _));
                bool outsider = Announcement.TryRecognize(copy, setup.BystanderBook, epoch, out _, out _) || Announcement.TryRecognize(copy, setup.StrangerBook, epoch, out _, out _);
                copy[bit / 8] ^= (byte)(1 << (bit % 8));
                if (outsider)
                {
                    return CheckOutcome.Fail($"bit {bit} made an outsider recognize the announcement");
                }

                if (bit / 8 < Announcement.SlotsOffset)
                {
                    headerFlips++;
                    if (failing != books.Length)
                    {
                        return CheckOutcome.Fail($"header bit {bit}: only {failing} of {books.Length} recipients refused it");
                    }
                }
                else if (failing == 1)
                {
                    slotFlipsAffecting++;
                }
                else if (failing == 0)
                {
                    slotFlipsNoEffect++;
                }
                else
                {
                    return CheckOutcome.Fail($"slot bit {bit} affected {failing} recipients");
                }
            }

            int expectedAffecting = books.Length * Announcement.SlotLength * 8;
            int expectedNoEffect = (Announcement.SlotCount - books.Length) * Announcement.SlotLength * 8;
            return slotFlipsAffecting == expectedAffecting && slotFlipsNoEffect == expectedNoEffect
                ? CheckOutcome.Pass($"{copy.Length * 8} changed copies: {headerFlips} in nonce and port (every recipient refuses), {slotFlipsAffecting} in a recipient's own slot (only that recipient refuses), {slotFlipsNoEffect} in padding slots (no effect); no outsider ever recognized one")
                : CheckOutcome.Fail($"affecting {slotFlipsAffecting} (expected {expectedAffecting}), no effect {slotFlipsNoEffect} (expected {expectedNoEffect})");
        });

        report.Run("wrong lengths are refused", () =>
        {
            foreach (int length in new[] { 0, 1, Announcement.Length - 1, Announcement.Length + 1 })
            {
                if (Announcement.TryRecognize(new byte[length], setup.RecipientBooks[0], epoch, out _, out _))
                {
                    return CheckOutcome.Fail($"length {length} recognized");
                }
            }

            return CheckOutcome.Pass("refused");
        });

        report.Run("false positives: random datagrams are never recognized", () =>
        {
            const int datagrams = 20_000;
            byte[] datagram = new byte[Announcement.Length];
            for (int i = 0; i < datagrams; i++)
            {
                RandomNumberGenerator.Fill(datagram);
                if (Announcement.TryRecognize(datagram, setup.RecipientBooks[0], epoch, out _, out _))
                {
                    return CheckOutcome.Fail($"datagram {i} was recognized");
                }
            }

            return CheckOutcome.Pass($"0 of {datagrams} random datagrams recognized (a slot is 8 bytes, so a false match is about 2^-64 per slot)");
        });
    }

    private static void RunScaleChecks(CheckReport report)
    {
        report.Run("more than 32 contacts: several announcements, each recipient recognized in exactly one", () =>
        {
            const int count = 70;
            using TestIdentity sender = TestIdentity.FromName(SenderName + "-many");
            TestIdentity[] recipients = [.. Enumerable.Range(0, count).Select(i => TestIdentity.FromName("many-recipient-" + i))];
            try
            {
                using ContactBook senderBook = ContactBook.Create(sender, recipients, cacheStaticDh: false);
                IReadOnlyList<byte[]> beacons = Announcement.BuildAll(senderBook, Port);
                int[] recognizedIn = new int[count];
                for (int i = 0; i < count; i++)
                {
                    using ContactBook recipientBook = ContactBook.Create(recipients[i], [sender], cacheStaticDh: false);
                    foreach (byte[] beacon in beacons)
                    {
                        if (Announcement.TryRecognize(beacon, recipientBook, null, out _, out _))
                        {
                            recognizedIn[i]++;
                        }
                    }
                }

                return beacons.Count == 3 && recognizedIn.All(times => times == 1)
                    ? CheckOutcome.Pass($"{count} contacts: {beacons.Count} announcements of {Announcement.Length} bytes, every recipient recognized in exactly one")
                    : CheckOutcome.Fail($"{beacons.Count} announcements; recognized {string.Join(",", recognizedIn.Distinct())} times");
            }
            finally
            {
                foreach (TestIdentity recipient in recipients)
                {
                    recipient.Dispose();
                }
            }
        });
    }

    private static Contact[] CreateRecipients(ContactBook senderBook, int count)
    {
        while (senderBook.Contacts.Count < count)
        {
            using TestIdentity extra = TestIdentity.FromName("extra-recipient-" + senderBook.Contacts.Count);
            senderBook.Add(extra.Name, extra.PublicKey.ToArray(), cacheStaticDh: false);
        }

        return [.. senderBook.Contacts.Take(count)];
    }

    private sealed record Setup(
        ContactBook SenderBook,
        TestIdentity[] Recipients,
        ContactBook[] RecipientBooks,
        ContactBook BystanderBook,
        ContactBook StrangerBook,
        Contact[] Chosen);
}
