using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Integration.Tests.Connections;

internal static class TestOptions
{
    public static AdmissionLimits Generous { get; } = new()
    {
        MaxPendingConnections = 256,
        MaxPendingPerSource = 256,
        HandshakesPerSecond = 10_000,
        HandshakeBurst = 10_000,
        HandshakesPerSecondPerSource = 10_000,
        HandshakeBurstPerSource = 10_000,
    };

    public static ConnectionOptions Fast { get; } = new()
    {
        DialTimeout = TimeSpan.FromSeconds(10),
        HandshakeTimeout = TimeSpan.FromSeconds(5),
        WriteTimeout = TimeSpan.FromSeconds(5),
        KeepAliveInterval = TimeSpan.FromSeconds(15),
        IdleTimeout = TimeSpan.FromSeconds(45),
        SimultaneousOpenWindow = TimeSpan.FromSeconds(10),
        ReconnectDelayMin = TimeSpan.FromMilliseconds(50),
        ReconnectDelayMax = TimeSpan.FromMilliseconds(400),
        Admission = Generous,
    };
}
