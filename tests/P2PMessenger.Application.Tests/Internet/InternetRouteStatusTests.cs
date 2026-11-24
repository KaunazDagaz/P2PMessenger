using P2PMessenger.Application.Internet;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Application.Tests.Internet;

[Trait("Story", "US-10")]
public sealed class InternetRouteStatusTests
{
    [Fact]
    public void From_TorConnected_IsConnected()
    {
        var status = InternetRouteStatus.From(new TorStatus(TorState.Connected, 100, TorProblem.None, true), hasAnyNetwork: true);

        Assert.Equal(new InternetRouteStatus(InternetRouteState.Connected, 100, InternetRouteProblem.None, true), status);
        Assert.True(status.IsAvailable);
    }

    [Fact]
    public void From_TorStarting_IsStartingWithItsProgress()
    {
        var status = InternetRouteStatus.From(TorStatus.Starting(45), hasAnyNetwork: true);

        Assert.Equal(new InternetRouteStatus(InternetRouteState.Starting, 45, InternetRouteProblem.None, false), status);
        Assert.False(status.IsAvailable);
    }

    [Fact]
    public void From_TorStillConnectedWhenTheNetworkIsGone_SaysThatThereIsNoNetwork()
    {
        var status = InternetRouteStatus.From(new TorStatus(TorState.Connected, 100, TorProblem.None, true), hasAnyNetwork: false);

        Assert.Equal(new InternetRouteStatus(InternetRouteState.Unavailable, 100, InternetRouteProblem.NoNetwork, true), status);
        Assert.False(status.IsAvailable);
    }

    [Theory]
    [InlineData(TorState.Starting, TorProblem.None)]
    [InlineData(TorState.Unavailable, TorProblem.CannotReachTor)]
    [InlineData(TorState.Unavailable, TorProblem.ClockWrong)]
    public void From_NoNetworkAtAll_SaysSoInsteadOfGuessing(TorState state, TorProblem problem)
    {
        var status = InternetRouteStatus.From(new TorStatus(state, 5, problem, false), hasAnyNetwork: false);

        Assert.Equal(InternetRouteState.Unavailable, status.State);
        Assert.Equal(InternetRouteProblem.NoNetwork, status.Problem);
    }

    [Theory]
    [InlineData(TorProblem.ClockWrong, InternetRouteProblem.ClockWrong)]
    [InlineData(TorProblem.CannotReachTor, InternetRouteProblem.CannotReachTor)]
    [InlineData(TorProblem.StartFailed, InternetRouteProblem.TorFailed)]
    [InlineData(TorProblem.Stopped, InternetRouteProblem.TorStopped)]
    [InlineData(TorProblem.None, InternetRouteProblem.CannotReachTor)]
    public void From_TorUnavailable_KeepsTheReason(TorProblem problem, InternetRouteProblem expected)
    {
        var status = InternetRouteStatus.From(new TorStatus(TorState.Unavailable, 10, problem, false), hasAnyNetwork: true);

        Assert.Equal(new InternetRouteStatus(InternetRouteState.Unavailable, 10, expected, false), status);
    }

    [Fact]
    public void NotInBuild_IsUnavailableForThatReason()
    {
        Assert.Equal(InternetRouteState.NotInBuild, InternetRouteStatus.NotInBuild.State);
        Assert.Equal(InternetRouteProblem.NotInBuild, InternetRouteStatus.NotInBuild.Problem);
        Assert.False(InternetRouteStatus.NotInBuild.IsAvailable);
    }
}
