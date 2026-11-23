using System.Net;
using Android.Runtime;
using Java.Util;
using P2PMessenger.Transport.Discovery;
using JavaInet4Address = Java.Net.Inet4Address;
using JavaInterfaceAddress = Java.Net.InterfaceAddress;
using JavaNetworkInterface = Java.Net.NetworkInterface;

namespace P2PMessenger.Android;

internal sealed class AndroidInterfaceSource : ILocalInterfaceSource
{
    private readonly SystemInterfaceSource fallback = new();

    public NetworkSnapshot Read()
    {
        try
        {
            List<LocalInterface> candidates = ReadJava();
            if (candidates.Count > 0)
            {
                return NetworkSnapshot.Create(candidates, hasOtherNetwork: false);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return fallback.Read();
        }

        return fallback.Read();
    }

    private static List<LocalInterface> ReadJava()
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
            if (item is null || !item.IsUp || item.IsLoopback || item.InterfaceAddresses is not { } addresses)
            {
                continue;
            }

            string name = item.Name ?? string.Empty;
            bool supportsMulticast = item.SupportsMulticast();
            foreach (JavaInterfaceAddress? address in addresses)
            {
                if (address?.Address is JavaInet4Address inet
                    && inet.HostAddress is { } text
                    && IPAddress.TryParse(text, out IPAddress? parsed)
                    && address.NetworkPrefixLength is >= 0 and <= 32)
                {
                    result.Add(new LocalInterface(name, item.Index, parsed, address.NetworkPrefixLength, supportsMulticast));
                }
            }
        }

        return result;
    }
}
