using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace P2PMessenger.Spikes.Nearby.Net;

public sealed record LocalInterface(string Name, int Index, IPAddress Address, int PrefixLength, bool SupportsMulticast)
{
    public uint Mask => PrefixLength == 0 ? 0 : uint.MaxValue << (32 - PrefixLength);

    public IPAddress Network => ToAddress(ToValue(Address) & Mask);

    public IPAddress Broadcast => ToAddress(ToValue(Address) | ~Mask);

    public long HostCount => PrefixLength >= 31 ? 0 : (1L << (32 - PrefixLength)) - 2;

    public bool Contains(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork && (ToValue(address) & Mask) == ToValue(Network);

    public IEnumerable<IPAddress> Hosts()
    {
        uint first = ToValue(Network) + 1;
        uint last = ToValue(Broadcast) - 1;
        for (uint value = first; value <= last; value++)
        {
            yield return ToAddress(value);
        }
    }

    public override string ToString() => $"{Name} {Address}/{PrefixLength}";

    private static uint ToValue(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

    private static IPAddress ToAddress(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }
}

public static class InterfaceCatalog
{
    private static readonly string[] NonLocalPrefixes = ["rmnet", "ccmni", "v4-", "clat", "pdp", "ppp", "tun", "wwan"];

    public static IReadOnlyList<LocalInterface> Enumerate()
    {
        List<LocalInterface> result = [];
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up
                || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            IPInterfaceProperties properties = adapter.GetIPProperties();
            int index = 0;
            try
            {
                index = properties.GetIPv4Properties()?.Index ?? 0;
            }
            catch (NetworkInformationException)
            {
                index = 0;
            }

            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    result.Add(new LocalInterface(adapter.Name, index, unicast.Address, unicast.PrefixLength, adapter.SupportsMulticast));
                }
            }
        }

        return result;
    }

    public static IReadOnlyList<LocalInterface> Select(IEnumerable<LocalInterface> all, IReadOnlyList<string> filters)
    {
        IEnumerable<LocalInterface> candidates = all.Where(IsUsable);
        if (filters.Count > 0)
        {
            candidates = candidates.Where(item => filters.Any(filter =>
                item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Address.ToString(), filter, StringComparison.Ordinal)));
        }

        return [.. candidates.OrderBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Address.ToString(), StringComparer.Ordinal)];
    }

    public static bool IsUsable(LocalInterface item)
    {
        byte[] bytes = item.Address.GetAddressBytes();
        bool linkLocal = bytes[0] == 169 && bytes[1] == 254;
        return !IPAddress.IsLoopback(item.Address) && !linkLocal && item.PrefixLength is >= 8 and <= 30 && !IsMobileOrTunnel(item.Name);
    }

    public static bool IsMobileOrTunnel(string name) =>
        NonLocalPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
