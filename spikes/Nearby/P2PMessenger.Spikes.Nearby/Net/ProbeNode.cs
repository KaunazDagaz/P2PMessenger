using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Spikes.Nearby.Net;

public sealed class ProbeNode : IDisposable
{
    private const int GossipEntries = 12;
    private const int ReportSeconds = 15;

    private static readonly string[] BaseColumns = ["bcast-dir", "bcast-lim", "mcast", "uni", "tcp"];

    private readonly NodeOptions _options;
    private readonly IPlatformHooks _hooks;
    private readonly ObservationBook _book;
    private readonly PeerBook _peers = new();
    private readonly UdpProbeEngine _udp;
    private readonly TcpProbeEngine _tcp;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly string _runId = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
    private readonly ConcurrentDictionary<string, TcpState> _tcpStates = [];
    private readonly ConcurrentDictionary<string, long> _heardFrom = [];
    private readonly ConcurrentDictionary<string, long> _gossipFrom = [];
    private readonly ConcurrentDictionary<string, byte> _logged = [];
    private readonly ConcurrentDictionary<string, Task> _sweeps = [];
    private readonly List<string> _columns = [.. BaseColumns];
    private IReadOnlyList<LocalInterface> _interfaces = [];
    private IReadOnlyList<LocalInterface> _allInterfaces = [];
    private int _sequence;
    private int _sameNameWarned;

    public ProbeNode(NodeOptions options, IPlatformHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(hooks);
        _options = options with { Name = ProbeMessage.SanitizeName(options.Name) };
        _hooks = hooks;
        _book = new ObservationBook(_options.Name);
        _udp = new UdpProbeEngine(_options, Log);
        _tcp = new TcpProbeEngine(_options, OnInboundPeer, Log);
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        PrintStart();
        RefreshInterfaces(first: true);
        foreach (IPAddress manual in _options.ManualPeers)
        {
            _peers.Learn("?" + manual, manual, _options.TcpPort, "manual");
        }

        using IDisposable? multicastLock = _hooks.AcquireMulticastLock();
        if (multicastLock is not null)
        {
            Log("MulticastLock acquired.");
        }

        try
        {
            _udp.OpenReceiver(_interfaces);
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException)
        {
            Log($"The UDP port {_options.UdpPort} cannot be opened: {exception.Message}. Is another node running on this machine?");
            return 1;
        }

        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.Duration > TimeSpan.Zero)
        {
            stop.CancelAfter(_options.Duration);
        }

        List<Task> tasks =
        [
            _udp.ReceiveLoopAsync(OnProbeAsync, stop.Token),
            _tcp.ServeAsync(stop.Token),
            SendLoopAsync(stop.Token),
            TestLoopAsync(stop.Token),
            ReportLoopAsync(stop.Token),
        ];

        IExtraDiscovery? extra = _hooks.CreateExtraDiscovery();
        if (extra is not null)
        {
            _columns.Insert(_columns.Count - 1, extra.Mechanism);
            tasks.Add(RunExtraAsync(extra, stop.Token));
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log("Stopped.");
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or IOException or InvalidOperationException)
        {
            Log($"The node stopped with an error: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            await Task.WhenAll(_sweeps.Values).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or IOException or InvalidOperationException)
        {
            Log($"A sweep stopped with an error: {exception.GetType().Name}: {exception.Message}");
        }

        PrintReport("final result");
        PrintDetails();
        await SaveResultsAsync().ConfigureAwait(false);
        return 0;
    }

    public void Dispose() => _udp.Dispose();

    private static bool SameAddresses(IReadOnlyList<LocalInterface> first, IReadOnlyList<LocalInterface> second) =>
        first.Count == second.Count && first.Zip(second).All(pair => pair.First.Address.Equals(pair.Second.Address) && pair.First.Name == pair.Second.Name);

    private int ElapsedMs() => (int)_clock.ElapsedMilliseconds;

    private string Elapsed() => _clock.Elapsed.ToString("mm\\:ss\\.f", CultureInfo.InvariantCulture);

    private void Log(string line) => Console.WriteLine($"[{Elapsed()}] {line}");

    private void PrintStart()
    {
        Console.WriteLine($"Node {_options.Name}, run {_runId}: UDP port {_options.UdpPort}, TCP port {_options.TcpPort}, multicast group {_options.MulticastGroup}");
        Console.WriteLine($"Local time {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} (UTC {DateTimeOffset.UtcNow:HH:mm:ss}); {RuntimeInformation.OSDescription}");
        Console.WriteLine(_options.Duration > TimeSpan.Zero
            ? $"The node runs for {_options.Duration.TotalSeconds:F0} s. Start the same on the other devices now; a probe goes out every {_options.Interval.TotalSeconds:F1} s per mechanism."
            : "The node runs until stopped.");
        Console.WriteLine("If Windows asks to let the app through its firewall, allow it on private AND public networks (a hotspot counts as public); on Linux, allow UDP and TCP on these ports.");
    }

    private void RefreshInterfaces(bool first)
    {
        IReadOnlyList<LocalInterface> all;
        try
        {
            all = _hooks.EnumerateInterfaces() ?? InterfaceCatalog.Enumerate();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log($"Listing the network interfaces failed: {exception.GetType().Name}: {exception.Message}");
            all = [];
        }

        IReadOnlyList<LocalInterface> selected = InterfaceCatalog.Select(all, _options.InterfaceFilters);
        bool changed = !SameAddresses(selected, _interfaces);
        _allInterfaces = all;
        _interfaces = selected;
        if (first || changed)
        {
            PrintInterfaces(all, selected);
        }

        if (changed)
        {
            _udp.JoinNew(selected);
        }
    }

    private void PrintInterfaces(IReadOnlyList<LocalInterface> all, IReadOnlyList<LocalInterface> selected)
    {
        Log(selected.Count == 0 ? "No usable IPv4 interface: join a Wi-Fi network or start the hotspot." : "Interfaces in use:");
        foreach (LocalInterface item in selected)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {item.Name}  {item.Address}/{item.PrefixLength}  broadcast {item.Broadcast}  multicast {(item.SupportsMulticast ? "yes" : "no")}  hosts {item.HostCount}"));
        }

        List<string> ignored = [.. all.Where(item => !selected.Contains(item)).Select(item => $"{item.Name} {item.Address}/{item.PrefixLength}")];
        if (ignored.Count > 0)
        {
            Console.WriteLine("  not used (loopback, link-local, very small or large prefix, cellular or tunnel by name, or filtered out): " + string.Join(", ", ignored));
        }
    }

