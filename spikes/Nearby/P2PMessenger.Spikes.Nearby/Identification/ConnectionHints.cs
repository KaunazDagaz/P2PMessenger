using System.Buffers.Binary;
using System.Text;
using NSec.Cryptography;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Spikes.Nearby.Identification;

public static class ConnectionHints
{
    public const int NonceLength = 16;
    public const int TagLength = 16;
    public const int PreambleLength = NonceLength + TagLength;

    private static readonly byte[] NonceLabel = Encoding.ASCII.GetBytes("P2PM/s05/conn-hint/v1");
    private static readonly byte[] EpochLabel = Encoding.ASCII.GetBytes("P2PM/s05/conn-epoch/v1");

    public static byte[] BuildNoncePreamble(Contact target)
    {
        ArgumentNullException.ThrowIfNull(target);
        byte[] preamble = new byte[PreambleLength];
        RandomNumberGenerator.Fill(preamble.AsSpan(0, NonceLength));
        Span<byte> data = stackalloc byte[NonceLabel.Length + NonceLength];
        NonceLabel.CopyTo(data);
        preamble.AsSpan(0, NonceLength).CopyTo(data[NonceLabel.Length..]);
        MacAlgorithm.Blake2b_128.Mac(target.PairwiseMacKey, data, preamble.AsSpan(NonceLength));
        return preamble;
    }

    public static bool TryIdentifyNonce(ContactBook book, ReadOnlySpan<byte> preamble, out Contact? caller, out int checks)
    {
        ArgumentNullException.ThrowIfNull(book);
        caller = null;
        checks = 0;
        if (preamble.Length != PreambleLength)
        {
            return false;
        }

        Span<byte> data = stackalloc byte[NonceLabel.Length + NonceLength];
        NonceLabel.CopyTo(data);
        preamble[..NonceLength].CopyTo(data[NonceLabel.Length..]);
        ReadOnlySpan<byte> tag = preamble[NonceLength..];
        foreach (Contact contact in book.Contacts)
        {
            checks++;
            if (MacAlgorithm.Blake2b_128.Verify(contact.PairwiseMacKey, data, tag))
            {
                caller = contact;
                return true;
            }
        }

        return false;
    }

    public static void ComputeEpochTag(Key pairwiseMacKey, long epoch, Span<byte> tag)
    {
        Span<byte> data = stackalloc byte[EpochLabel.Length + sizeof(long)];
        EpochLabel.CopyTo(data);
        BinaryPrimitives.WriteInt64BigEndian(data[EpochLabel.Length..], epoch);
        MacAlgorithm.Blake2b_128.Mac(pairwiseMacKey, data, tag);
    }

    public static byte[] BuildEpochTag(Contact target, long epoch)
    {
        ArgumentNullException.ThrowIfNull(target);
        byte[] tag = new byte[TagLength];
        ComputeEpochTag(target.PairwiseMacKey, epoch, tag);
        return tag;
    }
}

public sealed class EpochTagTable
{
    private readonly Dictionary<Guid, Contact> _tags = [];

    public long Epoch { get; private set; } = long.MinValue;

    public int Count => _tags.Count;

    public void Refresh(ContactBook book, long epoch)
    {
        ArgumentNullException.ThrowIfNull(book);
        _tags.Clear();
        Span<byte> tag = stackalloc byte[ConnectionHints.TagLength];
        for (long candidate = epoch - 1; candidate <= epoch + 1; candidate++)
        {
            foreach (Contact contact in book.Contacts)
            {
                ConnectionHints.ComputeEpochTag(contact.PairwiseMacKey, candidate, tag);
                _tags[new Guid(tag)] = contact;
            }
        }

        Epoch = epoch;
    }

    public bool TryIdentify(ReadOnlySpan<byte> tag, out Contact? contact)
    {
        contact = null;
        return tag.Length == ConnectionHints.TagLength && _tags.TryGetValue(new Guid(tag), out contact);
    }
}
