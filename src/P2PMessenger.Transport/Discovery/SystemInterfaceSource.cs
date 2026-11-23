using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace P2PMessenger.Transport.Discovery;

public sealed class SystemInterfaceSource : ILocalInterfaceSource
{
    public NetworkSnapshot Read()
    {
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception exception) when (exception is NetworkInformationException or PlatformNotSupportedException)
        {
            return NetworkSnapshot.None;
        }

        List<LocalInterface> candidates = [];
        bool hasOtherNetwork = false;
        foreach (NetworkInterface adapter in adapters)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = adapter.GetIPProperties();
            }
            catch (Exception exception) when (exception is NetworkInformationException or PlatformNotSupportedException)
            {
                continue;
            }

            bool notLocal = adapter.NetworkInterfaceType is NetworkInterfaceType.Ppp
                or NetworkInterfaceType.Tunnel
                or NetworkInterfaceType.Wwanpp
                or NetworkInterfaceType.Wwanpp2;
            int index = IndexOf(properties);
            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                LocalInterface candidate = new(adapter.Name, index, unicast.Address, PrefixOf(unicast), adapter.SupportsMulticast);
                if (notLocal)
                {
                    hasOtherNetwork |= !System.Net.IPAddress.IsLoopback(unicast.Address);
                }
                else
                {
                    candidates.Add(candidate);
                }
            }
        }

        return NetworkSnapshot.Create(candidates, hasOtherNetwork);
    }

    private static int IndexOf(IPInterfaceProperties properties)
    {
        try
        {
            return properties.GetIPv4Properties()?.Index ?? 0;
        }
        catch (Exception exception) when (exception is NetworkInformationException or PlatformNotSupportedException)
        {
            return 0;
        }
    }

    private static int PrefixOf(UnicastIPAddressInformation unicast)
    {
        try
        {
            return unicast.PrefixLength is >= 0 and <= 32 ? unicast.PrefixLength : 32;
        }
        catch (Exception exception) when (exception is NetworkInformationException or PlatformNotSupportedException)
        {
            return 32;
        }
    }
}
