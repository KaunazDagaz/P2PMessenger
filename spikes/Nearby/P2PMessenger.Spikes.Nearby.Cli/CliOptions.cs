using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using P2PMessenger.Spikes.Nearby.Net;

namespace P2PMessenger.Spikes.Nearby.Cli;

internal static class CliOptions
{
    public const string Usage = """
        Usage: p2pm-nearby-spike <command> [options]

        Commands:
          selftest                Design checks of caller identification and private announcements, plus the connection flow over loopback TCP. Needs no network.
          bench-caller [counts]   What caller identification costs for 1, 10, 50, 100 and 500 contacts (or the counts given). Use a Release build.
          interfaces              Lists the IPv4 interfaces and the broadcast address of each.
          node                    Runs a probe node. Start one on every device (laptop and phones) at about the same time.

        Options for node:
          --name <name>           Name of this device in the matrix (letters, digits, - _ .); default: the machine name
          --duration <seconds>    How long to run; 0 = until Ctrl+C; default: 90
          --iface <text>          Use only interfaces whose name contains the text, or whose address equals it; repeatable
          --peer <ip>             Also probe and dial this address (when nothing finds the peer); repeatable
          --tcp-port <port>       TCP port; default: 47800
          --udp-port <port>       UDP port for probes; default: 47801
          --group <ip>            Multicast group; default: 239.255.83.5
          --interval <ms>         Time between probe rounds; default: 1000
          --sweep-max-hosts <n>   Sweep an interface with unicast probes only if it has at most n addresses; default: 510
          --pings <n>             Round trips per TCP test; use 100 for a look at the 100-message target of NFR-01; default: 20
          --mdns                  Also advertise and browse _p2pms05._tcp with a small mDNS implementation
        """;

    public static bool TryParseNode(string[] args, [NotNullWhen(true)] out NodeOptions? options, out bool useMdns, out string error)
    {
        options = null;
        useMdns = false;
        error = string.Empty;
        NodeOptions result = new() { ResultsFolder = null };
        List<string> filters = [];
        List<IPAddress> peers = [];
        for (int index = 1; index < args.Length; index++)
        {
            string option = args[index];
            if (option == "--mdns")
            {
                useMdns = true;
                continue;
            }

            if (index + 1 >= args.Length)
            {
                error = $"{option} needs a value.";
                return false;
            }

            string value = args[++index];
            switch (option)
            {
                case "--name":
                    result = result with { Name = ProbeMessage.SanitizeName(value) };
                    break;
                case "--duration" when TryNumber(value, out int seconds):
                    result = result with { Duration = TimeSpan.FromSeconds(seconds) };
                    break;
                case "--iface":
                    filters.Add(value);
                    break;
                case "--peer" when IPAddress.TryParse(value, out IPAddress? peer):
                    peers.Add(peer);
                    break;
                case "--tcp-port" when TryNumber(value, out int tcp) && tcp is > 0 and <= ushort.MaxValue:
                    result = result with { TcpPort = tcp };
                    break;
                case "--udp-port" when TryNumber(value, out int udp) && udp is > 0 and <= ushort.MaxValue:
                    result = result with { UdpPort = udp };
                    break;
                case "--group" when IPAddress.TryParse(value, out IPAddress? group):
                    result = result with { MulticastGroup = group };
                    break;
                case "--interval" when TryNumber(value, out int milliseconds) && milliseconds >= 100:
                    result = result with { Interval = TimeSpan.FromMilliseconds(milliseconds) };
                    break;
                case "--sweep-max-hosts" when TryNumber(value, out int hosts):
                    result = result with { SweepMaxHosts = hosts };
                    break;
                case "--pings" when TryNumber(value, out int pings) && pings is > 0 and <= 1000:
                    result = result with { Pings = pings };
                    break;
                default:
                    error = $"Unknown option or bad value: {option} {value}";
                    return false;
            }
        }

        options = result with { InterfaceFilters = filters, ManualPeers = peers, UseMdns = useMdns };
        return true;
    }

    private static bool TryNumber(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= 1_000_000;
}
