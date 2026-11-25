using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Errors;
using P2PMessenger.Core;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class GlobalErrorHandlerTests
{
    [AvaloniaFact]
    public async Task Handle_ExceptionOnTheUiThread_ShowsTheNoticeUntilDismissedAndLogsOnlyTheType()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        var log = new CapturingLogger();
        var handler = new GlobalErrorHandler(log, new AvaloniaUiThread(), harness.Shell.ShowUnexpectedError);
        Assert.False(UiHarness.Find<Border>(view, "ErrorNoticePanel").IsVisible);

        Assert.True(handler.Handle(new InvalidOperationException("Bob P2PM1-SECRET"), FailureSource.UiThread));
        UiHarness.Flush();

        Assert.True(UiHarness.Find<Border>(view, "ErrorNoticePanel").IsVisible);
        Assert.Equal(UiText.AppUnexpectedError, UiHarness.Find<TextBlock>(view, "ErrorNoticeText").Text);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("System.InvalidOperationException", entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(FailureSource.UiThread), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", entry.Message, StringComparison.Ordinal);
        Assert.Null(entry.Exception);

        harness.Shell.DismissErrorNoticeCommand.Execute(null);
        UiHarness.Flush();

        Assert.False(UiHarness.Find<Border>(view, "ErrorNoticePanel").IsVisible);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Handle_AggregateException_LogsTheTypeOfTheCause()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var log = new CapturingLogger();
        var handler = new GlobalErrorHandler(log, new AvaloniaUiThread(), harness.Shell.ShowUnexpectedError);

        Assert.True(handler.Handle(new AggregateException(new TimeoutException("Bob")), FailureSource.BackgroundTask));
        UiHarness.Flush();

        var entry = Assert.Single(log.Entries);
        Assert.Contains("System.TimeoutException", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Bob", entry.Message, StringComparison.Ordinal);
        Assert.NotNull(harness.Shell.ErrorNotice);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Handle_ExceptionWhileTheAppIsEnding_IsLoggedWithoutANotice()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var log = new CapturingLogger();
        var handler = new GlobalErrorHandler(log, new AvaloniaUiThread(), harness.Shell.ShowUnexpectedError);

        Assert.True(handler.Handle(new InvalidOperationException(), FailureSource.AppDomain));
        UiHarness.Flush();

        Assert.Single(log.Entries);
        Assert.Null(harness.Shell.ErrorNotice);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "The test checks that the handler leaves this exception alone.")]
    public async Task Handle_OutOfMemory_IsLeftToCrash()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var log = new CapturingLogger();
        var handler = new GlobalErrorHandler(log, new AvaloniaUiThread(), harness.Shell.ShowUnexpectedError);

        Assert.False(handler.Handle(new OutOfMemoryException(), FailureSource.UiThread));
        UiHarness.Flush();

        Assert.Empty(log.Entries);
        Assert.Null(harness.Shell.ErrorNotice);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Handle_TheSameExceptionAgainFromTheTaskScheduler_IsReportedOnce()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var log = new CapturingLogger();
        var handler = new GlobalErrorHandler(log, new AvaloniaUiThread(), harness.Shell.ShowUnexpectedError);
        var exception = new InvalidOperationException("Bob");
        Assert.True(handler.Handle(exception, FailureSource.UiThread));
        harness.Shell.DismissErrorNoticeCommand.Execute(null);

        Assert.True(handler.Handle(new AggregateException(exception), FailureSource.BackgroundTask));
        UiHarness.Flush();

        Assert.Single(log.Entries);
        Assert.Null(harness.Shell.ErrorNotice);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Install_CommandThatThrows_IsCaughtReportedOnceAndTheAppKeepsRunning()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        var log = new CapturingLogger();
        var handler = new GlobalErrorHandler(log, new AvaloniaUiThread(), harness.Shell.ShowUnexpectedError);
        handler.Install();
        try
        {
            var command = new AsyncRelayCommand(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("Bob P2PM1-SECRET");
            });

            command.Execute(null);
            await UiHarness.WaitUntilAsync(() => harness.Shell.HasErrorNotice);
            Assert.True(UiHarness.Find<Border>(view, "ErrorNoticePanel").IsVisible);
            harness.Shell.DismissErrorNoticeCommand.Execute(null);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            UiHarness.Flush();
        }
        finally
        {
            handler.Uninstall();
        }

        var entry = Assert.Single(log.Entries);
        Assert.Contains("System.InvalidOperationException", entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(FailureSource.UiThread), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", entry.Message, StringComparison.Ordinal);
        Assert.Null(harness.Shell.ErrorNotice);
        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaTheory]
    [InlineData(FailureSource.Connections)]
    [InlineData(FailureSource.Discovery)]
    [InlineData(FailureSource.Delivery)]
    [InlineData(FailureSource.AppDomain)]
    public async Task Handle_FailureReportedByAComponentOrTheEndOfTheApp_IsLoggedWithoutANotice(FailureSource source)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var log = new CapturingLogger();
        var failures = new GlobalFailureHandler(log);
        var handler = new GlobalErrorHandler(failures, new AvaloniaUiThread(), harness.Shell.ShowUnexpectedError);

        ((IFailureSink)failures).Report(source, new InvalidOperationException("Bob P2PM1-SECRET"));
        UiHarness.Flush();

        Assert.Null(harness.Shell.ErrorNotice);
        var entry = Assert.Single(log.Entries);
        Assert.Contains(source.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", entry.Message, StringComparison.Ordinal);
        Assert.NotNull(handler);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Handle_FailureReportedToTheBackEndHandler_ShowsTheNoticeThroughTheAdapterForABackgroundTask()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var failures = new GlobalFailureHandler(new CapturingLogger());
        var handler = new GlobalErrorHandler(failures, new AvaloniaUiThread(), harness.Shell.ShowUnexpectedError);
        Assert.Null(harness.Shell.ErrorNotice);

        ((IFailureSink)failures).Report(FailureSource.BackgroundTask, new InvalidOperationException("Bob"));
        UiHarness.Flush();

        Assert.NotNull(harness.Shell.ErrorNotice);
        Assert.Equal(1, failures.Count);
        Assert.NotNull(handler);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Install_InstallsTheBackEndHandlerToo_SoTheAdapterAndTheBackEndShareOneCount()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var failures = new GlobalFailureHandler(new CapturingLogger());
        var handler = new GlobalErrorHandler(failures, new AvaloniaUiThread(), harness.Shell.ShowUnexpectedError);

        Assert.True(handler.Handle(new InvalidOperationException("Bob"), FailureSource.UiThread));
        ((IFailureSink)failures).Report(FailureSource.Delivery, new IOException());
        UiHarness.Flush();

        Assert.Equal(2, failures.Count);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task ErrorNotice_WhileLocked_StaysHiddenUntilUnlocked()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        harness.Shell.ShowUnexpectedError();
        Assert.True(harness.Shell.HasErrorNotice);

        harness.Shell.LockPage = harness.Shell.Pages.Unlock(false, (_, _) => Task.FromResult(true));

        Assert.False(harness.Shell.HasErrorNotice);
        harness.Shell.LockPage = null;
        Assert.True(harness.Shell.HasErrorNotice);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-09")]
    public async Task SwitchTor_FailsWithAnUnexpectedException_ReachesTheGlobalHandlerAndFreesTheButton()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = new ContactListViewModel(
            harness.Messenger,
            harness.Services.UiThread,
            harness.Shell,
            harness.Shell.Pages,
            false,
            false,
            (_, _) => throw new InvalidOperationException("Bob"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => page.SwitchTorCommand.ExecuteAsync(null));

        Assert.False(page.IsSwitchingTor);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-09")]
    public async Task SwitchTor_AppIsQuittingWhileSwitching_IsNotAnError()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = new ContactListViewModel(
            harness.Messenger,
            harness.Services.UiThread,
            harness.Shell,
            harness.Shell.Pages,
            false,
            false,
            (_, _) => throw new ObjectDisposedException("route"));

        await page.SwitchTorCommand.ExecuteAsync(null);

        Assert.False(page.IsSwitchingTor);
        harness.Messenger.Identity.Dispose();
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