    private string InterfaceName(int index)
    {
        if (index == 0)
        {
            return "?";
        }

        LocalInterface? item = _allInterfaces.FirstOrDefault(candidate => candidate.Index == index);
        return item?.Name ?? "if" + index.ToString(CultureInfo.InvariantCulture);
    }

    private byte[] BuildPayload(string mechanism)
    {
        int take = GossipEntries;
        while (true)
        {
            ProbeMessage message = new(_options.Name, _runId, mechanism, Interlocked.Increment(ref _sequence), _options.TcpPort, _book.NextGossip(take));
            byte[] bytes = message.ToBytes();
            if (bytes.Length <= ProbeMessage.MaxLength || take <= 1)
            {
                return bytes;
            }

            take /= 2;
        }
    }

    private async Task SendProbeAsync(LocalInterface item, string mechanism, IPAddress destination, CancellationToken cancellationToken)
    {
        await _udp.SendAsync(item, mechanism, destination, BuildPayload(mechanism), cancellationToken).ConfigureAwait(false);
    }

    private void StartSweep(LocalInterface item, CancellationToken cancellationToken)
    {
        if (_sweeps.TryGetValue(item.Name, out Task? running) && !running.IsCompleted)
        {
            return;
        }

        _sweeps[item.Name] = Task.Run(() => SweepAsync(item, cancellationToken), CancellationToken.None);
    }

