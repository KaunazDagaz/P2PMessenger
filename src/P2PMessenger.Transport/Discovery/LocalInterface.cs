using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace P2PMessenger.Transport.Discovery;

public sealed record LocalInterface
{
    public LocalInterface(string name, int index, IPAddress address, int prefixLength, bool supportsMulticast)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("A local interface address must be an IPv4 address.", nameof(address));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(prefixLength, 32);

        Name = name;
        Index = index;
        Address = address;
        PrefixLength = prefixLength;
        SupportsMulticast = supportsMulticast;
    }

    public string Name { get; }

    public int Index { get; }

    public IPAddress Address { get; }

    public int PrefixLength { get; }

    public bool SupportsMulticast { get; }

    public IPAddress Network => ToAddress(ToValue(Address) & Mask);

    public IPAddress Broadcast => ToAddress(ToValue(Address) | ~Mask);

    public long HostCount => PrefixLength >= 31 ? 0 : (1L << (32 - PrefixLength)) - 2;

    private uint Mask => PrefixLength == 0 ? 0 : uint.MaxValue << (32 - PrefixLength);

    public bool Contains(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return address.AddressFamily == AddressFamily.InterNetwork && (ToValue(address) & Mask) == (ToValue(Address) & Mask);
    }

    public IEnumerable<IPAddress> OtherHosts()
    {
        if (HostCount == 0)
        {
            yield break;
        }

        uint own = ToValue(Address);
        uint first = (own & Mask) + 1;
        uint last = (own | ~Mask) - 1;
        for (uint value = first; value <= last; value++)
        {
            if (value != own)
            {
                yield return ToAddress(value);
            }
        }
    }

    public override string ToString() => $"{Name} {Address}/{PrefixLength}";

    internal static uint ToValue(IPAddress address)
    {
        Span<byte> bytes = stackalloc byte[4];
        return address.TryWriteBytes(bytes, out int written) && written == 4 ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : 0;
    }

    private static IPAddress ToAddress(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }
}
