using System.Net;
using P2PMessenger.Transport.Admission;
using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Transport.Tests.Admission;

public sealed class AdmissionControlTests
{
    private static readonly IPAddress First = IPAddress.Parse("192.0.2.10");
    private static readonly IPAddress Second = IPAddress.Parse("192.0.2.20");
    private static readonly IPAddress Third = IPAddress.Parse("192.0.2.30");

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void TryAdmit_BeyondThePendingLimit_IsRefusedUntilASlotIsReleased()
    {
        var admission = new AdmissionControl(Limits() with { MaxPendingConnections = 2 }, new ManualTimeProvider());

        Assert.True(admission.TryAdmit(First, out var one, out _));
        Assert.True(admission.TryAdmit(Second, out var two, out _));
        Assert.False(admission.TryAdmit(Third, out var refused, out var refusal));
        Assert.Null(refused);
        Assert.Equal(AdmissionRefusal.TooManyPending, refusal);

        one.Dispose();

        Assert.True(admission.TryAdmit(Third, out var three, out _));
        two.Dispose();
        three.Dispose();
        Assert.Equal(0, admission.Pending);
    }

    [Fact]
    public void Dispose_Twice_ReleasesTheSlotOnce()
    {
        var admission = new AdmissionControl(Limits() with { MaxPendingConnections = 2 }, new ManualTimeProvider());
        Assert.True(admission.TryAdmit(First, out var one, out _));
        Assert.True(admission.TryAdmit(Second, out var two, out _));

        one.Dispose();
        one.Dispose();

        Assert.Equal(1, admission.Pending);
        Assert.True(admission.TryAdmit(Third, out var three, out _));
        Assert.False(admission.TryAdmit(Third, out _, out var refusal));
        Assert.Equal(AdmissionRefusal.TooManyPending, refusal);
        two.Dispose();
        three.Dispose();
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void TryAdmit_BeyondThePendingLimitOfOneSource_RefusesOnlyThatSource()
    {
        var admission = new AdmissionControl(Limits() with { MaxPendingPerSource = 1 }, new ManualTimeProvider());

        Assert.True(admission.TryAdmit(First, out var one, out _));
        Assert.False(admission.TryAdmit(First, out _, out var refusal));
        Assert.Equal(AdmissionRefusal.TooManyPendingFromSource, refusal);
        Assert.True(admission.TryAdmit(Second, out var two, out _));

        one.Dispose();

        Assert.True(admission.TryAdmit(First, out var again, out _));
        two.Dispose();
        again.Dispose();
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void TryAdmit_FasterThanTheHandshakeRate_IsRefusedUntilTokensRefill()
    {
        var time = new ManualTimeProvider();
        var admission = new AdmissionControl(Limits() with { HandshakesPerSecond = 1, HandshakeBurst = 2 }, time);

        AdmitAndRelease(admission, First);
        AdmitAndRelease(admission, Second);
        Assert.False(admission.TryAdmit(Third, out _, out var refusal));
        Assert.Equal(AdmissionRefusal.TooManyHandshakes, refusal);

        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(admission.TryAdmit(Third, out _, out _));

        time.Advance(TimeSpan.FromMilliseconds(1));
        AdmitAndRelease(admission, Third);
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void TryAdmit_FasterThanTheRateOfOneSource_RefusesOnlyThatSource()
    {
        var time = new ManualTimeProvider();
        var admission = new AdmissionControl(Limits() with { HandshakesPerSecondPerSource = 1, HandshakeBurstPerSource = 1 }, time);

        AdmitAndRelease(admission, First);
        Assert.False(admission.TryAdmit(First, out _, out var refusal));
        Assert.Equal(AdmissionRefusal.TooManyHandshakesFromSource, refusal);
        AdmitAndRelease(admission, Second);

        time.Advance(TimeSpan.FromSeconds(1));
        AdmitAndRelease(admission, First);
    }

    [Fact]
    public void TryAdmit_RefusedByTheOverallRate_TakesNoTokenFromTheSource()
    {
        var time = new ManualTimeProvider();
        var limits = Limits() with
        {
            HandshakesPerSecond = 1,
            HandshakeBurst = 1,
            HandshakesPerSecondPerSource = 0.001,
            HandshakeBurstPerSource = 2,
        };
        var admission = new AdmissionControl(limits, time);

        AdmitAndRelease(admission, First);
        Assert.False(admission.TryAdmit(First, out _, out var refusal));
        Assert.Equal(AdmissionRefusal.TooManyHandshakes, refusal);

        time.Advance(TimeSpan.FromSeconds(1));
        AdmitAndRelease(admission, First);
    }

    [Fact]
    public void TryAdmit_WithoutASource_IsLimitedOnlyByTheOverallLimits()
    {
        var admission = new AdmissionControl(Limits() with { MaxPendingPerSource = 1, HandshakeBurstPerSource = 1 }, new ManualTimeProvider());

        Assert.True(admission.TryAdmit(null, out var one, out _));
        Assert.True(admission.TryAdmit(null, out var two, out _));
        Assert.True(admission.TryAdmit(null, out var three, out _));

        Assert.Equal(3, admission.Pending);
        Assert.Equal(0, admission.TrackedSources);
        one.Dispose();
        two.Dispose();
        three.Dispose();
    }

    [Fact]
    public void TryAdmit_IPv4MappedAddress_CountsAsItsIPv4Source()
    {
        var admission = new AdmissionControl(Limits() with { MaxPendingPerSource = 1 }, new ManualTimeProvider());

        Assert.True(admission.TryAdmit(First, out var one, out _));
        Assert.False(admission.TryAdmit(First.MapToIPv6(), out _, out var refusal));

        Assert.Equal(AdmissionRefusal.TooManyPendingFromSource, refusal);
        Assert.Equal(1, admission.TrackedSources);
        one.Dispose();
    }

    [Fact]
    public void TryAdmit_MoreSourcesThanTracked_ForgetsIdleSourcesFirst()
    {
        var time = new ManualTimeProvider();
        var limits = Limits() with { MaxTrackedSources = 2, HandshakesPerSecondPerSource = 1, HandshakeBurstPerSource = 1 };
        var admission = new AdmissionControl(limits, time);

        AdmitAndRelease(admission, First);
        AdmitAndRelease(admission, Second);
        Assert.False(admission.TryAdmit(Third, out _, out var refusal));
        Assert.Equal(AdmissionRefusal.TooManySources, refusal);

        time.Advance(TimeSpan.FromSeconds(1));

        AdmitAndRelease(admission, Third);
        Assert.Equal(1, admission.TrackedSources);
    }

    private static void AdmitAndRelease(AdmissionControl admission, IPAddress? source)
    {
        Assert.True(admission.TryAdmit(source, out var slot, out var refusal), $"Refused: {refusal}");
        slot.Dispose();
    }

    private static AdmissionLimits Limits() => new()
    {
        MaxPendingConnections = 100,
        MaxPendingPerSource = 100,
        HandshakesPerSecond = 1000,
        HandshakeBurst = 1000,
        HandshakesPerSecondPerSource = 1000,
        HandshakeBurstPerSource = 1000,
        MaxTrackedSources = 100,
    };
}
