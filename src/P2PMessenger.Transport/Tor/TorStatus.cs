namespace P2PMessenger.Transport.Tor;

public sealed record TorStatus(TorState State, int Progress, TorProblem Problem, bool OnionServicePublished)
{
    public static TorStatus Starting(int progress) => new(TorState.Starting, progress, TorProblem.None, false);
}
