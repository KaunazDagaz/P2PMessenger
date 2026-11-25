using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Errors;
using P2PMessenger.Core;
using P2PMessenger.Tests.Shared;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Application.Tests.Errors;

public sealed class GlobalFailureHandlerTests
{
    private const string Secret = "Bob P2PM1-SECRET";

    [Fact]
    public void Handle_Exception_LogsOnlyTheTypeAndTheSourceAndRaisesTheEvent()
    {
        var log = new CapturingLogger();
        var handler = new GlobalFailureHandler(log);
        var reports = new List<FailureReport>();
        handler.FailureReported += (_, report) => reports.Add(report);

        Assert.True(handler.Handle(new InvalidOperationException(Secret), FailureSource.Connections));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("UnexpectedFailure", entry.Id.Name);
        Assert.Equal("Unexpected failure of type System.InvalidOperationException in Connections", entry.Message);
        Assert.Null(entry.Exception);
        Assert.Equal(new FailureReport(FailureSource.Connections, "System.InvalidOperationException"), Assert.Single(reports));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public void Report_ThroughTheSink_IsHandledLikeHandle()
    {
        var log = new CapturingLogger();
        var handler = new GlobalFailureHandler(log);

        ((IFailureSink)handler).Report(FailureSource.Delivery, new TimeoutException(Secret));

        Assert.Contains("System.TimeoutException in Delivery", Assert.Single(log.Entries).Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public void Handle_AggregateException_ReportsTheTypeOfTheCause()
    {
        var log = new CapturingLogger();
        var handler = new GlobalFailureHandler(log);

        Assert.True(handler.Handle(new AggregateException(new TimeoutException(Secret)), FailureSource.BackgroundTask));

        var message = Assert.Single(log.Entries).Message;
        Assert.Contains("System.TimeoutException", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Aggregate", message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Handle_TheSameExceptionAgainInsideAnAggregate_IsReportedOnce()
    {
        var log = new CapturingLogger();
        var handler = new GlobalFailureHandler(log);
        var reports = 0;
        handler.FailureReported += (_, _) => reports++;
        var exception = new InvalidOperationException(Secret);

        Assert.True(handler.Handle(exception, FailureSource.UiThread));
        Assert.True(handler.Handle(new AggregateException(exception), FailureSource.BackgroundTask));

        Assert.Single(log.Entries);
        Assert.Equal(1, reports);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "The test checks that the handler leaves this exception alone.")]
    public void Handle_OutOfMemory_IsNotHandledAndNotReported()
    {
        var log = new CapturingLogger();
        var handler = new GlobalFailureHandler(log);
        var reports = 0;
        handler.FailureReported += (_, _) => reports++;

        Assert.False(handler.Handle(new OutOfMemoryException(), FailureSource.UiThread));

        Assert.Empty(log.Entries);
        Assert.Equal(0, reports);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public void Handle_SubscriberThatThrows_DoesNotStopTheOthersAndIsLoggedByTypeOnly()
    {
        var log = new CapturingLogger();
        var handler = new GlobalFailureHandler(log);
        var reached = false;
        handler.FailureReported += static (_, _) => throw new InvalidOperationException(Secret);
        handler.FailureReported += (_, _) => reached = true;

        var handled = handler.Handle(new IOException(), FailureSource.Discovery);

        Assert.True(handled);
        Assert.True(reached);
        Assert.Equal(2, log.Entries.Count);
        var subscriber = Assert.Single(log.Entries, static entry => entry.Id.Name == "SubscriberFailed");
        Assert.Equal(LogLevel.Warning, subscriber.Level);
        Assert.Equal("A subscriber of a failure report threw System.InvalidOperationException", subscriber.Message);
    }

    [Fact(Timeout = 60_000)]
    public async Task Install_FailedTaskThatNobodyObserved_IsReportedOnceAsABackgroundTask()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new GlobalFailureHandler(new CapturingLogger());
        var reports = new ConcurrentQueue<FailureReport>();
        handler.FailureReported += (_, report) => reports.Enqueue(report);
        var marker = typeof(MarkerException).FullName;
        handler.Install();
        handler.Install();
        try
        {
            FailAndForget();
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!reports.Any(report => report.ExceptionType == marker) && DateTime.UtcNow < deadline)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(50, cancellationToken);
            }
        }
        finally
        {
            handler.Uninstall();
            handler.Uninstall();
        }

        var report = Assert.Single(reports, report => report.ExceptionType == marker);
        Assert.Equal(FailureSource.BackgroundTask, report.Source);
    }

    [Fact]
    public async Task StartAsync_DiscoveryThatFails_ReportsToTheSinkOfTheSetup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sink = new RecordingFailureSink();
        var hub = new LoopbackDiscovery();
        using var identity = TestKeys.NewIdentity("Alice");
        var setup = MessengerSetup.InMemory() with
        {
            ListenAddress = IPAddress.Loopback,
            ListenPort = 0,
            Discovery = hub.CreateNode().Setup with { Interfaces = new FailingInterfaces() },
            Failures = sink,
        };

        await using var messenger = await Messenger.StartAsync(identity, setup, cancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (sink.Reports.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, cancellationToken);
        }

        var report = Assert.Single(sink.Reports.Take(1));
        Assert.Equal(FailureSource.Discovery, report.Source);
        Assert.IsType<InvalidOperationException>(report.Exception);
    }

    [Fact]
    public void Arguments_Null_Throw()
    {
        var handler = new GlobalFailureHandler(new CapturingLogger());

        Assert.Throws<ArgumentNullException>(() => new GlobalFailureHandler(null!));
        Assert.Throws<ArgumentNullException>(() => handler.Handle(null!, FailureSource.UiThread));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FailAndForget()
    {
        var task = Task.Run((Action)(static () => throw new MarkerException()));
        ((IAsyncResult)task).AsyncWaitHandle.WaitOne();
    }

    private sealed class MarkerException : Exception;

    private sealed class FailingInterfaces : ILocalInterfaceSource
    {
        public NetworkSnapshot Read() => throw new InvalidOperationException("The platform list failed.");
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly Lock gate = new();
        private readonly List<(LogLevel Level, EventId Id, string Message, Exception? Exception)> entries = [];

        public IReadOnlyList<(LogLevel Level, EventId Id, string Message, Exception? Exception)> Entries
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
                entries.Add((logLevel, eventId, formatter(state, exception), exception));
            }
        }
    }
}
