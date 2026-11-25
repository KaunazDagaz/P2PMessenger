using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Logging;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class CommandFailureLogTests
{
    private const string Secret = "C:\\Users\\alice\\private\\contact-code.txt";

    [AvaloniaFact]
    public async Task AddContact_CodeFileThatThrows_ShowsTheMessageAndLogsTheTypeOfTheExceptionOnly()
    {
        var loggers = new CapturingLoggerFactory();
        await using var harness = await UiHarness.StartAsync("Alice", null, loggers);
        var page = harness.Shell.Pages.AddContact();
        harness.Shell.Push(page);
        var view = new AddContactView { DataContext = page };
        harness.Show(view);
        harness.Platform.OpenFailure = new IOException(Secret);

        await page.OpenFileCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Equal(UiText.AddContactFileUnreadable, UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        var entry = Assert.Single(loggers.Entries, static entry => entry.EventId.Name == "CommandFailed");
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("AddContactViewModel could not finish OpenFileAsync: System.IO.IOException", entry.Message);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(loggers.Entries, static entry => entry.Message.Contains(Secret, StringComparison.Ordinal));
        harness.Messenger.Identity.Dispose();
    }

    [Fact]
    public void FailureLog_Report_NamesThePageTheOperationAndTheTypeOfTheException()
    {
        var logger = new CapturingLogger();
        var log = new FailureLog(logger);

        log.Report(typeof(AddContactViewModel), "CheckAsync", new InvalidOperationException(Secret));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("AddContactViewModel could not finish CheckAsync: System.InvalidOperationException", entry.Message);
        Assert.DoesNotContain(Secret, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureLog_None_ReportsWithoutLogging() =>
        FailureLog.None.Report(typeof(AddContactViewModel), "CheckAsync", new InvalidOperationException(Secret));

    [Fact]
    public void FailureLog_NullArguments_Throw()
    {
        var log = FailureLog.None;
        var exception = new InvalidOperationException();

        Assert.Throws<ArgumentNullException>(() => new FailureLog(null!));
        Assert.Throws<ArgumentNullException>(() => log.Report(null!, "x", exception));
        Assert.Throws<ArgumentNullException>(() => log.Report(typeof(AddContactViewModel), null!, exception));
        Assert.Throws<ArgumentNullException>(() => log.Report(typeof(AddContactViewModel), "x", null!));
    }

    private sealed record Entry(LogLevel Level, EventId EventId, string Message, Exception? Exception);

    private sealed class CapturingLogger : ILogger
    {
        private readonly Lock gate = new();
        private readonly List<Entry> entries = [];

        public IReadOnlyList<Entry> Entries
        {
            get
            {
                lock (gate)
                {
                    return [.. entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (gate)
            {
                entries.Add(new Entry(logLevel, eventId, formatter(state, exception), exception));
            }
        }
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly CapturingLogger logger = new();

        public IReadOnlyList<Entry> Entries => logger.Entries;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => logger;

        public void Dispose()
        {
        }
    }
}
