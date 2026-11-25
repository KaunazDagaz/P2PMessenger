using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Logging;

namespace P2PMessenger.UI.Services;

public static class AppLoggers
{
    public static ILoggerFactory Create(AppPlatform platform, bool debug)
    {
        ArgumentNullException.ThrowIfNull(platform);

        TraceLoggerFactory trace = new(debug ? LogLevel.Information : LogLevel.Error);
        if (platform.LogFolder is not { } folder)
        {
            return trace;
        }

        return new TeeLoggerFactory(trace, new FileLoggerFactory(new LogFileOptions { Folder = folder }, TimeProvider.System));
    }
}