    private async Task SweepAsync(LocalInterface item, CancellationToken cancellationToken)
    {
        if (item.HostCount > _options.SweepMaxHosts)
        {
            if (_logged.TryAdd("sweep-skipped|" + item.Name, 0))
            {
                Log($"Sweep skipped on {item.Name}: {item.HostCount} addresses are too many for a polite sweep (--sweep-max-hosts {_options.SweepMaxHosts}).");
            }

            return;
        }

        byte[] payload = BuildPayload("uni");
        HashSet<IPAddress> known = [.. _peers.Endpoints().Select(peer => peer.Address)];
        long started = Stopwatch.GetTimestamp();
        int count = 0;
        try
        {
            foreach (IPAddress host in item.Hosts())
            {
                if (!host.Equals(item.Address) && !known.Contains(host))
                {
                    await _udp.SendAsync(item, "uni", host, payload, cancellationToken, bulk: true).ConfigureAwait(false);
                    count++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"Sweep on {item.Name}: {count} unicast probes took {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms, in the background."));
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        long round = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (round > 0 && round % 10 == 0)
                {
                    RefreshInterfaces(first: false);
                }

                foreach (LocalInterface item in _interfaces)
                {
                    await SendProbeAsync(item, "bcast-dir", item.Broadcast, cancellationToken).ConfigureAwait(false);
                    await SendProbeAsync(item, "bcast-lim", IPAddress.Broadcast, cancellationToken).ConfigureAwait(false);
                    await SendProbeAsync(item, "mcast", _options.MulticastGroup, cancellationToken).ConfigureAwait(false);
                    foreach (PeerEndpoint peer in _peers.Endpoints())
                    {
                        bool belongs = item.Contains(peer.Address) || (!_interfaces.Any(other => other.Contains(peer.Address)) && item == _interfaces[0]);
                        if (belongs)
                        {
                            await SendProbeAsync(item, "uni", peer.Address, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    if (round == 1 || (round >= 5 && round % 15 == 5))
                    {
                        StartSweep(item, cancellationToken);
                    }
                }

                round++;
                await Task.Delay(_options.Interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private Task OnProbeAsync(ReceivedProbe probe)
    {
        ProbeMessage message = probe.Message;
        if (message.Node == _options.Name)
        {
            if (message.RunId != _runId && Interlocked.Exchange(ref _sameNameWarned, 1) == 0)
            {
                Log("Another node uses the same name; give every device its own name with --name.");
            }

            return Task.CompletedTask;
        }

        string detail = $"via {InterfaceName(probe.InterfaceIndex)} as {probe.ArrivalKind}";
        _heardFrom.AddOrUpdate(message.Node, 1, (_, count) => count + 1);
        _gossipFrom.AddOrUpdate(message.Node, 1, (_, count) => count + 1);
        _book.RecordHeard(message.Node, message.Mechanism, ElapsedMs(), detail);
        _book.Merge(message.Knowledge);
        AdoptPeer(message.Node, probe.Remote.Address, message.TcpPort, message.Mechanism);
        if (_logged.TryAdd(message.Node + "|" + message.Mechanism, 0))
        {
            Log($"heard {message.Node} at {probe.Remote.Address} via {message.Mechanism} ({detail})");
        }

        return Task.CompletedTask;
    }

    private void OnInboundPeer(string name, IPAddress address)
    {
        _heardFrom.AddOrUpdate(name, 1, (_, count) => count + 1);
        _book.RecordServed(name, ElapsedMs(), "connected to me");
        AdoptPeer(name, address, _options.TcpPort, "tcp");
        if (_logged.TryAdd(name + "|tcp-in", 0))
        {
            Log($"{name} at {address} connected to my TCP port");
        }
    }

    private void OnExtraPeer(string mechanism, string name, IPAddress address, int tcpPort, string detail)
    {
        string safeName = ProbeMessage.SanitizeName(name);
        if (safeName == _options.Name)
        {
            return;
        }

        _heardFrom.AddOrUpdate(safeName, 1, (_, count) => count + 1);
        _book.RecordHeard(safeName, mechanism, ElapsedMs(), detail, periodic: false);
        AdoptPeer(safeName, address, tcpPort, mechanism);
        if (_logged.TryAdd(safeName + "|" + mechanism, 0))
        {
            Log($"{mechanism} found {safeName} at {address}:{tcpPort} ({detail})");
        }
    }

    private void AdoptPeer(string name, IPAddress address, int tcpPort, string via)
    {
        _peers.Rename("?" + address, name, address);
        if (_peers.Learn(name, address, tcpPort, via))
        {
            Log($"new peer {name} at {address}:{tcpPort}, first through {via}");
        }
    }

    private async Task RunExtraAsync(IExtraDiscovery extra, CancellationToken cancellationToken)
    {
        DiscoveryHost host = new(
            _options.Name,
            _options.TcpPort,
            () => _interfaces,
            (name, address, port, detail) => OnExtraPeer(extra.Mechanism, name, address, port, detail),
            Log);
        try
        {
            await extra.RunAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log($"{extra.Mechanism} stopped: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            await extra.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task TestLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (PeerEndpoint peer in _peers.Endpoints())
            {
                string key = peer.Address.ToString();
                TcpState state = _tcpStates.GetOrAdd(key, _ => new TcpState());
                if (peer.TcpPort <= 0 || state.Running || ElapsedMs() < state.NextDueMs)
                {
                    continue;
                }

                state.Running = true;
                _ = Task.Run(() => TestPeerAsync(peer, state, cancellationToken), cancellationToken);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TestPeerAsync(PeerEndpoint peer, TcpState state, CancellationToken cancellationToken)
    {
        try
        {
            TcpTestResult result = await _tcp.TestAsync(peer.Address, peer.TcpPort, cancellationToken).ConfigureAwait(false);
            string peerName = result.PeerName ?? peer.Name;
            if (peer.Name.StartsWith('?') && result.PeerName is not null)
            {
                _peers.Rename(peer.Name, result.PeerName, peer.Address);
            }

            int now = ElapsedMs();
            bool changed = state.LastOk != result.Success;
            state.LastOk = result.Success;
            if (result.Success)
            {
                state.Successes++;
                state.LastDetail = string.Create(CultureInfo.InvariantCulture, $"connect {result.ConnectMs:F0} ms, rtt {result.RttMedianMs:F1} ms (p95 {result.RttP95Ms:F1}, max {result.RttMaxMs:F1}, n={result.Pings})");
            }
            else
            {
                state.Failures++;
                state.LastDetail = result.Error;
            }

            state.FirstMs = Math.Min(state.FirstMs, now);
            string detail = state.Successes > 0
                ? state.LastDetail + (state.Failures > 0 ? $"; {state.Failures} failed" : string.Empty)
                : $"{state.LastDetail} ({state.Failures} tries)";
            _book.Set(new Observation(_options.Name, peerName, "tcp", state.Successes, state.FirstMs, now, detail));
            state.NextDueMs = now + (result.Success ? 20_000 : 8_000);
            if (changed)
            {
                Log(result.Success ? $"tcp {_options.Name} -> {peerName} works: {state.LastDetail}" : $"tcp {_options.Name} -> {peerName} FAILED: {result.Error}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            state.Running = false;
        }
    }

    private async Task ReportLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(ReportSeconds), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!_heardFrom.IsEmpty || _peers.Endpoints().Count > 0)
            {
                PrintReport("status");
            }
        }
    }

    private void PrintReport(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {title}: node {_options.Name} after {Elapsed()}");
        Console.WriteLine("Cells: datagrams heard (the receiver counts them); tcp: dialer's result; - = nothing; ? = no report from that device yet.");
        foreach (string line in NodeReport.Render(_options.Name, _book.Snapshot(), [.. _heardFrom.Keys], [.. _gossipFrom.Keys], _columns))
        {
            Console.WriteLine(line);
        }
    }

    private void PrintDetails()
    {
        Console.WriteLine();
        Console.WriteLine("Sent, per interface and mechanism (errors name what the operating system said):");
        foreach ((string key, SendStats stats) in _udp.Stats.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {key,-22} sent {stats.Sent}, errors {stats.Errors}{(stats.LastError is null ? string.Empty : ", last: " + stats.LastError)}");
        }

        Console.WriteLine("Multicast group joins per interface address: " + (_udp.Joins.Count == 0 ? "none" : string.Join("; ", _udp.Joins.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}"))));
        Console.WriteLine("Datagrams from other nodes, by how they arrived: " + (_udp.Arrivals.Count == 0 ? "none" : string.Join(", ", _udp.Arrivals.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}"))) + $"; own probes heard back {_udp.SelfHeard}; unparseable {_udp.Unparseable}");
        if (_udp.PacketInformationUnavailable)
        {
            Console.WriteLine("This platform gave no packet information, so the arrival kinds above are unknown.");
        }

        int thresholdMs = (int)(_options.Interval.TotalMilliseconds * 4);
        IReadOnlyList<string> silences = _book.Silences(ElapsedMs(), thresholdMs);
        Console.WriteLine($"Silences of {thresholdMs / 1000.0:F0} s or more between datagrams from one device and mechanism (a probe goes out every {_options.Interval.TotalSeconds:F1} s; mDNS and TCP are not probes):");
        if (silences.Count == 0)
        {
            Console.WriteLine("  none");
        }
        else
        {
            foreach (string line in silences)
            {
                Console.WriteLine(line);
            }
        }
    }

    private async Task SaveResultsAsync()
    {
        if (string.IsNullOrEmpty(_options.ResultsFolder))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_options.ResultsFolder);
            string path = Path.Combine(_options.ResultsFolder, $"nearby-results-{_options.Name}.csv");
            StringBuilder builder = new();
            if (!File.Exists(path))
            {
                builder.AppendLine("run_utc,node,os,source,destination,mechanism,count,first_ms,last_ms,detail");
            }

            string runUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            foreach (Observation item in _book.Snapshot().OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                builder.AppendLine(string.Join(
                    ',',
                    runUtc,
                    _options.Name,
                    Quote(RuntimeInformation.OSDescription),
                    item.Source,
                    item.Destination,
                    item.Mechanism,
                    item.Count.ToString(CultureInfo.InvariantCulture),
                    item.FirstMs.ToString(CultureInfo.InvariantCulture),
                    item.LastMs.ToString(CultureInfo.InvariantCulture),
                    Quote(item.Detail)));
            }

            await File.AppendAllTextAsync(path, builder.ToString()).ConfigureAwait(false);
            Console.WriteLine($"Results appended to {path}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not save the results: {exception.Message}");
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private sealed class TcpState
    {
        public volatile bool Running;
        public long NextDueMs;
        public int Successes;
        public int Failures;
        public bool? LastOk;
        public string LastDetail = string.Empty;
        public int FirstMs = int.MaxValue;
    }
}
