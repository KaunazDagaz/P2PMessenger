using System.ComponentModel;
using System.Diagnostics;

namespace P2PMessenger.Transport.Tor;

internal sealed class SystemTorProcess : ITorProcess
{
    private readonly Process process;

    private SystemTorProcess(Process process)
    {
        this.process = process;
    }

    public bool HasExited
    {
        get
        {
            try
            {
                return process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public int? ExitCode => HasExited ? SafeExitCode() : null;

    public static SystemTorProcess Start(TorLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ProcessStartInfo startInfo = new(launch.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = launch.DataFolder,
        };
        foreach (string argument in launch.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach ((string name, string value) in launch.Environment)
        {
            startInfo.Environment[name] = value;
        }

        Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new TorControlException("tor did not start.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            process.Dispose();
            throw new TorControlException("tor could not be started.", exception);
        }

        return new SystemTorProcess(process);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);

    public void Kill()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }

    public void Dispose() => process.Dispose();

    private int? SafeExitCode()
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
