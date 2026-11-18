using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace P2PMessenger.Application.Connections;

public static class LocalAddresses
{
    public static IReadOnlyList<IPAddress> Find()
    {
        try
        {
            return
            [
                .. NetworkInterface.GetAllNetworkInterfaces()
                    .Where(static network => network.OperationalStatus != OperationalStatus.Down
                        && network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(static network => network.GetIPProperties().UnicastAddresses)
                    .Select(static unicast => unicast.Address)
                    .Where(static address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    .Distinct(),
            ];
        }
        catch (Exception exception) when (exception is NetworkInformationException or PlatformNotSupportedException)
        {
            return [];
        }
    }
}
