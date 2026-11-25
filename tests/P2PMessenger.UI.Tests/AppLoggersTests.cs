using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Logging;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.Tests;

public sealed class AppLoggersTests : IDisposable
{
    private static readonly Action<ILogger, int, Exception?> Started = LoggerMessage.Define<int>(
        LogLevel.Information,
        new EventId(1, "Started"),
        "Started on port {Port}");

    private static readonly Action<ILogger, int, Exception?> Detail = LoggerMessage.Define<int>(
        LogLevel.Debug,
        new EventId(2, "Detail"),
        "A detail of connection {Number}");

    private readonly string folder = Path.Combine(Path.GetTempPath(), "p2pm-apploggers-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_PlatformWithoutADataFolder_OnlyTraces(bool debug)
    {
        using var loggers = AppLoggers.Create(AppPlatform.WithoutKeyStore, debug);

        Assert.IsType<TraceLoggerFactory>(loggers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_PlatformWithADataFolder_WritesInformationAndAboveToTheLogFile(bool debug)
    {
        var platform = new AppPlatform(folder, null);
        var file = Path.Combine(folder, AppPlatform.LogFolderName, LogFileOptions.DefaultFileName);

        using (var loggers = AppLoggers.Create(platform, debug))
        {
            var logger = loggers.CreateLogger("Test");
            Started(logger, 51234, null);
            Detail(logger, 7, null);
        }

        var text = File.ReadAllText(file);
        Assert.Contains("\"event\":\"Started\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Detail", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_NullPlatform_Throws() =>
        Assert.Throws<ArgumentNullException>(() => AppLoggers.Create(null!, false));
}
