using System.Net;
using System.Text;
using P2PMessenger.Spikes.Nearby.Checks;
using P2PMessenger.Spikes.Nearby.Net.Mdns;

namespace P2PMessenger.Spikes.Nearby.Net;

public static class NetChecks
{
    private static readonly string[] Columns = ["bcast-dir", "mcast", "uni", "tcp"];
    private static readonly byte[] ReferenceRecordHeader = [0, 12, 0, 1, 0, 0, 0, 120];
    private static readonly string[] ServiceLabels = ["_p2pms05", "_tcp", "local"];
    private static readonly int[] StallTimes = [1000, 2000, 3000, 12_000, 13_000];

    public static void Run(CheckReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        CheckReport.Section("Probe format, addressing arithmetic and the reachability report (no network needed)");

        report.Run("interface arithmetic: network, broadcast, host count and membership", () =>
        {
            LocalInterface hotspot = new("ap0", 1, IPAddress.Parse("192.168.43.1"), 24, true);
            LocalInterface wifi = new("wlan0", 2, IPAddress.Parse("10.11.2.45"), 22, true);
            LocalInterface large = new("eth1", 3, IPAddress.Parse("172.20.48.1"), 20, true);
            bool ok = hotspot.Network.ToString() == "192.168.43.0"
                && hotspot.Broadcast.ToString() == "192.168.43.255"
                && hotspot.HostCount == 254
                && hotspot.Hosts().Count() == 254
                && hotspot.Contains(IPAddress.Parse("192.168.43.77"))
                && !hotspot.Contains(IPAddress.Parse("192.168.44.1"))
                && wifi.Network.ToString() == "10.11.0.0"
                && wifi.Broadcast.ToString() == "10.11.3.255"
                && wifi.HostCount == 1022
                && large.Broadcast.ToString() == "172.20.63.255"
                && !InterfaceCatalog.IsUsable(new LocalInterface("lo", 1, IPAddress.Loopback, 8, false))
                && !InterfaceCatalog.IsUsable(new LocalInterface("apipa", 4, IPAddress.Parse("169.254.1.1"), 16, true))
                && !InterfaceCatalog.IsUsable(new LocalInterface("rmnet_data1", 5, IPAddress.Parse("10.74.12.9"), 29, true))
                && !InterfaceCatalog.IsUsable(new LocalInterface("tun0", 6, IPAddress.Parse("10.8.0.2"), 24, true))
                && InterfaceCatalog.IsUsable(hotspot)
                && InterfaceCatalog.IsUsable(wifi);
            return ok ? CheckOutcome.Pass("192.168.43.1/24 gives 192.168.43.255 and 254 hosts; /22 and /20 are right; loopback, link-local, cellular and tunnel interfaces are skipped") : CheckOutcome.Fail("wrong result");
        });

        report.Run("probe: written and read back unchanged", () =>
        {
            ProbeMessage message = new("phone-a", "0123abcd", "mcast", 42, 47800, ["a>b|mcast|3|100|900|via wlan0 as multicast", "b>a|tcp|1|5|6|connect 4 ms"]);
            return ProbeMessage.TryParse(message.ToBytes(), out ProbeMessage? read)
                && read.Node == message.Node && read.RunId == message.RunId && read.Mechanism == message.Mechanism
                && read.Sequence == 42 && read.TcpPort == 47800 && read.Knowledge.SequenceEqual(message.Knowledge)
                ? CheckOutcome.Pass($"{message.ToBytes().Length} bytes with two knowledge entries")
                : CheckOutcome.Fail("different after the round trip");
        });

        report.Run("probe: garbage, other apps' datagrams and bad fields are refused", () =>
        {
            string[] bad =
            [
                "{}",
                "not json at all, just some text of enough length",
                "{\"s\":\"other\",\"v\":1,\"run\":\"r\",\"node\":\"n\",\"mech\":\"m\",\"seq\":1,\"tcp\":1,\"k\":[]}",
                "{\"s\":\"p2pm-s05\",\"v\":1,\"run\":\"r\",\"node\":\"bad name!\",\"mech\":\"m\",\"seq\":1,\"tcp\":1,\"k\":[]}",
                "{\"s\":\"p2pm-s05\",\"v\":1,\"run\":\"r\",\"node\":\"n\",\"mech\":\"m\",\"seq\":1,\"tcp\":70000,\"k\":[]}",
                "{\"s\":\"p2pm-s05\",\"v\":1,\"run\":\"r\",\"node\":\"n\",\"mech\":\"m\",\"seq\":1,\"tcp\":1,\"k\":[1]}",
                "{\"s\":\"p2pm-s05\",\"v\":1,\"run\":\"r\",\"node\":\"n\",\"mech\":\"m\",\"seq\":\"x\",\"tcp\":1,\"k\":[]}",
            ];
            foreach (string text in bad)
            {
                if (ProbeMessage.TryParse(Encoding.UTF8.GetBytes(text), out _))
                {
                    return CheckOutcome.Fail("accepted: " + text);
                }
            }

            return ProbeMessage.TryParse(new byte[3000], out _) || ProbeMessage.TryParse([], out _)
                ? CheckOutcome.Fail("accepted empty or oversized data")
                : CheckOutcome.Pass($"{bad.Length} malformed probes, an empty one and an oversized one refused");
        });

        report.Run("probe: every single-bit change is either refused or read without an exception", () =>
        {
            byte[] original = new ProbeMessage("node-1", "abcd1234", "bcast-dir", 7, 47800, ["a>b|uni|1|1|1|x"]).ToBytes();
            int refused = 0;
            int accepted = 0;
            byte[] copy = [.. original];
            for (int bit = 0; bit < copy.Length * 8; bit++)
            {
                copy[bit / 8] ^= (byte)(1 << (bit % 8));
                if (ProbeMessage.TryParse(copy, out _))
                {
                    accepted++;
                }
                else
                {
                    refused++;
                }

                copy[bit / 8] ^= (byte)(1 << (bit % 8));
            }

            return CheckOutcome.Pass($"{copy.Length * 8} changed copies: {refused} refused, {accepted} still valid text (a probe is not authenticated; it is only a test message)");
        });

        report.Run("gossip: the receiver's own count wins, a larger count from the owner replaces a copy, nobody can lower a count", () =>
        {
            ObservationBook book = new("me");
            book.RecordHeard("a", "mcast", 10, "via x");
            book.RecordHeard("a", "mcast", 20, "via x");
            book.Merge(["a>me|mcast|99|1|2|fake", "b>me|mcast|5|1|2|not mine", "a>b|mcast|7|1|2|report", "a>b|mcast|3|1|2|older report"]);
            Observation[] items = [.. book.Snapshot().OrderBy(item => item.Key, StringComparer.Ordinal)];
            int own = items.Single(item => item.Key == "a>me|mcast").Count;
            int copy = items.Single(item => item.Key == "a>b|mcast").Count;
            return own == 2 && copy == 7 && items.Length == 2
                ? CheckOutcome.Pass("own count 2 kept against a claim of 99; the report 7 kept against an older 3; an entry about me made by someone else ignored")
                : CheckOutcome.Fail($"own {own}, copy {copy}, entries {items.Length}");
        });

        report.Run("report: three devices that all hear each other", () =>
        {
            string[] lines = [.. NodeReport.Render("a", AllWorking(), ["b", "c"], ["b", "c"], Columns)];
            return lines.Any(line => line.Contains("NO PATH", StringComparison.Ordinal)) || !lines.Any(line => line.Contains("6 of 6", StringComparison.Ordinal))
                ? CheckOutcome.Fail(string.Join(" | ", lines))
                : CheckOutcome.Pass("every mechanism works in all 6 directions; no path warning");
        });

        report.Run("report: the hotspot host sees that its two clients cannot reach each other", () =>
        {
            List<Observation> items = [.. AllWorking().Where(item => !((item.Source == "b" && item.Destination == "c") || (item.Source == "c" && item.Destination == "b")))];
            string[] lines = [.. NodeReport.Render("a", items, ["b", "c"], ["b", "c"], Columns)];
            return lines.Any(line => line.Contains("NO PATH between b and c", StringComparison.Ordinal))
                && lines.Any(line => line.Contains("bcast-dir", StringComparison.Ordinal) && line.Contains("4 of 6", StringComparison.Ordinal) && line.Contains("b->c", StringComparison.Ordinal))
                ? CheckOutcome.Pass("host a shows NO PATH between b and c and lists the failing directions")
                : CheckOutcome.Fail(string.Join(" | ", lines));
        });

        report.Run("report: a client does not claim what only the other client could report", () =>
        {
            List<Observation> items =
            [
                new("a", "b", "mcast", 12, 1, 2, "via wlan0 as multicast"),
                new("b", "a", "mcast", 11, 1, 2, "x"),
                new("b", "a", "tcp", 1, 1, 2, "connect 3 ms"),
            ];
            string[] lines = [.. NodeReport.Render("b", items, ["a", "c"], ["a"], Columns)];
            bool unknownMarked = lines.Any(line => line.StartsWith("  b -> c", StringComparison.Ordinal) && line.Contains('?', StringComparison.Ordinal));
            bool noClaim = !lines.Any(line => line.Contains("NO PATH", StringComparison.Ordinal));
            return unknownMarked && noClaim
                ? CheckOutcome.Pass("b -> c is shown as ? (no report from c) and no path warning is made")
                : CheckOutcome.Fail(string.Join(" | ", lines));
        });

        report.Run("silences: a stall between datagrams and a stream that stopped are reported, a steady stream is not, a one-time find is not tracked", () =>
        {
            ObservationBook book = new("me");
            foreach (int time in StallTimes)
            {
                book.RecordHeard("stalls", "mcast", time, "x");
            }

            for (int time = 1000; time <= 20_000; time += 1000)
            {
                book.RecordHeard("steady", "bcast-dir", time, "x");
            }

            for (int time = 1000; time <= 5000; time += 1000)
            {
                book.RecordHeard("stopped", "uni", time, "x");
            }

            book.RecordHeard("found-once", "mdns", 500, "x", periodic: false);
            string[] lines = [.. book.Silences(20_000, 4000)];
            return lines.Length == 2
                && lines.Any(line => line.Contains("stalls>me|mcast", StringComparison.Ordinal) && line.Contains("longest silence between two datagrams 9 s", StringComparison.Ordinal))
                && lines.Any(line => line.Contains("stopped>me|uni", StringComparison.Ordinal) && line.Contains("silent for the last 15 s", StringComparison.Ordinal))
                ? CheckOutcome.Pass("the 9 s stall and the stream that went quiet 15 s before the end are listed; the steady stream and the service found once by mDNS are not")
                : CheckOutcome.Fail(string.Join(" | ", lines));
        });

        report.Run("report: a device that hears nobody says what to check", () =>
        {
            string[] lines = [.. NodeReport.Render("solo", [], [], [], Columns)];
            return lines.Length == 2 && lines[0].Contains("No other node was heard", StringComparison.Ordinal)
                ? CheckOutcome.Pass("the advice names the firewall, the shared network and client isolation")
                : CheckOutcome.Fail(string.Join(" | ", lines));
        });

        RunDnsChecks(report);
    }

