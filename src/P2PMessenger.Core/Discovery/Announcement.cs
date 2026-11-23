using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using NSec.Cryptography;
using P2PMessenger.Core.Sessions;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Core.Discovery;

public static class Announcement
{
    public const int NonceLength = 16;
    public const int PortLength = 2;
    public const int SlotLength = 8;
    public const int SlotCount = 32;
    public const int SlotsOffset = NonceLength + PortLength;
    public const int Length = SlotsOffset + (SlotCount * SlotLength);
    public const int MinPort = 1;
    public const int MaxPort = ushort.MaxValue;

    internal const byte SentByLowerKey = 1;
    internal const byte SentByHigherKey = 2;

    private static ReadOnlySpan<byte> Label => "P2PM/announcement/v1"u8;

    public static IReadOnlyList<byte[]> BuildAll(IReadOnlyList<SessionContact> recipients, int port)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        CheckPort(port);

        SessionContact[] shuffled = [.. recipients];
        if (Array.Exists(shuffled, static recipient => recipient is null))
        {
            throw new ArgumentException("A recipient is missing.", nameof(recipients));
        }

        RandomNumberGenerator.Shuffle(shuffled.AsSpan());
        List<byte[]> announcements = [];
        for (int start = 0; start < shuffled.Length; start += SlotCount)
        {
            int count = Math.Min(SlotCount, shuffled.Length - start);
            announcements.Add(Build(new ArraySegment<SessionContact>(shuffled, start, count), port));
        }

        return announcements;
    }

    public static byte[] Build(IReadOnlyList<SessionContact> recipients, int port)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        CheckPort(port);
        if (recipients.Count > SlotCount)
        {
            throw new ArgumentException("One announcement holds at most 32 recipients.", nameof(recipients));
        }

        byte[] announcement = new byte[Length];
        RandomNumberGenerator.Fill(announcement);
        Span<int> positions = stackalloc int[SlotCount];
        for (int index = 0; index < SlotCount; index++)
        {
            positions[index] = index;
        }

        RandomNumberGenerator.Shuffle(positions);
        Write(recipients, port, announcement.AsSpan(0, NonceLength), positions, announcement);
        return announcement;
    }

    public static bool TryRecognize(
        ReadOnlySpan<byte> announcement,
        IReadOnlyList<SessionContact> contacts,
        [NotNullWhen(true)] out SessionContact? sender,
        out int port)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        sender = null;
        port = 0;
        if (announcement.Length != Length)
        {
            return false;
        }

        int announcedPort = BinaryPrimitives.ReadUInt16BigEndian(announcement.Slice(NonceLength, PortLength));
        if (announcedPort < MinPort)
        {
            return false;
        }

        ReadOnlySpan<byte> header = announcement[..SlotsOffset];
        ReadOnlySpan<byte> slots = announcement[SlotsOffset..];
        Span<byte> expected = stackalloc byte[SlotLength];
        for (int index = 0; index < contacts.Count; index++)
        {
            SessionContact contact = contacts[index];
            ComputeSlot(contact, ownIsSender: false, header, expected);
            for (int slot = 0; slot < SlotCount; slot++)
            {
                if (CryptographicOperations.FixedTimeEquals(slots.Slice(slot * SlotLength, SlotLength), expected))
                {
                    sender = contact;
                    port = announcedPort;
                    return true;
                }
            }
        }

        return false;
    }

    internal static void Write(IReadOnlyList<SessionContact> recipients, int port, ReadOnlySpan<byte> nonce, ReadOnlySpan<int> positions, Span<byte> announcement)
    {
        if (nonce.Length != NonceLength || positions.Length < recipients.Count || announcement.Length != Length)
        {
            throw new ArgumentException("The nonce, the slot positions or the announcement have the wrong length.", nameof(announcement));
        }

        nonce.CopyTo(announcement);
        BinaryPrimitives.WriteUInt16BigEndian(announcement.Slice(NonceLength, PortLength), (ushort)port);
        ReadOnlySpan<byte> header = announcement[..SlotsOffset];
        for (int index = 0; index < recipients.Count; index++)
        {
            int position = positions[index];
            if (position is < 0 or >= SlotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(positions));
            }

            ComputeSlot(recipients[index], ownIsSender: true, header, announcement.Slice(SlotsOffset + (position * SlotLength), SlotLength));
        }
    }

    internal static void ComputeSlot(SessionContact contact, bool ownIsSender, ReadOnlySpan<byte> header, Span<byte> slot)
    {
        bool senderIsLower = ownIsSender ? contact.OwnStaticKeyIsLower : !contact.OwnStaticKeyIsLower;
        Span<byte> data = stackalloc byte[Label.Length + 1 + SlotsOffset];
        Label.CopyTo(data);
        data[Label.Length] = senderIsLower ? SentByLowerKey : SentByHigherKey;
        header.CopyTo(data[(Label.Length + 1)..]);
        Span<byte> mac = stackalloc byte[MacAlgorithm.Blake2b_128.MacSize];
        MacAlgorithm.Blake2b_128.Mac(contact.AnnouncementKey, data, mac);
        mac[..SlotLength].CopyTo(slot);
    }

    private static void CheckPort(int port)
    {
        if (port is < MinPort or > MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "The port must be from 1 to 65535.");
        }
    }
}
