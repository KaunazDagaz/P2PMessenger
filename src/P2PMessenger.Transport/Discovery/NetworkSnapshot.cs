using System.Net;

namespace P2PMessenger.Transport.Discovery;

public sealed class NetworkSnapshot
{
    public const int SmallestPrefix = 8;
    public const int LargestPrefix = 30;

    private static readonly string[] NotLocalNamePrefixes = ["rmnet", "ccmni", "v4-", "clat", "pdp", "ppp", "tun", "wwan", "wg"];

    private NetworkSnapshot(IReadOnlyList<LocalInterface> interfaces, bool hasOtherNetwork)
    {
        Interfaces = interfaces;
        HasOtherNetwork = hasOtherNetwork;
    }

    public static NetworkSnapshot None { get; } = new([], false);

    public IReadOnlyList<LocalInterface> Interfaces { get; }

    public bool HasOtherNetwork { get; }

    public bool HasAnyNetwork => Interfaces.Count > 0 || HasOtherNetwork;

    public static NetworkSnapshot Create(IEnumerable<LocalInterface> candidates, bool hasOtherNetwork)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        List<LocalInterface> local = [];
        HashSet<IPAddress> addresses = [];
        bool other = hasOtherNetwork;
        foreach (LocalInterface candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (IPAddress.IsLoopback(candidate.Address))
            {
                continue;
            }

            if (!IsLocal(candidate))
            {
                other = true;
                continue;
            }

            if (addresses.Add(candidate.Address))
            {
                local.Add(candidate);
            }
        }

        local.Sort(static (first, second) =>
        {
            int byName = string.CompareOrdinal(first.Name, second.Name);
            return byName != 0 ? byName : LocalInterface.ToValue(first.Address).CompareTo(LocalInterface.ToValue(second.Address));
        });
        return new NetworkSnapshot(local, other);
    }

    public static bool IsLocal(LocalInterface candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        uint value = LocalInterface.ToValue(candidate.Address);
        bool linkLocal = (value & 0xFFFF0000) == 0xA9FE0000;
        bool unspecified = value == 0;
        return !linkLocal
            && !unspecified
            && !IPAddress.IsLoopback(candidate.Address)
            && candidate.PrefixLength is >= SmallestPrefix and <= LargestPrefix
            && !Array.Exists(NotLocalNamePrefixes, prefix => candidate.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsSameAs(NetworkSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return HasOtherNetwork == other.HasOtherNetwork && Interfaces.SequenceEqual(other.Interfaces);
    }
}
