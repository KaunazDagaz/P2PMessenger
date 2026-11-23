using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using P2PMessenger.Core.Discovery;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Transport.Tests.Discovery;

public sealed class LocalNetworkDiscoveryTests
{
    private const int ListeningPort = 51_234;

    private static readonly LocalInterface Wifi = new("wlan0", 3, IPAddress.Parse("192.168.43.10"), 24, supportsMulticast: true);
    private static readonly LocalInterface Wired = new("eth0", 2, IPAddress.Parse("10.20.0.5"), 29, supportsMulticast: false);
    private static readonly IPAddress Neighbour = IPAddress.Parse("192.168.43.20");

    [Fact]
    [Trait("Story", "US-06")]
    public async Task AnnounceRound_SendsByMulticastAndDirectedBroadcastOnEveryInterfaceToTheDiscoveryPort()
    {
        using var peers = new DiscoveryPeers(2);
        var (discovery, link, _, _) = Create(peers, Wifi, Wired);
        await using var disposing = discovery;
        discovery.SetAnnouncedPort(ListeningPort);

        var sent = await discovery.AnnounceRoundAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, sent);
        Assert.Equal(
            [
                ("eth0", new IPEndPoint(IPAddress.Parse("10.20.0.7"), 45821)),
                ("wlan0", new IPEndPoint(IPAddress.Parse("239.255.83.5"), 45821)),
                ("wlan0", new IPEndPoint(IPAddress.Parse("192.168.43.255"), 45821)),
            ],
            link.Sent.Select(static datagram => (datagram.From.Name, datagram.Destination)));
        Assert.All(link.Sent, static datagram => Assert.False(datagram.Sweep));
        Assert.Single(link.Sent.Select(static datagram => Convert.ToHexString(datagram.Datagram)).Distinct());
        foreach (var contact in peers.OwnForEachContact)
        {
            Assert.True(Announcement.TryRecognize(link.Sent[0].Datagram, [contact], out _, out var port));
            Assert.Equal(ListeningPort, port);
        }
    }

    [Fact]
    [Trait("Story", "US-06")]
    public async Task AnnounceRound_With70Contacts_SendsThreeAnnouncementsPerMechanism()
    {
        using var peers = new DiscoveryPeers(70);
        var (discovery, link, _, _) = Create(peers, Wifi);
        await using var disposing = discovery;
        discovery.SetAnnouncedPort(ListeningPort);

        await discovery.AnnounceRoundAsync(TestContext.Current.CancellationToken);

        Assert.Equal(6, link.Sent.Count);
        var distinct = link.Sent.Select(static datagram => Convert.ToHexString(datagram.Datagram)).Distinct().Count();
        Assert.Equal(3, distinct);
        foreach (var contact in peers.OwnForEachContact)
        {
            Assert.Equal(2, link.Sent.Count(datagram => Announcement.TryRecognize(datagram.Datagram, [contact], out _, out _)));
        }
    }

    [Fact]
    public async Task AnnounceRound_WithoutAPortContactsOrInterfaces_SendsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var peers = new DiscoveryPeers(1);
        using var lonely = new DiscoveryPeers(0);

        var (withoutPort, linkWithoutPort, _, _) = Create(peers, Wifi);
        await using (withoutPort)
        {
            Assert.Equal(0, await withoutPort.AnnounceRoundAsync(cancellationToken));
            Assert.Empty(linkWithoutPort.Sent);
        }

        var (withoutContacts, linkWithoutContacts, _, _) = Create(lonely, Wifi);
        await using (withoutContacts)
        {
            withoutContacts.SetAnnouncedPort(ListeningPort);
            Assert.Equal(0, await withoutContacts.AnnounceRoundAsync(cancellationToken));
            Assert.Empty(linkWithoutContacts.Sent);
        }

        var (withoutInterfaces, linkWithoutInterfaces, _, _) = Create(peers);
        await using (withoutInterfaces)
        {
            withoutInterfaces.SetAnnouncedPort(ListeningPort);
            Assert.Equal(0, await withoutInterfaces.AnnounceRoundAsync(cancellationToken));
            Assert.Empty(linkWithoutInterfaces.Sent);
        }
    }

    [Fact]
    public async Task AnnounceRound_SendFails_GoesOnAndReportsTheErrorOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var peers = new DiscoveryPeers(1);
        var logger = new ListLogger<LocalNetworkDiscovery>();
        var (discovery, link, _, _) = Create(peers, logger, Wifi);
        await using var disposing = discovery;
        discovery.SetAnnouncedPort(ListeningPort);
        link.SendResult = SocketError.NetworkUnreachable;

        Assert.Equal(0, await discovery.AnnounceRoundAsync(cancellationToken));
        Assert.Equal(0, await discovery.AnnounceRoundAsync(cancellationToken));

        Assert.Equal(4, link.Sent.Count);
        Assert.Single(logger.Entries, static entry => entry.Id.Name == "AnnouncementNotSent");
    }

    [Fact]
    [Trait("Story", "US-06")]
    public async Task OnDatagram_AnnouncementOfAContact_IsHeardAtItsSourceAndAnnouncedPort()
    {
        using var peers = new DiscoveryPeers(3);
        var (discovery, _, observer, _) = Create(peers, Wifi);
        await using var disposing = discovery;
        var announcement = Announcement.Build([peers.OwnForEachContact[1]], 50_001);

        Assert.True(discovery.OnDatagram(announcement, Neighbour));

        var (contact, endpoint) = Assert.Single(observer.HeardContacts);
        Assert.Same(peers.ContactsOfOwn[1], contact);
        Assert.Equal(new IPEndPoint(Neighbour, 50_001), endpoint);
    }

    [Fact]
    [Trait("Story", "US-06")]
    public async Task OnDatagram_OwnAnnouncementHeardBack_IsIgnoredWithoutBeingScreened()
    {
        using var peers = new DiscoveryPeers(1);
        var options = new DiscoveryOptions { ScreeningsPerSecond = 0.001, ScreeningBurst = 1 };
        var (discovery, link, observer, _) = Create(peers, options, new ManualTimeProvider(), NullLogger<LocalNetworkDiscovery>.Instance, Wifi);
        await using var disposing = discovery;
        discovery.SetAnnouncedPort(ListeningPort);
        await discovery.AnnounceRoundAsync(TestContext.Current.CancellationToken);

        Assert.False(discovery.OnDatagram(link.Sent[0].Datagram, Wifi.Address));
        Assert.False(discovery.OnDatagram(link.Sent[1].Datagram, Wifi.Address));

        Assert.Empty(observer.HeardContacts);
        Assert.True(discovery.OnDatagram(Announcement.Build([peers.OwnForEachContact[0]], 50_001), Neighbour));
    }

    [Fact]
    public async Task OnDatagram_SameAnnouncementByMulticastAndBroadcast_IsScreenedOnce()
    {
        using var peers = new DiscoveryPeers(1);
        var (discovery, _, observer, _) = Create(peers, Wifi);
        await using var disposing = discovery;
        var announcement = Announcement.Build([peers.OwnForEachContact[0]], 50_001);

        Assert.True(discovery.OnDatagram(announcement, Neighbour));
        Assert.False(discovery.OnDatagram(announcement, Neighbour));

        Assert.Single(observer.HeardContacts);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("239.255.83.5")]
    [InlineData("::1")]
    public async Task OnDatagram_FromAnAddressThatCannotBeDialed_IsIgnored(string source)
    {
        using var peers = new DiscoveryPeers(1);
        var (discovery, _, observer, _) = Create(peers, Wifi);
        await using var disposing = discovery;

        Assert.False(discovery.OnDatagram(Announcement.Build([peers.OwnForEachContact[0]], 50_001), IPAddress.Parse(source)));

        Assert.Empty(observer.HeardContacts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(273)]
    [InlineData(275)]
    public async Task OnDatagram_WrongLength_IsIgnored(int length)
    {
        using var peers = new DiscoveryPeers(1);
        var (discovery, _, observer, _) = Create(peers, Wifi);
        await using var disposing = discovery;

        Assert.False(discovery.OnDatagram(new byte[length], Neighbour));

        Assert.Empty(observer.HeardContacts);
    }

    [Fact]
    [Trait("Story", "US-05")]
    public async Task OnDatagram_ManyFromOneSource_IsCappedPerSourceAndOtherSourcesAreStillScreened()
    {
        using var peers = new DiscoveryPeers(1);
        var time = new ManualTimeProvider();
        var options = new DiscoveryOptions { ScreeningsPerSecondPerSource = 1, ScreeningBurstPerSource = 2 };
        var logger = new ListLogger<LocalNetworkDiscovery>();
        var (discovery, _, observer, interfaces) = Create(peers, options, time, logger, Wifi);
        await using var disposing = discovery;
        var contact = peers.OwnForEachContact[0];

        Assert.True(discovery.OnDatagram(Announcement.Build([contact], 50_001), Neighbour));
        Assert.True(discovery.OnDatagram(Announcement.Build([contact], 50_001), Neighbour));
        Assert.False(discovery.OnDatagram(Announcement.Build([contact], 50_001), Neighbour));
        Assert.True(discovery.OnDatagram(Announcement.Build([contact], 50_001), IPAddress.Parse("192.168.43.30")));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(discovery.OnDatagram(Announcement.Build([contact], 50_001), Neighbour));

        Assert.Equal(4, observer.HeardContacts.Count);
        interfaces.Snapshot = NetworkSnapshot.Create([Wifi], hasOtherNetwork: true);
        discovery.RefreshInterfaces();
        Assert.Contains(logger.Entries, static entry => entry.Id.Name == "ScreeningLimited" && entry.Message.StartsWith("1 ", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Story", "US-05")]
    public async Task OnDatagram_ManyFromManySources_IsCappedOverall()
    {
        using var peers = new DiscoveryPeers(1);
        var time = new ManualTimeProvider();
        var options = new DiscoveryOptions { ScreeningsPerSecond = 1, ScreeningBurst = 3 };
        var (discovery, _, observer, _) = Create(peers, options, time, NullLogger<LocalNetworkDiscovery>.Instance, Wifi);
        await using var disposing = discovery;

        for (var host = 1; host <= 6; host++)
        {
            discovery.OnDatagram(Announcement.Build([peers.OwnForEachContact[0]], 50_001), IPAddress.Parse($"192.168.43.{host}"));
        }

        Assert.Equal(3, observer.HeardContacts.Count);
    }

    [Fact]
    public async Task OnDatagram_ScreeningOfManySources_TracksAtMostTheConfiguredNumber()
    {
        using var peers = new DiscoveryPeers(1);
        var time = new ManualTimeProvider();
        var options = new DiscoveryOptions { MaxTrackedSources = 2 };
        var (discovery, _, observer, _) = Create(peers, options, time, NullLogger<LocalNetworkDiscovery>.Instance, Wifi);
        await using var disposing = discovery;

        for (var host = 1; host <= 3; host++)
        {
            discovery.OnDatagram(Announcement.Build([peers.OwnForEachContact[0]], 50_001), IPAddress.Parse($"192.168.43.{host}"));
        }

        Assert.Equal(2, observer.HeardContacts.Count);
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(discovery.OnDatagram(Announcement.Build([peers.OwnForEachContact[0]], 50_001), IPAddress.Parse("192.168.43.3")));
    }

    [Fact]
    [Trait("Story", "US-06")]
    public async Task Sweep_IsDueOnlyWhenNobodyWasHeardForAWhile()
    {
        using var peers = new DiscoveryPeers(1);
        var time = new ManualTimeProvider();
        var (discovery, _, _, _) = Create(peers, new DiscoveryOptions(), time, NullLogger<LocalNetworkDiscovery>.Instance, Wifi);
        await using var disposing = discovery;
        discovery.RefreshInterfaces();

        Assert.False(discovery.IsSweepDue());
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(discovery.IsSweepDue());

        Assert.True(discovery.OnDatagram(Announcement.Build([peers.OwnForEachContact[0]], 50_001), Neighbour));
        Assert.False(discovery.IsSweepDue());
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(discovery.IsSweepDue());
    }

    [Fact]
    public async Task Sweep_IsNeverDueOnSubnetsLargerThanTheLimitOrWithoutContacts()
    {
        using var peers = new DiscoveryPeers(1);
        using var lonely = new DiscoveryPeers(0);
        var time = new ManualTimeProvider();
        var large = new LocalInterface("eth0", 2, IPAddress.Parse("10.1.2.3"), 22, supportsMulticast: true);

        var (onLarge, _, _, _) = Create(peers, new DiscoveryOptions(), time, NullLogger<LocalNetworkDiscovery>.Instance, large);
        await using (onLarge)
        {
            onLarge.RefreshInterfaces();
            time.Advance(TimeSpan.FromMinutes(5));
            Assert.False(onLarge.IsSweepDue());
        }

        var (alone, _, _, _) = Create(lonely, new DiscoveryOptions(), time, NullLogger<LocalNetworkDiscovery>.Instance, Wifi);
        await using (alone)
        {
            alone.RefreshInterfaces();
            time.Advance(TimeSpan.FromMinutes(5));
            Assert.False(alone.IsSweepDue());
        }
    }

    [Fact]
    [Trait("Story", "US-06")]
    public async Task SweepAsync_SendsEachAnnouncementToEveryOtherHostOfTheSmallSubnetsOnly()
    {
        using var peers = new DiscoveryPeers(1);
        var large = new LocalInterface("eth1", 4, IPAddress.Parse("10.1.2.3"), 22, supportsMulticast: true);
        var (discovery, link, _, _) = Create(peers, new DiscoveryOptions { SweepBatchPause = TimeSpan.Zero }, new ManualTimeProvider(), NullLogger<LocalNetworkDiscovery>.Instance, Wired, large);
        await using var disposing = discovery;
        byte[] first = Announcement.Build([peers.ContactsOfOwn[0]], ListeningPort);
        byte[] second = Announcement.Build([peers.ContactsOfOwn[0]], ListeningPort);

        var sent = await discovery.SweepAsync([first, second], [Wired, large], TestContext.Current.CancellationToken);

        Assert.Equal(10, sent);
        Assert.All(link.Sent, static datagram => Assert.True(datagram.Sweep));
        Assert.All(link.Sent, static datagram => Assert.Equal("eth0", datagram.From.Name));
        Assert.Equal(
            ["10.20.0.1", "10.20.0.2", "10.20.0.3", "10.20.0.4", "10.20.0.6"],
            link.Sent.Select(static datagram => datagram.Destination.Address.ToString()).Distinct());
        Assert.All(link.Sent, static datagram => Assert.Equal(45821, datagram.Destination.Port));
    }

    [Fact]
    [Trait("Story", "US-06")]
    public async Task AnnounceRound_WhenTheSweepIsDue_StartsOneSweepAndNotAgainUntilItsIntervalPassed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var peers = new DiscoveryPeers(1);
        var time = new ManualTimeProvider();
        var options = new DiscoveryOptions { SweepBatchPause = TimeSpan.Zero };
        var (discovery, link, _, _) = Create(peers, options, time, NullLogger<LocalNetworkDiscovery>.Instance, Wired);
        await using var disposing = discovery;
        discovery.SetAnnouncedPort(ListeningPort);

        await discovery.AnnounceRoundAsync(cancellationToken);
        Assert.DoesNotContain(link.Sent, static datagram => datagram.Sweep);
        time.Advance(TimeSpan.FromSeconds(31));
        await discovery.AnnounceRoundAsync(cancellationToken);
        await WaitUntilAsync(() => link.Sent.Count(static datagram => datagram.Sweep) == 5 && !discovery.IsSweepDue(), cancellationToken);
        time.Advance(TimeSpan.FromSeconds(30));
        await discovery.AnnounceRoundAsync(cancellationToken);
        await Task.Delay(100, cancellationToken);
        Assert.Equal(5, link.Sent.Count(static datagram => datagram.Sweep));

        time.Advance(TimeSpan.FromSeconds(31));
        await discovery.AnnounceRoundAsync(cancellationToken);
        await WaitUntilAsync(() => link.Sent.Count(static datagram => datagram.Sweep) == 10, cancellationToken);
    }

    [Fact]
    [Trait("Story", "US-07")]
    public async Task RefreshInterfaces_ReportsOnlyChangesAndHandsTheInterfacesToTheLink()
    {
        using var peers = new DiscoveryPeers(1);
        var (discovery, link, observer, interfaces) = Create(peers, Wifi);
        await using var disposing = discovery;
        Assert.Null(discovery.Network);

        Assert.True(discovery.RefreshInterfaces());
        Assert.False(discovery.RefreshInterfaces());
        interfaces.Snapshot = NetworkSnapshot.None;
        Assert.True(discovery.RefreshInterfaces());

        Assert.Equal(2, observer.Networks.Count);
        Assert.Equal(Wifi, Assert.Single(observer.Networks[0].Interfaces));
        Assert.False(observer.Networks[1].HasAnyNetwork);
        Assert.Same(NetworkSnapshot.None, discovery.Network);
        Assert.Empty(link.Interfaces);
    }

    [Fact]
    public async Task RefreshInterfaces_SourceFails_KeepsTheLastNetwork()
    {
        using var peers = new DiscoveryPeers(1);
        var logger = new ListLogger<LocalNetworkDiscovery>();
        var (discovery, _, _, interfaces) = Create(peers, logger, Wifi);
        await using var disposing = discovery;
        discovery.RefreshInterfaces();
        interfaces.Failure = new InvalidOperationException("The platform list failed.");

        Assert.False(discovery.RefreshInterfaces());

        Assert.Equal(Wifi, Assert.Single(discovery.Network!.Interfaces));
        Assert.Contains(logger.Entries, static entry => entry.Id.Name == "DiscoveryUnexpected" && entry.Message.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
    }

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-06")]
    public async Task Start_RunsTheLoopsUntilDisposed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var peers = new DiscoveryPeers(1);
        var options = new DiscoveryOptions { AnnounceInterval = TimeSpan.FromMilliseconds(50), HeardLifetime = TimeSpan.FromSeconds(1) };
        var (discovery, link, observer, _) = Create(peers, options, TimeProvider.System, NullLogger<LocalNetworkDiscovery>.Instance, Wifi);

        discovery.Start(ListeningPort);
        Assert.Equal(DiscoveryState.Running, discovery.State);
        Assert.Equal(ListeningPort, discovery.AnnouncedPort);
        await WaitUntilAsync(() => link.Sent.Count >= 6, cancellationToken);
        link.Deliver(Announcement.Build([peers.OwnForEachContact[0]], 50_001), Neighbour);
        await WaitUntilAsync(() => observer.HeardContacts.Count == 1, cancellationToken);
        Assert.Throws<InvalidOperationException>(() => discovery.Start(ListeningPort));

        await discovery.DisposeAsync();

        Assert.Equal(DiscoveryState.Stopped, discovery.State);
        Assert.True(link.Disposed);
        var count = link.Sent.Count;
        await Task.Delay(150, cancellationToken);
        Assert.Equal(count, link.Sent.Count);
        await discovery.DisposeAsync();
    }

    [Fact(Timeout = 30_000)]
    public async Task AnnounceSoon_StartsARoundWithoutWaitingForTheInterval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var peers = new DiscoveryPeers(1);
        var options = new DiscoveryOptions { AnnounceInterval = TimeSpan.FromHours(1), HeardLifetime = TimeSpan.FromHours(2) };
        var (discovery, link, _, _) = Create(peers, options, TimeProvider.System, NullLogger<LocalNetworkDiscovery>.Instance, Wifi);
        await using var disposing = discovery;
        discovery.Start(null);
        await Task.Delay(100, cancellationToken);
        Assert.Empty(link.Sent);

        discovery.SetAnnouncedPort(ListeningPort);

        await WaitUntilAsync(() => link.Sent.Count == 2, cancellationToken);
        discovery.AnnounceSoon();
        await WaitUntilAsync(() => link.Sent.Count == 4, cancellationToken);
    }

    [Fact(Timeout = 30_000)]
    public async Task Start_PortCannotBeOpened_StillAnnouncesButHearsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var peers = new DiscoveryPeers(1);
        var options = new DiscoveryOptions { AnnounceInterval = TimeSpan.FromMilliseconds(50), HeardLifetime = TimeSpan.FromSeconds(1) };
        var logger = new ListLogger<LocalNetworkDiscovery>();
        var (discovery, link, observer, _) = Create(peers, options, TimeProvider.System, logger, Wifi);
        await using var disposing = discovery;
        link.OpenResult = SocketError.AddressAlreadyInUse;

        discovery.Start(ListeningPort);

        Assert.Equal(DiscoveryState.Unavailable, discovery.State);
        await WaitUntilAsync(() => link.Sent.Count >= 2, cancellationToken);
        link.Deliver(Announcement.Build([peers.OwnForEachContact[0]], 50_001), Neighbour);
        await Task.Delay(150, cancellationToken);
        Assert.Empty(observer.HeardContacts);
        Assert.Contains(logger.Entries, static entry => entry.Id.Name == "DiscoveryUnavailable");
    }

    [Fact]
    public async Task Construction_InvalidOptionsOrPorts_AreRefused()
    {
        using var peers = new DiscoveryPeers(1);
        var link = new FakeLink();
        await using var disposingLink = link;

        Assert.Throws<ArgumentException>(() => new LocalNetworkDiscovery(
            new FakeContacts(),
            new FakeInterfaces(),
            link,
            new RecordingDiscoveryObserver(),
            new DiscoveryOptions { Port = 0 },
            TimeProvider.System,
            NullLogger<LocalNetworkDiscovery>.Instance));

        var (discovery, _, _, _) = Create(peers, Wifi);
        await using var disposing = discovery;
        Assert.Throws<ArgumentOutOfRangeException>(() => discovery.SetAnnouncedPort(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => discovery.Start(70_000));
    }

    private static (LocalNetworkDiscovery Discovery, FakeLink Link, RecordingDiscoveryObserver Observer, FakeInterfaces Interfaces) Create(
        DiscoveryPeers peers,
        params LocalInterface[] interfaces) =>
        Create(peers, new DiscoveryOptions(), new ManualTimeProvider(), NullLogger<LocalNetworkDiscovery>.Instance, interfaces);

    private static (LocalNetworkDiscovery Discovery, FakeLink Link, RecordingDiscoveryObserver Observer, FakeInterfaces Interfaces) Create(
        DiscoveryPeers peers,
        Microsoft.Extensions.Logging.ILogger<LocalNetworkDiscovery> logger,
        params LocalInterface[] interfaces) =>
        Create(peers, new DiscoveryOptions(), new ManualTimeProvider(), logger, interfaces);

    private static (LocalNetworkDiscovery Discovery, FakeLink Link, RecordingDiscoveryObserver Observer, FakeInterfaces Interfaces) Create(
        DiscoveryPeers peers,
        DiscoveryOptions options,
        TimeProvider time,
        Microsoft.Extensions.Logging.ILogger<LocalNetworkDiscovery> logger,
        params LocalInterface[] interfaces)
    {
        var link = new FakeLink();
        var observer = new RecordingDiscoveryObserver();
        var source = new FakeInterfaces { Snapshot = NetworkSnapshot.Create(interfaces, hasOtherNetwork: false) };
        var contacts = new FakeContacts { Current = peers.ContactsOfOwn };
        var discovery = new LocalNetworkDiscovery(contacts, source, link, observer, options, time, logger);
        return (discovery, link, observer, source);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
