using System.Diagnostics;
using System.Globalization;
using System.Net;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Internet;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Tests.Shared;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Application.Tests.Internet;

[Trait("Category", "Network")]
[Trait("Story", "US-09")]
public sealed class RealTorNetworkTests : IDisposable
{
    private static readonly TimeSpan StepLimit = TimeSpan.FromMinutes(4);

    private readonly string folder = Directory.CreateTempSubdirectory("p2pm-net-").FullName;
    private readonly LoopbackDiscovery hub = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact(Timeout = 900_000)]
    [Trait("NFR", "NFR-02")]
    public async Task TwoApps_OverTheTorNetwork_AddEachOtherAndExchangeAMessageEachWay()
    {
        var executable = Environment.GetEnvironmentVariable("P2PM_TOR");
        Assert.SkipUnless(
            executable is { Length: > 0 } && File.Exists(executable) && Environment.GetEnvironmentVariable("P2PM_TOR_NETWORK") == "1",
            "Set P2PM_TOR to a tor executable and P2PM_TOR_NETWORK to 1, on a machine with internet, to run this test.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var watch = Stopwatch.StartNew();
        var aliceNode = OutOfRange();
        await using var alice = await StartAsync("Alice", aliceNode, executable!, "a", cancellationToken);
        await using var bob = await StartAsync("Bob", OutOfRange(), executable!, "b", cancellationToken);
        var aliceKey = alice.Identity.Keys.IdentityKey;
        var bobKey = bob.Identity.Keys.IdentityKey;

        await WaitAsync(() => Published(alice) && Published(bob), "Both apps connected to Tor and published their onion services", watch, cancellationToken);
        Assert.True((await alice.Contacts.AddAsync(alice.Contacts.Check(bob.Identity.CodeText), cancellationToken)).IsSaved);
        Assert.True((await bob.Contacts.AddAsync(bob.Contacts.Check(alice.Identity.CodeText), cancellationToken)).IsSaved);
        watch.Restart();
        await WaitAsync(
            () => alice.Connections.ReachabilityOf(bobKey) == Reachability.Tor && bob.Connections.ReachabilityOf(aliceKey) == Reachability.Tor
                && alice.Contacts.All.Single().IsAdded && bob.Contacts.All.Single().IsAdded,
            "Both contacts added and reachable over Tor after the codes were added",
            watch,
            cancellationToken);
        watch.Restart();
        await alice.SendAsync(bobKey, "From Alice over Tor", cancellationToken);
        await bob.SendAsync(aliceKey, "From Bob over Tor", cancellationToken);
        await WaitAsync(
            () => Received(bob, aliceKey, "From Alice over Tor") && Received(alice, bobKey, "From Bob over Tor")
                && alice.Chats.MessagesWith(bobKey).Single(static message => message.Direction == MessageDirection.Outgoing).Delivery == DeliveryState.Received
                && bob.Chats.MessagesWith(aliceKey).Single(static message => message.Direction == MessageDirection.Outgoing).Delivery == DeliveryState.Received,
            "A message each way received and acknowledged",
            watch,
            cancellationToken);

        watch.Restart();
        await WaitAsync(
            () => alice.Connections.TorStatusOf(bobKey) == TorDialStatus.None && bob.Connections.TorStatusOf(aliceKey) == TorDialStatus.None
                && alice.Connections.ReachabilityOf(bobKey) == Reachability.Tor && bob.Connections.ReachabilityOf(aliceKey) == Reachability.Tor,
            "Both apps' own Tor attempts finished and one session kept",
            watch,
            cancellationToken);

        var proxyBefore = alice.Connections.TorProxy;
        aliceNode.Network = NetworkSnapshot.Create([], hasOtherNetwork: false, ["rmnet_data0"]);
        watch.Restart();
        await WaitAsync(
            () => alice.Connections.TorProxy is { } proxy && !proxy.Equals(proxyBefore),
            "Alice's network changed from Wi-Fi to mobile data and her tor rebuilt its connections",
            watch,
            cancellationToken);
        watch.Restart();
        await alice.SendAsync(bobKey, "From Alice after the switch", cancellationToken);
        await bob.SendAsync(aliceKey, "From Bob after the switch", cancellationToken);
        await WaitAsync(
            () => Received(bob, aliceKey, "From Alice after the switch") && Received(alice, bobKey, "From Bob after the switch")
                && alice.Chats.MessagesWith(bobKey).All(static message => message.Direction == MessageDirection.Incoming || message.Delivery == DeliveryState.Received)
                && bob.Chats.MessagesWith(aliceKey).All(static message => message.Direction == MessageDirection.Incoming || message.Delivery == DeliveryState.Received)
                && alice.Connections.ReachabilityOf(bobKey) == Reachability.Tor && bob.Connections.ReachabilityOf(aliceKey) == Reachability.Tor,
            "After the switch, a message each way received and acknowledged over Tor",
            watch,
            cancellationToken);

        alice.Identity.Dispose();
        bob.Identity.Dispose();
    }

    private static bool Published(Messenger messenger) =>
        messenger.InternetRoute.Status is { IsAvailable: true, OnionServicePublished: true } && messenger.Connections.UsesTor;

    private static bool Received(Messenger messenger, Ed25519PublicKey contactKey, string text) =>
        messenger.Chats.MessagesWith(contactKey).Any(message => message.Direction == MessageDirection.Incoming && message.Text == text);

    private static async Task WaitAsync(Func<bool> condition, string what, Stopwatch watch, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            Assert.True(watch.Elapsed < StepLimit, $"{what}: not within {StepLimit.TotalMinutes} minutes.");
            await Task.Delay(100, cancellationToken);
        }

        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{what}: {watch.Elapsed.TotalSeconds:F1} s."));
    }

    private LoopbackDiscovery.Node OutOfRange()
    {
        var node = hub.CreateNode();
        node.InRange = false;
        return node;
    }

    private Task<Messenger> StartAsync(string name, LoopbackDiscovery.Node node, string executable, string subfolder, CancellationToken cancellationToken) =>
        Messenger.StartAsync(
            TestKeys.NewIdentity(name),
            MessengerSetup.InMemory() with
            {
                ListenAddress = IPAddress.Loopback,
                ListenPort = 0,
                Discovery = node.Setup,
                Tor = TorSetup.For(Path.GetFullPath(executable), Path.Combine(folder, subfolder, "tor")),
            },
            cancellationToken);
}
