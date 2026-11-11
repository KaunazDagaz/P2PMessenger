using System.Text;
using P2PMessenger.Spikes.Nearby.Checks;
using P2PMessenger.Spikes.Nearby.Noise;

namespace P2PMessenger.Spikes.Nearby.Identification;

public static class IdentificationChecks
{
    private static readonly byte[] Prologue = Encoding.ASCII.GetBytes("P2PM/s05/kk/v1");
    private static readonly byte[] Hello = Encoding.ASCII.GetBytes("hello");

    public static void Run(CheckReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        const int contactCount = 6;
        using TestIdentity me = TestIdentity.FromName("check-me");
        TestIdentity[] identities = [.. Enumerable.Range(0, contactCount).Select(i => TestIdentity.FromName("check-contact-" + i))];
        using TestIdentity stranger = TestIdentity.FromName("check-stranger");
        using TestIdentity impostor = TestIdentity.CreateRandom("check-contact-2");
        using ContactBook book = ContactBook.Create(me, identities, cacheStaticDh: true);
        ContactBook[] callerBooks = [.. identities.Select(identity => ContactBook.Create(identity, [me], cacheStaticDh: false))];
        using ContactBook strangerBook = ContactBook.Create(stranger, [me], cacheStaticDh: false);
        try
        {
            RunTrialChecks(report, me, identities, stranger, impostor, book);
            RunNonceHintChecks(report, identities, callerBooks, strangerBook, book);
            RunEpochTagChecks(report, callerBooks, strangerBook, book);
        }
        finally
        {
            foreach (ContactBook callerBook in callerBooks)
            {
                callerBook.Dispose();
            }

            foreach (TestIdentity identity in identities)
            {
                identity.Dispose();
            }
        }
    }

    public static byte[] BuildMessage1(TestIdentity initiator, ReadOnlySpan<byte> responderPublicKey, ReadOnlySpan<byte> prologue, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(initiator);
        using KkHandshake handshake = new(NoiseProtocol.KkBlake2b, initiator: true, prologue, initiator.StaticKey, responderPublicKey);
        byte[] message = new byte[payload.Length + KkHandshake.MessageOverhead];
        int written = handshake.WriteMessage(payload, message);
        return message[..written];
    }

    private static void RunTrialChecks(CheckReport report, TestIdentity me, TestIdentity[] identities, TestIdentity stranger, TestIdentity impostor, ContactBook book)
    {
        CheckReport.Section("Caller identification by trying each contact's key (KK message 1)");
        byte[][] messages = [.. identities.Select(identity => BuildMessage1(identity, me.PublicKey, Prologue, Hello))];

        report.Run("trial, naive: every contact is found, with its payload", () =>
        {
            for (int i = 0; i < identities.Length; i++)
            {
                TrialResult result = TrialIdentifier.IdentifyNaive(book, Prologue, messages[i]);
                if (result.Caller?.Name != identities[i].Name || result.Trials != i + 1 || result.Payload is null || !result.Payload.AsSpan().SequenceEqual(Hello))
                {
                    return CheckOutcome.Fail($"contact {i}: found {result.Caller?.Name ?? "nobody"} after {result.Trials} trials");
                }
            }

            return CheckOutcome.Pass($"{identities.Length} of {identities.Length} found; trial counts 1 to {identities.Length}");
        });

        report.Run("trial, cached DH: the same contact, trials and payload as the naive version", () =>
        {
            for (int i = 0; i < identities.Length; i++)
            {
                TrialResult naive = TrialIdentifier.IdentifyNaive(book, Prologue, messages[i]);
                TrialResult cached = TrialIdentifier.IdentifyCached(book, Prologue, messages[i]);
                if (cached.Caller?.Name != naive.Caller?.Name || cached.Trials != naive.Trials || cached.Payload is null || !cached.Payload.AsSpan().SequenceEqual(Hello))
                {
                    return CheckOutcome.Fail($"contact {i} differs between the two versions");
                }
            }

            return CheckOutcome.Pass("identical results for every contact");
        });

        report.Run("trial: a stranger and a contact with a changed key are refused after every contact was tried", () =>
        {
            foreach (TestIdentity caller in new[] { stranger, impostor })
            {
                byte[] message = BuildMessage1(caller, me.PublicKey, Prologue, Hello);
                TrialResult naive = TrialIdentifier.IdentifyNaive(book, Prologue, message);
                TrialResult cached = TrialIdentifier.IdentifyCached(book, Prologue, message);
                if (naive.Caller is not null || cached.Caller is not null || naive.Trials != identities.Length || cached.Trials != identities.Length)
                {
                    return CheckOutcome.Fail($"{caller.Name} was identified or not fully tried");
                }
            }

            return CheckOutcome.Pass($"both refused after {identities.Length} trials; the transport cannot tell a stranger from a contact with a new key");
        });

        report.Run("trial: another prologue is refused", () =>
        {
            byte[] message = BuildMessage1(identities[1], me.PublicKey, Encoding.ASCII.GetBytes("P2PM/s05/kk/v2"), Hello);
            return TrialIdentifier.IdentifyNaive(book, Prologue, message).Caller is null && TrialIdentifier.IdentifyCached(book, Prologue, message).Caller is null
                ? CheckOutcome.Pass("refused by both versions")
                : CheckOutcome.Fail("identified");
        });

        report.Run("trial: every single-bit change of message 1 is refused", () =>
        {
            byte[] message = [.. messages[3]];
            int flips = 0;
            for (int bit = 0; bit < message.Length * 8; bit++)
            {
                message[bit / 8] ^= (byte)(1 << (bit % 8));
                bool identified = TrialIdentifier.IdentifyNaive(book, Prologue, message).Caller is not null
                    || TrialIdentifier.IdentifyCached(book, Prologue, message).Caller is not null;
                message[bit / 8] ^= (byte)(1 << (bit % 8));
                flips++;
                if (identified)
                {
                    return CheckOutcome.Fail($"bit {bit} was identified");
                }
            }

            return CheckOutcome.Pass($"{flips} changed copies refused by both versions");
        });

        report.Run("trial: too short and too long messages are refused", () =>
        {
            foreach (int length in new[] { 0, 1, KkHandshake.MessageOverhead - 1, KkHandshake.MaxMessageLength + 1 })
            {
                byte[] message = new byte[length];
                if (TrialIdentifier.IdentifyNaive(book, Prologue, message).Caller is not null || TrialIdentifier.IdentifyCached(book, Prologue, message).Caller is not null)
                {
                    return CheckOutcome.Fail($"length {length} identified");
                }
            }

            return CheckOutcome.Pass("refused");
        });
    }