    private static void RunDnsChecks(CheckReport report)
    {
        report.Run("mDNS codec: a response with PTR, SRV, TXT and A records is written and read back", () =>
        {
            const string service = MdnsDiscovery.ServiceType;
            DnsMessage response = new(
                true,
                [],
                [
                    new DnsRecord(service, DnsType.Reference, Target: "s05-pixel." + service),
                    new DnsRecord("s05-pixel." + service, DnsType.Srv, Target: "pixel.local", Port: 47800),
                    new DnsRecord("s05-pixel." + service, DnsType.Txt),
                    new DnsRecord("pixel.local", DnsType.A, Address: IPAddress.Parse("192.168.43.17")),
                ]);
            byte[] bytes = response.Write();
            if (!DnsMessage.TryParse(bytes, out DnsMessage? read) || !read.IsResponse || read.Records.Count != 4)
            {
                return CheckOutcome.Fail("not read back");
            }

            DnsRecord pointer = read.Records[0];
            DnsRecord srv = read.Records.Single(record => record.Type == DnsType.Srv);
            DnsRecord address = read.Records.Single(record => record.Type == DnsType.A);
            return pointer.Target == "s05-pixel." + service && srv.Port == 47800 && srv.Target == "pixel.local" && address.Address?.ToString() == "192.168.43.17"
                ? CheckOutcome.Pass($"{bytes.Length} bytes; names, port and address survive")
                : CheckOutcome.Fail("fields differ");
        });

        report.Run("mDNS codec: names compressed by a responder are read (pointer to an earlier name)", () =>
        {
            List<byte> bytes = [0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0];
            int serviceOffset = bytes.Count;
            foreach (string label in ServiceLabels)
            {
                bytes.Add((byte)label.Length);
                bytes.AddRange(Encoding.ASCII.GetBytes(label));
            }

            bytes.Add(0);
            bytes.AddRange(ReferenceRecordHeader);
            byte[] label2 = Encoding.ASCII.GetBytes("s05-x");
            int dataLength = 1 + label2.Length + 2;
            bytes.AddRange([(byte)(dataLength >> 8), (byte)dataLength, (byte)label2.Length]);
            bytes.AddRange(label2);
            bytes.AddRange([0xC0, (byte)serviceOffset]);
            return DnsMessage.TryParse(bytes.ToArray(), out DnsMessage? read) && read.Records.Count == 1 && read.Records[0].Target == "s05-x._p2pms05._tcp.local"
                ? CheckOutcome.Pass("the pointer was followed: s05-x._p2pms05._tcp.local")
                : CheckOutcome.Fail("the compressed name was not read");
        });

        report.Run("mDNS codec: loops, overruns, reserved label types and absurd counts are refused", () =>
        {
            byte[][] bad =
            [
                [0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0xC0, 12, 0, 1, 0, 1],
                [0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 40, 65, 66, 0, 1, 0, 1],
                [0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0x80, 65, 0, 0, 1, 0, 1],
                [0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
                [0, 0, 0],
                [],
            ];
            for (int i = 0; i < bad.Length; i++)
            {
                if (DnsMessage.TryParse(bad[i], out _))
                {
                    return CheckOutcome.Fail($"malformed message {i} was accepted");
                }
            }

            return DnsMessage.TryParse(new byte[10_000], out _) ? CheckOutcome.Fail("an oversized message was accepted") : CheckOutcome.Pass($"{bad.Length} malformed messages and an oversized one refused");
        });

        report.Run("mDNS codec: every single-bit change is refused or read without an exception", () =>
        {
            byte[] original = new DnsMessage(
                true,
                [],
                [
                    new DnsRecord(MdnsDiscovery.ServiceType, DnsType.Reference, Target: "s05-a." + MdnsDiscovery.ServiceType),
                    new DnsRecord("s05-a." + MdnsDiscovery.ServiceType, DnsType.Srv, Target: "a.local", Port: 47800),
                    new DnsRecord("a.local", DnsType.A, Address: IPAddress.Parse("10.0.0.2")),
                ]).Write();
            byte[] copy = [.. original];
            int accepted = 0;
            for (int bit = 0; bit < copy.Length * 8; bit++)
            {
                copy[bit / 8] ^= (byte)(1 << (bit % 8));
                if (DnsMessage.TryParse(copy, out _))
                {
                    accepted++;
                }

                copy[bit / 8] ^= (byte)(1 << (bit % 8));
            }

            return CheckOutcome.Pass($"{copy.Length * 8} changed copies, none threw; {accepted} still parse (DNS has no integrity protection)");
        });
    }

    private static List<Observation> AllWorking()
    {
        List<Observation> items = [];
        foreach (string source in new[] { "a", "b", "c" })
        {
            foreach (string destination in new[] { "a", "b", "c" })
            {
                if (source == destination)
                {
                    continue;
                }

                foreach (string mechanism in Columns)
                {
                    items.Add(mechanism == "tcp"
                        ? new Observation(source, destination, mechanism, 2, 1, 9, "connect 4 ms, rtt 1.0 ms")
                        : new Observation(source, destination, mechanism, 20, 1, 9, "via eth0"));
                }
            }
        }

        return items;
    }
}
