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

    [Theory]
    [InlineData("dial")]
    [InlineData("hint")]
    [InlineData("handshake")]
    [InlineData("write")]
    [InlineData("idle")]
    [InlineData("window")]
    [InlineData("reconnect")]
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
