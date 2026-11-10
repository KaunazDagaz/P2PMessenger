using P2PMessenger.Spikes.Noise.Candidates;
using P2PMessenger.Spikes.Noise.Checks;
using P2PMessenger.Spikes.Noise.Own;

namespace P2PMessenger.Spikes.Noise;

public static class SelfTest
{
    public static int Run()
    {
        EnvironmentReport.Print();
        CheckReport report = new();
        INoiseCandidate own = new OwnCandidate();
        INoiseCandidate noiseNet = new NoiseNetCandidate();
        INoiseCandidate[] candidates = [own, noiseNet];

        CheckReport.Section("What NSec offers for Noise");
        CapabilityChecks.Run(report);

        CheckReport.Section("Fixed ephemeral keys, needed to replay the test vectors");
        foreach (INoiseCandidate candidate in candidates)
        {
            report.Run(
                candidate.Name,
                () => candidate.CanFixEphemeral(out string reason) ? CheckOutcome.Info("possible: " + reason) : CheckOutcome.Info("not possible: " + reason));
        }

        CheckReport.Section("Noise test vectors (cacophony and snow)");
        VectorChecks.Run(report, candidates);

        CheckReport.Section("Interoperability with random keys");
        InteropChecks.Run(report, own, noiseNet);

        CheckReport.Section("Rejections (Noise_KK_25519_ChaChaPoly_BLAKE2b)");
        foreach (INoiseCandidate candidate in candidates)
        {
            RejectionChecks.Run(report, candidate, NoiseProtocol.KkBlake2b);
        }

        CheckReport.Section("libsodium");
        LibsodiumChecks.Run(report);

        report.PrintSummary();
        return report.Failed == 0 ? 0 : 1;
    }
}
