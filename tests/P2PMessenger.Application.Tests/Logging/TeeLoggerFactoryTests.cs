using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Logging;

namespace P2PMessenger.Application.Tests.Logging;

public sealed class TeeLoggerFactoryTests
{
    [Fact]
    public void Log_ReachesEveryLoggerThatIsEnabledForTheLevel()
    {
        var verbose = new RecordingFactory(LogLevel.Information);
        var quiet = new RecordingFactory(LogLevel.Error);
        using var tee = new TeeLoggerFactory(verbose, quiet);
        var logger = tee.CreateLogger("Test");

        logger.Log(LogLevel.Warning, default, "a warning", null, static (state, _) => state);
        logger.Log(LogLevel.Error, default, "an error", null, static (state, _) => state);

        Assert.Equal(["Warning", "Error"], verbose.Entries);
        Assert.Equal(["Error"], quiet.Entries);
    }

    [Fact]
    public void IsEnabled_WhenAnyLoggerIsEnabled_IsTrue()
    {
        using var tee = new TeeLoggerFactory(new RecordingFactory(LogLevel.Error), new RecordingFactory(LogLevel.Warning));
        var logger = tee.CreateLogger("Test");

        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.Null(logger.BeginScope("scope"));
    }

    [Fact]
    public void CreateLogger_AsksEveryFactoryForTheSameCategory()
    {
        var first = new RecordingFactory(LogLevel.Error);
        var second = new RecordingFactory(LogLevel.Error);
        using var tee = new TeeLoggerFactory(first, second);

        tee.CreateLogger("Some.Category");

        Assert.Equal(["Some.Category"], first.Categories);
        Assert.Equal(["Some.Category"], second.Categories);
    }

    [Fact]
    public void Dispose_DisposesEveryFactory()
    {
        var first = new RecordingFactory(LogLevel.Error);
        var second = new RecordingFactory(LogLevel.Error);
        var tee = new TeeLoggerFactory(first, second);

        tee.Dispose();

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public void AddProvider_IsNotSupported()
    {
        using var tee = new TeeLoggerFactory(new RecordingFactory(LogLevel.Error));

        Assert.Throws<NotSupportedException>(() => tee.AddProvider(null!));
    }

    [Fact]
    public void Constructor_NoFactoryOrANullOne_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TeeLoggerFactory(null!));
        Assert.Throws<ArgumentException>(() => new TeeLoggerFactory());
        Assert.Throws<ArgumentException>(() => new TeeLoggerFactory(new RecordingFactory(LogLevel.Error), null!));
    }

    private sealed class RecordingFactory(LogLevel minimum) : ILoggerFactory
    {
        public List<string> Entries { get; } = [];

        public List<string> Categories { get; } = [];

        public bool Disposed { get; private set; }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            Categories.Add(categoryName);
            return new RecordingLogger(this, minimum);
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class RecordingLogger(RecordingFactory factory, LogLevel minimum) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            factory.Entries.Add(logLevel.ToString());
    }
}
