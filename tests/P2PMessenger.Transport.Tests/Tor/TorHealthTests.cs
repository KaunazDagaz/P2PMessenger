using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Transport.Tests.Tor;

[Trait("Story", "US-10")]
public sealed class TorHealthTests
{
    private const string Own = "hvabpq7iioevvevxbktu2g36xsojqlgpf3cjndgazvk7ckxumygcmyyd";
    private const string Other = "25njqamcweflpvkl73j4szahhihoc4xt3ktcgjnpaingr5yhkenl5sid";

    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(120);

    private readonly ManualTimeProvider time = new();

    [Fact]
    public void Evaluate_JustStarted_IsStartingAtZero()
    {
        var health = Health();

        Assert.Equal(TorStatus.Starting(0), health.Evaluate());
    }

    [Fact]
    public void Evaluate_ProgressWithoutWarnings_StaysStartingUntilTheLimit()
    {
        var health = Health();
        health.Apply(new TorEvent(TorEventKind.Bootstrap, 40));
        time.Advance(Limit - TimeSpan.FromSeconds(1));

        Assert.Equal(TorStatus.Starting(40), health.Evaluate());

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new TorStatus(TorState.Unavailable, 40, TorProblem.CannotReachTor, false), health.Evaluate());
    }

    [Fact]
    public void Evaluate_WarningsPastTheGrace_CannotReachTor()
    {
        var health = Health();
        health.Apply(new TorEvent(TorEventKind.Bootstrap, 5, true, "CONNECTREFUSED"));
        time.Advance(Grace - TimeSpan.FromSeconds(1));
        Assert.Equal(TorState.Starting, health.Evaluate().State);

        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(TorProblem.CannotReachTor, health.Evaluate().Problem);
    }

    [Theory]
    [InlineData(nameof(TorEventKind.DirectoriesUnreachable))]
    [InlineData(nameof(TorEventKind.NetworkDown))]
    public void Evaluate_NetworkTroublePastTheGrace_CannotReachTor(string kind)
    {
        var health = Health();
        health.Apply(new TorEvent(Enum.Parse<TorEventKind>(kind)));
        time.Advance(Grace);

        Assert.Equal(TorProblem.CannotReachTor, health.Evaluate().Problem);
    }

    [Fact]
    public void Evaluate_NetworkUpAgain_ClearsTheNetworkTrouble()
    {
        var health = Health();
        health.Apply(new TorEvent(TorEventKind.NetworkDown));
        health.Apply(new TorEvent(TorEventKind.NetworkUp));
        time.Advance(Grace);

        Assert.Equal(TorState.Starting, health.Evaluate().State);
    }

    [Fact]
    public void Evaluate_ClockSkewBeforeBootstrap_IsAWrongClockAtOnce()
    {
        var health = Health();
        health.Apply(new TorEvent(TorEventKind.Bootstrap, 10));
        health.Apply(new TorEvent(TorEventKind.ClockSkew));

        Assert.Equal(new TorStatus(TorState.Unavailable, 10, TorProblem.ClockWrong, false), health.Evaluate());
    }

    [Fact]
    public void Evaluate_ClockFixedAndBootstrapped_IsConnected()
    {
        var health = Health();
        health.Apply(new TorEvent(TorEventKind.ClockSkew));
        health.Apply(new TorEvent(TorEventKind.Bootstrap, 100));

        Assert.Equal(TorState.Connected, health.Evaluate().State);
    }

    [Fact]
    public void Evaluate_Bootstrapped_IsConnected()
    {
        var health = Health();
        health.Apply(new TorEvent(TorEventKind.Bootstrap, 100));
        time.Advance(Limit * 2);

        Assert.Equal(new TorStatus(TorState.Connected, 100, TorProblem.None, false), health.Evaluate());
    }

    [Fact]
    public void Evaluate_OwnDescriptorUploaded_IsPublished()
    {
        var health = Health();
        health.ExpectService(Own);
        health.Apply(new TorEvent(TorEventKind.Bootstrap, 100));
        health.Apply(new TorEvent(TorEventKind.DescriptorUploaded, ServiceId: Own));

        Assert.True(health.Evaluate().OnionServicePublished);
    }

    [Fact]
    public void Evaluate_OtherDescriptorUploaded_IsNotPublished()
    {
        var health = Health();
        health.ExpectService(Own);
        health.Apply(new TorEvent(TorEventKind.DescriptorUploaded, ServiceId: Other));

        Assert.False(health.Evaluate().OnionServicePublished);
    }

    [Fact]
    public void Evaluate_CircuitsLostBriefly_StaysConnected()
    {
        var health = Health();
        health.Apply(new TorEvent(TorEventKind.Bootstrap, 100));
        health.Apply(new TorEvent(TorEventKind.CircuitsNotEstablished, Reason: "CLOCK_JUMPED"));
        time.Advance(Grace - TimeSpan.FromSeconds(1));

        Assert.Equal(TorState.Connected, health.Evaluate().State);
    }

    [Fact]
    public void Evaluate_CircuitsLostPastTheGrace_CannotReachTorUntilTheyAreBack()
    {
        var health = Health();
        health.Apply(new TorEvent(TorEventKind.Bootstrap, 100));
        health.Apply(new TorEvent(TorEventKind.CircuitsNotEstablished, Reason: "DIR_ALL_UNREACHABLE"));
        time.Advance(TimeSpan.FromSeconds(10));
        health.Apply(new TorEvent(TorEventKind.CircuitsNotEstablished, Reason: "DIR_ALL_UNREACHABLE"));
        time.Advance(Grace - TimeSpan.FromSeconds(10));

        Assert.Equal(TorProblem.CannotReachTor, health.Evaluate().Problem);

        health.Apply(new TorEvent(TorEventKind.CircuitsEstablished));
        Assert.Equal(TorState.Connected, health.Evaluate().State);
    }

    [Fact]
    public void Evaluate_CircuitsLostWithAClockSkew_IsAWrongClock()
    {
        var health = Health();
        health.Apply(new TorEvent(TorEventKind.Bootstrap, 100));
        health.Apply(new TorEvent(TorEventKind.CircuitsNotEstablished));
        health.Apply(new TorEvent(TorEventKind.ClockSkew));
        time.Advance(Grace);

        Assert.Equal(TorProblem.ClockWrong, health.Evaluate().Problem);
    }

    [Fact]
    public void Restart_ForgetsEverything()
    {
        var health = Health();
        health.ExpectService(Own);
        health.Apply(new TorEvent(TorEventKind.Bootstrap, 100));
        health.Apply(new TorEvent(TorEventKind.DescriptorUploaded, ServiceId: Own));
        time.Advance(Limit);

        health.Restart();

        Assert.Equal(TorStatus.Starting(0), health.Evaluate());
        health.Apply(new TorEvent(TorEventKind.DescriptorUploaded, ServiceId: Own));
        Assert.False(health.Evaluate().OnionServicePublished);
    }

    private TorHealth Health() => new(Grace, Limit, time);
}