    private static void RunNonceHintChecks(CheckReport report, TestIdentity[] identities, ContactBook[] callerBooks, ContactBook strangerBook, ContactBook book)
    {
        CheckReport.Section("Caller identification by a pairwise hint (random nonce, no clock)");
        byte[][] preambles = [.. callerBooks.Select(callerBook => ConnectionHints.BuildNoncePreamble(callerBook.Contacts[0]))];

        report.Run("hint: every contact is found and no other contact matches", () =>
        {
            for (int i = 0; i < identities.Length; i++)
            {
                if (!ConnectionHints.TryIdentifyNonce(book, preambles[i], out Contact? caller, out int checks) || caller?.Name != identities[i].Name || checks != i + 1)
                {
                    return CheckOutcome.Fail($"contact {i}: found {caller?.Name ?? "nobody"} after {checks} checks");
                }
            }

            return CheckOutcome.Pass($"{identities.Length} of {identities.Length} found; {ConnectionHints.PreambleLength} bytes each");
        });

        report.Run("hint: a stranger is refused after every contact was checked", () =>
        {
            byte[] preamble = ConnectionHints.BuildNoncePreamble(strangerBook.Contacts[0]);
            return !ConnectionHints.TryIdentifyNonce(book, preamble, out _, out int checks) && checks == identities.Length
                ? CheckOutcome.Pass($"refused after {checks} checks")
                : CheckOutcome.Fail("identified or not fully checked");
        });

        report.Run("hint: every single-bit change is refused", () =>
        {
            byte[] preamble = [.. preambles[2]];
            for (int bit = 0; bit < preamble.Length * 8; bit++)
            {
                preamble[bit / 8] ^= (byte)(1 << (bit % 8));
                bool identified = ConnectionHints.TryIdentifyNonce(book, preamble, out _, out _);
                preamble[bit / 8] ^= (byte)(1 << (bit % 8));
                if (identified)
                {
                    return CheckOutcome.Fail($"bit {bit} was identified");
                }
            }

            return CheckOutcome.Pass($"{preamble.Length * 8} changed copies refused");
        });

        report.Run("hint: wrong lengths are refused", () =>
        {
            foreach (int length in new[] { 0, 1, ConnectionHints.PreambleLength - 1, ConnectionHints.PreambleLength + 1 })
            {
                if (ConnectionHints.TryIdentifyNonce(book, new byte[length], out _, out _))
                {
                    return CheckOutcome.Fail($"length {length} identified");
                }
            }

            return CheckOutcome.Pass("refused");
        });

        report.Run("hint: two hints of one pair share nothing", () =>
        {
            byte[] first = ConnectionHints.BuildNoncePreamble(callerBooks[0].Contacts[0]);
            byte[] second = ConnectionHints.BuildNoncePreamble(callerBooks[0].Contacts[0]);
            bool nonceDiffers = !first.AsSpan(0, ConnectionHints.NonceLength).SequenceEqual(second.AsSpan(0, ConnectionHints.NonceLength));
            bool tagDiffers = !first.AsSpan(ConnectionHints.NonceLength).SequenceEqual(second.AsSpan(ConnectionHints.NonceLength));
            return nonceDiffers && tagDiffers ? CheckOutcome.Pass("different nonce and different tag, so an observer cannot link two connections") : CheckOutcome.Fail("repeated bytes");
        });

        report.Run("hint: a recorded hint is accepted again (replay)", () =>
            ConnectionHints.TryIdentifyNonce(book, preambles[0], out Contact? first, out _)
            && ConnectionHints.TryIdentifyNonce(book, preambles[0], out Contact? second, out _)
            && first == second
                ? CheckOutcome.Info("accepted again, as expected: a hint only says who is calling, and the Noise handshake that follows needs the caller's private key; a replayed message 1 can still draw a message 2")
                : CheckOutcome.Fail("not accepted"));
    }

