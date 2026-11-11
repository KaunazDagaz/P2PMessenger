using System.Globalization;

namespace P2PMessenger.Spikes.Nearby.Net;

public static class InterfaceReport
{
    public static void Print(IReadOnlyList<string>? filters = null)
    {
        IReadOnlyList<LocalInterface> all = InterfaceCatalog.Enumerate();
        IReadOnlyList<LocalInterface> selected = InterfaceCatalog.Select(all, filters ?? []);
        Console.WriteLine($"Local time {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}; UTC {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine("IPv4 interfaces that are up (the node uses the ones marked *):");
        foreach (LocalInterface item in all.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            string mark = selected.Contains(item) ? "*" : " ";
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {mark} {item.Name,-34} {item.Address}/{item.PrefixLength}  network {item.Network}  broadcast {item.Broadcast}  multicast {(item.SupportsMulticast ? "yes" : "no")}  hosts {item.HostCount}  index {item.Index}"));
        }

        if (all.Count == 0)
        {
            Console.WriteLine("  none");
        }

        Console.WriteLine($"The sweep covers an interface only when it has at most {new NodeOptions().SweepMaxHosts} addresses; the multicast group is {new NodeOptions().MulticastGroup}.");
    }
}
