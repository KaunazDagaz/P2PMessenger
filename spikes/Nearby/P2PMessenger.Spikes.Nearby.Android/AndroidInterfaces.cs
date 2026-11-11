using System.Net;
using Android.Runtime;
using Java.Util;
using P2PMessenger.Spikes.Nearby.Net;
using JavaInet4Address = Java.Net.Inet4Address;
using JavaNetworkInterface = Java.Net.NetworkInterface;

namespace P2PMessenger.Spikes.Nearby.Android;

internal static class AndroidInterfaces
{
    public static IReadOnlyList<LocalInterface> EnumerateJava()
    {
        List<LocalInterface> result = [];
        IEnumeration? interfaces = JavaNetworkInterface.NetworkInterfaces;
        while (interfaces is not null && interfaces.HasMoreElements)
        {
            if (interfaces.NextElement() is not Java.Lang.Object element)
            {
                continue;
            }

            using JavaNetworkInterface? item = element.JavaCast<JavaNetworkInterface>();
            if (item is null || !item.IsUp || item.IsLoopback || item.InterfaceAddresses is null)
            {
                continue;
            }

            foreach (Java.Net.InterfaceAddress? address in item.InterfaceAddresses)
            {
                if (address?.Address is JavaInet4Address inet && inet.HostAddress is { } text && IPAddress.TryParse(text, out IPAddress? ip))
                {
                    result.Add(new LocalInterface(item.Name ?? "?", item.Index, ip, address.NetworkPrefixLength, item.SupportsMulticast()));
                }
            }
        }

        return result;
    }

    public static IReadOnlyList<LocalInterface> EnumerateDotnet() => InterfaceCatalog.Enumerate();

    public static void PrintBoth()
    {
        Print(".NET NetworkInterface", EnumerateDotnet);
        Print("Java NetworkInterface", EnumerateJava);
    }

    private static void Print(string title, Func<IReadOnlyList<LocalInterface>> source)
    {
        try
        {
            IReadOnlyList<LocalInterface> items = source();
            Console.WriteLine($"{title}: {items.Count} IPv4 addresses");
            foreach (LocalInterface item in items)
            {
                Console.WriteLine($"  {item.Name}  {item.Address}/{item.PrefixLength}  broadcast {item.Broadcast}  multicast {(item.SupportsMulticast ? "yes" : "no")}  index {item.Index}");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.WriteLine($"{title} failed: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
