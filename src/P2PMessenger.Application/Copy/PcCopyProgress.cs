namespace P2PMessenger.Application.Copy;

public readonly record struct PcCopyProgress(PcCopyReceivePhase Phase, int CompletedCount, int TotalCount);
