using System.Buffers.Binary;
using System.Text;
using NSec.Cryptography;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Spikes.Nearby.Identification;

public sealed record RecognizedAnnouncement(Contact Sender, int Port, long? Epoch);

public static class Announcement
{
    public const int NonceLength = 16;
    public const int PortLength = 2;
    public const int SlotLength = 8;
    public const int SlotCount = 32;
    public const int SlotsOffset = NonceLength + PortLength;
    public const int Length = SlotsOffset + (SlotCount * SlotLength);

    private static readonly byte[] FreeLabel = Encoding.ASCII.GetBytes("P2PM/s05/announce/v1");
    private static readonly byte[] EpochLabel = Encoding.ASCII.GetBytes("P2PM/s05/announce-epoch/v1");

    public static IReadOnlyList<byte[]> BuildAll(ContactBook book, int port, long? epoch = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        Contact[] shuffled = [.. book.Contacts];
        RandomNumberGenerator.Shuffle(shuffled.AsSpan());
        int chunks = Math.Max(1, (shuffled.Length + SlotCount - 1) / SlotCount);
        List<byte[]> beacons = [];
        for (int chunk = 0; chunk < chunks; chunk++)
        {
            beacons.Add(Build(shuffled.Skip(chunk * SlotCount).Take(SlotCount).ToArray(), port, epoch));
        }

        return beacons;
    }

    public static byte[] Build(IReadOnlyList<Contact> recipients, int port, long? epoch = null)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        if (recipients.Count > SlotCount)
        {
            throw new ArgumentException("One announcement holds at most " + SlotCount + " recipients.", nameof(recipients));
        }

        if (port is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        byte[] beacon = new byte[Length];
        RandomNumberGenerator.Fill(beacon);
        BinaryPrimitives.WriteUInt16BigEndian(beacon.AsSpan(NonceLength, PortLength), (ushort)port);
        int[] positions = [.. Enumerable.Range(0, SlotCount)];
        RandomNumberGenerator.Shuffle(positions.AsSpan());
        for (int i = 0; i < recipients.Count; i++)
        {
            ComputeSlot(
                recipients[i].PairwiseMacKey,
                epoch,
                beacon.AsSpan(0, SlotsOffset),
                beacon.AsSpan(SlotsOffset + (positions[i] * SlotLength), SlotLength));
        }

        return beacon;
    }

    public static bool TryRecognize(ReadOnlySpan<byte> beacon, ContactBook book, long? currentEpoch, out RecognizedAnnouncement? recognized, out int checks)
    {
        ArgumentNullException.ThrowIfNull(book);
        recognized = null;
        checks = 0;
        if (beacon.Length != Length)
        {
            return false;
        }

        ReadOnlySpan<byte> header = beacon[..SlotsOffset];
        ReadOnlySpan<byte> slots = beacon[SlotsOffset..];
        int port = BinaryPrimitives.ReadUInt16BigEndian(beacon.Slice(NonceLength, PortLength));
        Span<byte> expected = stackalloc byte[SlotLength];
        long?[] epochs = currentEpoch is { } now ? [now, now - 1, now + 1] : [null];
        foreach (long? epoch in epochs)
        {
            foreach (Contact contact in book.Contacts)
            {
                checks++;
                ComputeSlot(contact.PairwiseMacKey, epoch, header, expected);
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    if (CryptographicOperations.FixedTimeEquals(slots.Slice(slot * SlotLength, SlotLength), expected))
                    {
                        recognized = new RecognizedAnnouncement(contact, port, epoch);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static void ComputeSlot(Key pairwiseMacKey, long? epoch, ReadOnlySpan<byte> header, Span<byte> slot)
    {
        byte[] label = epoch is null ? FreeLabel : EpochLabel;
        int epochLength = epoch is null ? 0 : sizeof(long);
        Span<byte> data = stackalloc byte[label.Length + epochLength + SlotsOffset];
        label.CopyTo(data);
        if (epoch is { } value)
        {
            BinaryPrimitives.WriteInt64BigEndian(data[label.Length..], value);
        }

        header.CopyTo(data[(label.Length + epochLength)..]);
        Span<byte> mac = stackalloc byte[MacAlgorithm.Blake2b_128.MacSize];
        MacAlgorithm.Blake2b_128.Mac(pairwiseMacKey, data, mac);
        mac[..SlotLength].CopyTo(slot);
    }
}
