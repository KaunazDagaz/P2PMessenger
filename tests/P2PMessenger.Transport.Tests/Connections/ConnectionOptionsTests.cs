using Microsoft.Extensions.Logging.Abstractions;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Transport.Tests.Connections;

public sealed class ConnectionOptionsTests
{
    [Fact]
    public void FindProblem_Defaults_FindsNone()
    {
        var options = new ConnectionOptions();

        Assert.Null(options.FindProblem());
        Assert.True(options.IdleTimeout > options.KeepAliveInterval);
        Assert.True(options.HintTimeout < options.HandshakeTimeout);
    }

    [Fact]
    [Trait("Story", "US-09")]
    public void Defaults_TorRoute_HasItsOwnLongerTimes()
    {
        var options = new ConnectionOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.TorDialTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.TorHintTimeout);
        Assert.Equal(TimeSpan.FromSeconds(20), options.TorHandshakeTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.TorRetryDelayMin);
        Assert.Equal(TimeSpan.FromSeconds(60), options.TorRetryDelayMax);
        Assert.Equal(options.DialTimeout, options.DialTimeoutFor(ConnectionRoute.Nearby));
        Assert.Equal(options.HintTimeout, options.HintTimeoutFor(ConnectionRoute.Nearby));
        Assert.Equal(options.HandshakeTimeout, options.HandshakeTimeoutFor(ConnectionRoute.Nearby));
        Assert.Equal(options.TorDialTimeout, options.DialTimeoutFor(ConnectionRoute.Tor));
        Assert.Equal(options.TorHintTimeout, options.HintTimeoutFor(ConnectionRoute.Tor));
        Assert.Equal(options.TorHandshakeTimeout, options.HandshakeTimeoutFor(ConnectionRoute.Tor));
    }

    [Theory]
    [InlineData("dial")]
    [InlineData("hint")]
    [InlineData("handshake")]
    [InlineData("write")]
    [InlineData("idle")]
    [InlineData("window")]
    [InlineData("reconnect")]
    [InlineData("torDial")]
    [InlineData("torHint")]
    [InlineData("torHandshake")]
    [InlineData("torRetry")]
    [InlineData("torRetryMax")]
    [InlineData("pending")]
    [InlineData("burst")]
    [InlineData("rate")]
    [InlineData("sourceRate")]
    [InlineData("admission")]
    public void FindProblem_InvalidValue_IsReported(string field)
    {
        Assert.NotNull(Invalid(field).FindProblem());
    }

    [Fact]
    public void Constructor_InvalidOptions_ThrowsArgumentException()
    {
        using var keys = KeySet.Create();
        var options = new ConnectionOptions { KeepAliveInterval = TimeSpan.FromSeconds(2), IdleTimeout = TimeSpan.FromSeconds(1) };

        var exception = Assert.Throws<ArgumentException>(() => Create(keys, options));

        Assert.Equal("options", exception.ParamName);
    }

    private static ConnectionManager Create(KeySet keys, ConnectionOptions options) =>
        new(keys, new KnownContacts(), new SilentObserver(), options, TimeProvider.System, NullLogger<ConnectionManager>.Instance);

    private static ConnectionOptions Invalid(string field) => field switch
    {
        "dial" => new ConnectionOptions { DialTimeout = TimeSpan.Zero },
        "hint" => new ConnectionOptions { HintTimeout = TimeSpan.Zero },
        "handshake" => new ConnectionOptions { HandshakeTimeout = TimeSpan.FromSeconds(-1) },
        "write" => new ConnectionOptions { WriteTimeout = TimeSpan.FromDays(2) },
        "idle" => new ConnectionOptions { KeepAliveInterval = TimeSpan.FromSeconds(30), IdleTimeout = TimeSpan.FromSeconds(30) },
        "window" => new ConnectionOptions { SimultaneousOpenWindow = TimeSpan.Zero },
        "reconnect" => new ConnectionOptions { ReconnectDelayMin = TimeSpan.FromSeconds(5), ReconnectDelayMax = TimeSpan.FromSeconds(1) },
        "torDial" => new ConnectionOptions { TorDialTimeout = TimeSpan.Zero },
        "torHint" => new ConnectionOptions { TorHintTimeout = TimeSpan.FromSeconds(-1) },
        "torHandshake" => new ConnectionOptions { TorHandshakeTimeout = TimeSpan.FromDays(2) },
        "torRetry" => new ConnectionOptions { TorRetryDelayMin = TimeSpan.Zero },
        "torRetryMax" => new ConnectionOptions { TorRetryDelayMin = TimeSpan.FromSeconds(10), TorRetryDelayMax = TimeSpan.FromSeconds(5) },
        "pending" => new ConnectionOptions { Admission = new AdmissionLimits { MaxPendingConnections = 0 } },
        "burst" => new ConnectionOptions { Admission = new AdmissionLimits { HandshakeBurstPerSource = -1 } },
        "rate" => new ConnectionOptions { Admission = new AdmissionLimits { HandshakesPerSecond = double.NaN } },
        "sourceRate" => new ConnectionOptions { Admission = new AdmissionLimits { HandshakesPerSecondPerSource = 0 } },
        "admission" => new ConnectionOptions { Admission = null! },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown field."),
    };

    private sealed class SilentObserver : IConnectionObserver
    {
        public ValueTask ConnectedAsync(ContactConnection connection, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask MessageReceivedAsync(ContactConnection connection, SessionMessage message, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisconnectedAsync(ContactConnection connection, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
