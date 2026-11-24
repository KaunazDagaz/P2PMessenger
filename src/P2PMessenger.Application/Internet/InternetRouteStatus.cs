using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Application.Internet;

public sealed record InternetRouteStatus(InternetRouteState State, int Progress, InternetRouteProblem Problem, bool OnionServicePublished)
{
    public static InternetRouteStatus NotInBuild { get; } = new(InternetRouteState.NotInBuild, 0, InternetRouteProblem.NotInBuild, false);

    public static InternetRouteStatus Off { get; } = new(InternetRouteState.Off, 0, InternetRouteProblem.None, false);

    public bool IsAvailable => State == InternetRouteState.Connected;

    public static InternetRouteStatus From(TorStatus tor, bool hasAnyNetwork)
    {
        ArgumentNullException.ThrowIfNull(tor);
        if (!hasAnyNetwork)
        {
            return new InternetRouteStatus(InternetRouteState.Unavailable, tor.Progress, InternetRouteProblem.NoNetwork, tor.OnionServicePublished);
        }

        if (tor.State == TorState.Connected)
        {
            return new InternetRouteStatus(InternetRouteState.Connected, 100, InternetRouteProblem.None, tor.OnionServicePublished);
        }

        if (tor.State == TorState.Starting)
        {
            return new InternetRouteStatus(InternetRouteState.Starting, tor.Progress, InternetRouteProblem.None, tor.OnionServicePublished);
        }

        InternetRouteProblem problem = tor.Problem switch
        {
            TorProblem.ClockWrong => InternetRouteProblem.ClockWrong,
            TorProblem.StartFailed => InternetRouteProblem.TorFailed,
            TorProblem.Stopped => InternetRouteProblem.TorStopped,
            _ => InternetRouteProblem.CannotReachTor,
        };
        return new InternetRouteStatus(InternetRouteState.Unavailable, tor.Progress, problem, tor.OnionServicePublished);
    }
}
