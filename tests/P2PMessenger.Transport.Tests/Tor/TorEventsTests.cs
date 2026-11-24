using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Transport.Tests.Tor;

[Trait("Story", "US-10")]
public sealed class TorEventsTests
{
    private const string ServiceId = "hvabpq7iioevvevxbktu2g36xsojqlgpf3cjndgazvk7ckxumygcmyyd";

    [Fact]
    public void TryParse_BootstrapNotice_GivesTheProgress()
    {
        Assert.True(TorEvents.TryParse("STATUS_CLIENT NOTICE BOOTSTRAP PROGRESS=45 TAG=requesting_descriptors SUMMARY=\"Asking for relay descriptors\"", out var parsed));

        Assert.Equal(new TorEvent(TorEventKind.Bootstrap, 45), parsed);
    }

    [Fact]
    public void TryParse_BootstrapWarning_KeepsTheReasonButNoAddress()
    {
        Assert.True(TorEvents.TryParse("STATUS_CLIENT WARN BOOTSTRAP PROGRESS=5 TAG=conn SUMMARY=\"Connecting to a relay\" WARNING=\"Connection refused\" REASON=CONNECTREFUSED COUNT=3 RECOMMENDATION=ignore HOSTID=\"ABCD\" HOSTADDR=\"192.0.2.7:443\"", out var parsed));

        Assert.Equal(new TorEvent(TorEventKind.Bootstrap, 5, true, "CONNECTREFUSED"), parsed);
        Assert.DoesNotContain("192.0.2.7", parsed.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseBootstrapPhase_GetInfoValue_GivesTheProgress()
    {
        Assert.True(TorEvents.TryParseBootstrapPhase("NOTICE BOOTSTRAP PROGRESS=100 TAG=done SUMMARY=\"Done\"", out var parsed));

        Assert.Equal(100, parsed.Progress);
        Assert.False(parsed.Warning);
    }

    [Theory]
    [InlineData("STATUS_CLIENT NOTICE CIRCUIT_ESTABLISHED", nameof(TorEventKind.CircuitsEstablished))]
    [InlineData("STATUS_CLIENT NOTICE CIRCUIT_NOT_ESTABLISHED REASON=DIR_ALL_UNREACHABLE", nameof(TorEventKind.CircuitsNotEstablished))]
    [InlineData("STATUS_GENERAL WARN CLOCK_SKEW SKEW=-86400 SOURCE=NETWORKSTATUS:198.51.100.1:80", nameof(TorEventKind.ClockSkew))]
    [InlineData("STATUS_GENERAL NOTICE CLOCK_JUMPED TIME=120", nameof(TorEventKind.ClockJumped))]
    [InlineData("STATUS_GENERAL ERR DIR_ALL_UNREACHABLE", nameof(TorEventKind.DirectoriesUnreachable))]
    [InlineData("NETWORK_LIVENESS UP", nameof(TorEventKind.NetworkUp))]
    [InlineData("NETWORK_LIVENESS DOWN", nameof(TorEventKind.NetworkDown))]
    [InlineData("HS_DESC UPLOADED " + ServiceId + " UNKNOWN $AAAA~relay", nameof(TorEventKind.DescriptorUploaded))]
    [InlineData("HS_DESC FAILED " + ServiceId + " UNKNOWN $AAAA~relay REASON=UPLOAD_REJECTED", nameof(TorEventKind.DescriptorFailed))]
    public void TryParse_KnownEvents_GivesTheirKind(string text, string kind)
    {
        Assert.True(TorEvents.TryParse(text, out var parsed));

        Assert.Equal(Enum.Parse<TorEventKind>(kind), parsed.Kind);
    }

    [Fact]
    public void TryParse_DescriptorUploaded_KeepsTheServiceId()
    {
        Assert.True(TorEvents.TryParse("HS_DESC UPLOADED " + ServiceId + " UNKNOWN $AAAA~relay", out var parsed));

        Assert.Equal(ServiceId, parsed.ServiceId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CIRC 1 BUILT")]
    [InlineData("STATUS_CLIENT NOTICE")]
    [InlineData("STATUS_CLIENT NOTICE BOOTSTRAP TAG=done")]
    [InlineData("STATUS_CLIENT NOTICE BOOTSTRAP PROGRESS=101")]
    [InlineData("STATUS_CLIENT NOTICE BOOTSTRAP PROGRESS=-1")]
    [InlineData("STATUS_CLIENT NOTICE BOOTSTRAP PROGRESS=1e2")]
    [InlineData("STATUS_GENERAL NOTICE CONSENSUS_ARRIVED")]
    [InlineData("HS_DESC UPLOADED notanaddress UNKNOWN x")]
    [InlineData("HS_DESC REQUESTED " + ServiceId + " UNKNOWN x")]
    [InlineData("NETWORK_LIVENESS SIDEWAYS")]
    [InlineData("STATUS_CLIENT NOTICE BOOTSTRAP PROGRESS=\"50")]
    public void TryParse_UnknownOrBrokenEvents_ReturnsFalse(string text)
    {
        Assert.False(TorEvents.TryParse(text, out _));
    }

    [Fact]
    public void TryParse_UnexpectedReason_IsDropped()
    {
        Assert.True(TorEvents.TryParse("STATUS_CLIENT WARN BOOTSTRAP PROGRESS=5 REASON=\"198.51.100.1\"", out var parsed));

        Assert.Null(parsed.Reason);
    }

    [Theory]
    [InlineData(ServiceId, true)]
    [InlineData("HVABPQ7IIOEVVEVXBKTU2G36XSOJQLGPF3CJNDGAZVK7CKXUMYGCMYYD", false)]
    [InlineData("hvabpq7iioevvevxbktu2g36xsojqlgpf3cjndgazvk7ckxumygcmyy", false)]
    [InlineData("hvabpq7iioevvevxbktu2g36xsojqlgpf3cjndgazvk7ckxumygcmyy1", false)]
    public void IsServiceId_ChecksLengthAndAlphabet(string text, bool expected)
    {
        Assert.Equal(expected, TorEvents.IsServiceId(text));
    }
}