    private static void RunEpochTagChecks(CheckReport report, ContactBook[] callerBooks, ContactBook strangerBook, ContactBook book)
    {
        CheckReport.Section("Caller identification by a pairwise rotating tag (epoch " + Epochs.PeriodSeconds + " s)");
        const long epoch = 1_000_000;
        EpochTagTable table = new();
        table.Refresh(book, epoch);

        report.Run("epoch tag: every contact is found in the epochs before, at and after the receiver's", () =>
        {
            for (int i = 0; i < callerBooks.Length; i++)
            {
                for (long sender = epoch - 1; sender <= epoch + 1; sender++)
                {
                    byte[] tag = ConnectionHints.BuildEpochTag(callerBooks[i].Contacts[0], sender);
                    if (!table.TryIdentify(tag, out Contact? caller) || caller is null || caller.Name != book.Contacts[i].Name)
                    {
                        return CheckOutcome.Fail($"contact {i}, epoch {sender - epoch:+0;-0;0}");
                    }
                }
            }

            return CheckOutcome.Pass($"found in all 3 epochs; the table holds {table.Count} tags for {book.Contacts.Count} contacts");
        });

        report.Run("epoch tag: two or more epochs away, and strangers, are refused", () =>
        {
            foreach (long sender in new[] { epoch - 2, epoch + 2, epoch + 1000 })
            {
                if (table.TryIdentify(ConnectionHints.BuildEpochTag(callerBooks[0].Contacts[0], sender), out _))
                {
                    return CheckOutcome.Fail($"epoch {sender - epoch:+0;-0;0} was identified");
                }
            }

            return table.TryIdentify(ConnectionHints.BuildEpochTag(strangerBook.Contacts[0], epoch), out _)
                ? CheckOutcome.Fail("a stranger was identified")
                : CheckOutcome.Pass("refused: a clock that is off by more than one epoch breaks recognition");
        });

        report.Run("epoch tag: every single-bit change is refused", () =>
        {
            byte[] tag = ConnectionHints.BuildEpochTag(callerBooks[4].Contacts[0], epoch);
            for (int bit = 0; bit < tag.Length * 8; bit++)
            {
                tag[bit / 8] ^= (byte)(1 << (bit % 8));
                bool identified = table.TryIdentify(tag, out _);
                tag[bit / 8] ^= (byte)(1 << (bit % 8));
                if (identified)
                {
                    return CheckOutcome.Fail($"bit {bit} was identified");
                }
            }

            return CheckOutcome.Pass($"{tag.Length * 8} changed copies refused");
        });

        report.Run("epoch tag: the same pair shows the same tag for a whole epoch", () =>
        {
            byte[] first = ConnectionHints.BuildEpochTag(callerBooks[0].Contacts[0], epoch);
            byte[] second = ConnectionHints.BuildEpochTag(callerBooks[0].Contacts[0], epoch);
            byte[] next = ConnectionHints.BuildEpochTag(callerBooks[0].Contacts[0], epoch + 1);
            return first.AsSpan().SequenceEqual(second) && !first.AsSpan().SequenceEqual(next)
                ? CheckOutcome.Info("repeated within an epoch, different in the next: connections of one pair are linkable for up to one epoch")
                : CheckOutcome.Fail("unexpected tag behaviour");
        });
    }
}
