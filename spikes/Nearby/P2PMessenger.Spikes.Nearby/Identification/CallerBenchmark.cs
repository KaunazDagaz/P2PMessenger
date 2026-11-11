using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace P2PMessenger.Spikes.Nearby.Identification;

public static class CallerBenchmark
{
    private const int Port = 47800;

    private static readonly byte[] Prologue = Encoding.ASCII.GetBytes("P2PM/s05/kk/v1");
    private static readonly int[] DefaultCounts = [1, 10, 50, 100, 500];

    public static int Run(int[]? contactCounts = null)
    {
        int[] counts = contactCounts is { Length: > 0 } ? contactCounts : DefaultCounts;
        EnvironmentReport.Print();
        Console.WriteLine();
        Console.WriteLine("Caller identification: what the responder spends on one inbound connection attempt, median in microseconds.");
        Console.WriteLine("known = the caller is the contact in the middle of the list; unknown = nobody matches, so every contact is tried.");
        Console.WriteLine("trial naive = one full KK message 1 read per contact; trial cached = our own reader with the static-static key agreements kept per contact;");
        Console.WriteLine("hint = one keyed BLAKE2b check per contact; tag lookup = one dictionary lookup (the table holds 3 epochs per contact);");
        Console.WriteLine("announcement unknown = a datagram nobody in the book recognizes, so every contact is checked (3 epochs per contact with an epoch).");
        Console.WriteLine();
        Console.WriteLine("| Contacts | Trial naive known | Trial naive unknown | Trial cached known | Trial cached unknown | Hint known | Hint unknown | Tag lookup | Tag table refresh | Announcement unknown, no clock | Announcement unknown, epoch | Book setup per contact |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");

        using TestIdentity me = TestIdentity.FromName("bench-me");
        using TestIdentity stranger = TestIdentity.FromName("bench-stranger");
        foreach (int count in counts)
        {
            RunOne(me, stranger, count);
        }

        Console.WriteLine();
        Console.WriteLine("Per contact the responder keeps: trial naive nothing but the public key; trial cached the public key and 32 bytes of key agreement output;");
        Console.WriteLine("hint and tag one NSec key (one protected memory region) for the pairwise MAC key, and the tag table 3 entries of 16 bytes.");
        return 0;
    }

    private static void RunOne(TestIdentity me, TestIdentity stranger, int count)
    {
        TestIdentity[] others = [.. Enumerable.Range(0, count).Select(i => TestIdentity.FromName("bench-contact-" + i))];
        try
        {
            long setupStart = Stopwatch.GetTimestamp();
            using ContactBook book = ContactBook.Create(me, others, cacheStaticDh: true);
            double setupPerContact = Stopwatch.GetElapsedTime(setupStart).TotalMilliseconds * 1000 / count;

            TestIdentity known = others[count / 2];
            byte[] knownMessage = IdentificationChecks.BuildMessage1(known, me.PublicKey, Prologue, default);
            byte[] unknownMessage = IdentificationChecks.BuildMessage1(stranger, me.PublicKey, Prologue, default);

            using ContactBook knownBook = ContactBook.Create(known, [me], cacheStaticDh: false);
            using ContactBook strangerBook = ContactBook.Create(stranger, [me], cacheStaticDh: false);
            byte[] knownHint = ConnectionHints.BuildNoncePreamble(knownBook.Contacts[0]);
            byte[] unknownHint = ConnectionHints.BuildNoncePreamble(strangerBook.Contacts[0]);

            long epoch = Epochs.Of(DateTimeOffset.UtcNow);
            EpochTagTable table = new();
            byte[] knownTag = ConnectionHints.BuildEpochTag(knownBook.Contacts[0], epoch);

            int slowRepetitions = Math.Clamp(1500 / count, 3, 60);
            int fastRepetitions = Math.Clamp(20_000 / count, 20, 2000);

            double naiveKnown = Median(slowRepetitions, () => _ = TrialIdentifier.IdentifyNaive(book, Prologue, knownMessage));
            double naiveUnknown = Median(slowRepetitions, () => _ = TrialIdentifier.IdentifyNaive(book, Prologue, unknownMessage));
            double cachedKnown = Median(fastRepetitions, () => _ = TrialIdentifier.IdentifyCached(book, Prologue, knownMessage));
            double cachedUnknown = Median(fastRepetitions, () => _ = TrialIdentifier.IdentifyCached(book, Prologue, unknownMessage));
            double hintKnown = Median(fastRepetitions, () => ConnectionHints.TryIdentifyNonce(book, knownHint, out _, out _));
            double hintUnknown = Median(fastRepetitions, () => ConnectionHints.TryIdentifyNonce(book, unknownHint, out _, out _));
            double refresh = Median(Math.Clamp(fastRepetitions / 10, 5, 100), () => table.Refresh(book, epoch));
            double lookup = Median(fastRepetitions * 10, () => table.TryIdentify(knownTag, out _));
            byte[] unknownBeacon = Announcement.Build(strangerBook.Contacts, Port);
            byte[] unknownEpochBeacon = Announcement.Build(strangerBook.Contacts, Port, epoch);
            double beaconFree = Median(fastRepetitions, () => Announcement.TryRecognize(unknownBeacon, book, null, out _, out _));
            double beaconEpoch = Median(fastRepetitions, () => Announcement.TryRecognize(unknownEpochBeacon, book, epoch + 5, out _, out _));

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {count} | {naiveKnown:F1} | {naiveUnknown:F1} | {cachedKnown:F1} | {cachedUnknown:F1} | {hintKnown:F2} | {hintUnknown:F2} | {lookup:F2} | {refresh:F0} | {beaconFree:F1} | {beaconEpoch:F1} | {setupPerContact:F0} |"));
        }
        finally
        {
            foreach (TestIdentity other in others)
            {
                other.Dispose();
            }
        }
    }

    private static double Median(int repetitions, Action action)
    {
        for (int i = 0; i < 3; i++)
        {
            action();
        }

        double[] times = new double[repetitions];
        for (int i = 0; i < repetitions; i++)
        {
            long start = Stopwatch.GetTimestamp();
            action();
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds * 1000;
        }

        Array.Sort(times);
        return times[repetitions / 2];
    }
}
