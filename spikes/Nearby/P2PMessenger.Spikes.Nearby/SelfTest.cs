using P2PMessenger.Spikes.Nearby.Checks;
using P2PMessenger.Spikes.Nearby.Identification;
using P2PMessenger.Spikes.Nearby.Net;

namespace P2PMessenger.Spikes.Nearby;

public static class SelfTest
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        EnvironmentReport.Print();
        CheckReport report = new();

        IdentificationChecks.Run(report);
        AnnouncementChecks.Run(report);
        NetChecks.Run(report);
        await SecureChannelChecks.RunAsync(report, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        report.PrintSummary();
        return report.Failed == 0 ? 0 : 1;
    }
}
