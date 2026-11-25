using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Logging;

namespace P2PMessenger.Application.Tests.Logging;

public sealed class FileLoggerTests : IDisposable
{
    private const string Secret = "C:\\Users\\alice\\private\\contact-code.txt";

    private static readonly Action<ILogger, string, int, bool, Exception?> Sample = LoggerMessage.Define<string, int, bool>(
        LogLevel.Warning,
        new EventId(7, "Sample"),
        "Sample {Name} had {Count} items, ok: {Ok}");

    private static readonly Action<ILogger, int, Exception?> Failed = LoggerMessage.Define<int>(
        LogLevel.Error,
        new EventId(9, "Failed"),
        "Something went wrong {Attempt}");

    private static readonly Action<ILogger, long, ulong, double, TimeSpan, char, DayOfWeek, Exception?> Kinds = LoggerMessage.Define<long, ulong, double, TimeSpan, char, DayOfWeek>(
        LogLevel.Warning,
        new EventId(10, "Kinds"),
        "{{literal}} {Long} {Unsigned} {Real} {Span} {Letter} {Day}");

    private static readonly Action<ILogger, double, Exception?> NotANumber = LoggerMessage.Define<double>(
        LogLevel.Warning,
        new EventId(11, "NotANumber"),
        "Real {Real}");

    private static readonly Action<ILogger, string?, Exception?> Absent = LoggerMessage.Define<string?>(
        LogLevel.Warning,
        new EventId(12, "Absent"),
        "Name was {Name}");

    private static readonly Action<ILogger, IPEndPoint, byte[], Exception?> NotPlain = LoggerMessage.Define<IPEndPoint, byte[]>(
        LogLevel.Error,
        new EventId(8, "NotPlain"),
        "Reached {Endpoint} with {Key}");

    private readonly string folder = Path.Combine(Path.GetTempPath(), "p2pm-log-" + Guid.NewGuid().ToString("N"));
    private readonly SettableTime time = new();

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Log_Warning_WritesOneJsonLineWithTheTemplateTheMessageAndTheFields()
    {
        time.Now = new DateTimeOffset(2026, 11, 25, 12, 30, 5, 123, TimeSpan.Zero);
        using var loggers = new FileLoggerFactory(Options(), time);
        var logger = loggers.CreateLogger("Test.Category");

        Sample(logger, "inbox", 3, true, null);

        var line = Assert.Single(ReadLines(loggers.CurrentFile));
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        Assert.Equal("2026-11-25T12:30:05.123Z", root.GetProperty("time").GetString());
        Assert.Equal("Warning", root.GetProperty("level").GetString());
        Assert.Equal("Test.Category", root.GetProperty("category").GetString());
        Assert.Equal(7, root.GetProperty("eventId").GetInt32());
        Assert.Equal("Sample", root.GetProperty("event").GetString());
        Assert.Equal("Sample {Name} had {Count} items, ok: {Ok}", root.GetProperty("template").GetString());
        Assert.Equal("Sample inbox had 3 items, ok: true", root.GetProperty("message").GetString());
        var fields = root.GetProperty("fields");
        Assert.Equal("inbox", fields.GetProperty("Name").GetString());
        Assert.Equal(3, fields.GetProperty("Count").GetInt32());
        Assert.True(fields.GetProperty("Ok").GetBoolean());
        Assert.False(root.TryGetProperty("exception", out _));
    }

    [Fact]
    public void Log_BelowTheMinimum_WritesNothingAndCreatesNoFile()
    {
        using var loggers = new FileLoggerFactory(Options(LogLevel.Error), time);
        var logger = loggers.CreateLogger("Test");

        Sample(logger, "inbox", 1, true, null);

        Assert.False(logger.IsEnabled(LogLevel.Warning));
        Assert.False(File.Exists(loggers.CurrentFile));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void IsEnabled_LevelNone_IsFalse()
    {
        using var loggers = new FileLoggerFactory(Options(LogLevel.Trace), time);

        Assert.False(loggers.CreateLogger("Test").IsEnabled(LogLevel.None));
        Assert.True(loggers.CreateLogger("Test").IsEnabled(LogLevel.Trace));
        Assert.Null(loggers.CreateLogger("Test").BeginScope("scope"));
    }

    [Fact]
    public void Log_Exception_WritesItsTypeNameAndNeitherItsMessageNorItsStack()
    {
        using var loggers = new FileLoggerFactory(Options(), time);
        var logger = loggers.CreateLogger("Test");

        Failed(logger, 2, new InvalidOperationException(Secret));

        var text = ReadAll(loggers.CurrentFile);
        using var json = JsonDocument.Parse(text.TrimEnd());
        Assert.Equal("System.InvalidOperationException", json.RootElement.GetProperty("exception").GetString());
        Assert.DoesNotContain("alice", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private", text, StringComparison.Ordinal);
        Assert.DoesNotContain("at P2PMessenger", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_ValuesThatAreNotPlain_AreWrittenAsTheirTypeNameOnly()
    {
        using var loggers = new FileLoggerFactory(Options(), time);
        var logger = loggers.CreateLogger("Test");

        NotPlain(logger, new IPEndPoint(IPAddress.Parse("10.1.2.3"), 4567), [0xAB, 0xCD, 0xEF], null);

        var text = ReadAll(loggers.CurrentFile);
        using var json = JsonDocument.Parse(text.TrimEnd());
        Assert.Equal("<IPEndPoint>", json.RootElement.GetProperty("fields").GetProperty("Endpoint").GetString());
        Assert.Equal("<Byte[]>", json.RootElement.GetProperty("fields").GetProperty("Key").GetString());
        Assert.Equal("Reached <IPEndPoint> with <Byte[]>", json.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("10.1.2.3", text, StringComparison.Ordinal);
        Assert.DoesNotContain("4567", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ABCDEF", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Log_StateThatIsNotStructured_IsNeverFormatted()
    {
        using var loggers = new FileLoggerFactory(Options(), time);
        var logger = loggers.CreateLogger("Test");

        logger.Log(LogLevel.Error, new EventId(3), "secret state", null, static (state, _) => "secret formatter output " + state);

        var text = ReadAll(loggers.CurrentFile);
        using var json = JsonDocument.Parse(text.TrimEnd());
        Assert.Equal("<unstructured log entry>", json.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_LongText_IsTruncatedWithoutSplittingASurrogatePair()
    {
        using var loggers = new FileLoggerFactory(Options(), time);
        var logger = loggers.CreateLogger("Test");

        Sample(logger, new string('a', 1000), 1, true, null);
        Sample(logger, new string('b', 255) + "\U0001F600" + "tail", 1, true, null);

        var lines = ReadLines(loggers.CurrentFile);
        Assert.Equal(2, lines.Count);
        using var first = JsonDocument.Parse(lines[0]);
        using var second = JsonDocument.Parse(lines[1]);
        Assert.Equal(256, first.RootElement.GetProperty("fields").GetProperty("Name").GetString()!.Length);
        Assert.Equal(new string('b', 255), second.RootElement.GetProperty("fields").GetProperty("Name").GetString());
    }

    [Fact]
    public void Log_PastTheFileLimit_RollsTheFilesAndKeepsOnlyTheConfiguredNumber()
    {
        using var loggers = new FileLoggerFactory(Options(maxFileBytes: 600, maxFiles: 3), time);
        var logger = loggers.CreateLogger("Test");

        for (var count = 0; count < 30; count++)
        {
            Sample(logger, "inbox", count, true, null);
        }

        var current = loggers.CurrentFile;
        var first = Path.Combine(folder, "p2pmessenger.1.log");
        var second = Path.Combine(folder, "p2pmessenger.2.log");
        Assert.True(File.Exists(current));
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.False(File.Exists(Path.Combine(folder, "p2pmessenger.3.log")));
        Assert.All(new[] { current, first, second }, path => Assert.True(new FileInfo(path).Length <= 600));
        Assert.Contains("\"Count\":29", ReadAll(current), StringComparison.Ordinal);
        Assert.DoesNotContain("\"Count\":0}", string.Concat(ReadAll(current), ReadAll(first), ReadAll(second)), StringComparison.Ordinal);
    }

    [Fact]
    public void Log_ASingleFileAllowed_StartsAgainWhenItIsFull()
    {
        using var loggers = new FileLoggerFactory(Options(maxFileBytes: 400, maxFiles: 1), time);
        var logger = loggers.CreateLogger("Test");

        for (var count = 0; count < 10; count++)
        {
            Sample(logger, "inbox", count, true, null);
        }

        Assert.Equal(["p2pmessenger.log"], Directory.GetFiles(folder).Select(Path.GetFileName));
        Assert.Contains("\"Count\":9", ReadAll(loggers.CurrentFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Log_FromManyThreads_WritesOnlyCompleteLines()
    {
        using var loggers = new FileLoggerFactory(Options(maxFileBytes: 10 * 1024 * 1024), time);
        var logger = loggers.CreateLogger("Test");

        await Task.WhenAll(Enumerable.Range(0, 8).Select(thread => Task.Run(() =>
        {
            for (var count = 0; count < 100; count++)
            {
                Sample(logger, "thread" + thread, count, true, null);
            }
        }, TestContext.Current.CancellationToken)));

        var lines = ReadLines(loggers.CurrentFile);
        Assert.Equal(800, lines.Count);
        Assert.All(lines, static line =>
        {
            using var json = JsonDocument.Parse(line);
            Assert.Equal("Sample", json.RootElement.GetProperty("event").GetString());
        });
    }

    [Fact]
    public void Log_FolderThatCannotBeCreated_DoesNotThrowAndTriesAgainAfterTheWait()
    {
        Directory.CreateDirectory(folder);
        var blocker = Path.Combine(folder, "blocker");
        File.WriteAllText(blocker, "a file where a folder is needed");
        var options = new LogFileOptions
        {
            Folder = Path.Combine(blocker, "logs"),
            Minimum = LogLevel.Information,
            RetryAfterFailure = TimeSpan.FromSeconds(30),
        };
        time.Now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        using var loggers = new FileLoggerFactory(options, time);
        var logger = loggers.CreateLogger("Test");

        Sample(logger, "dropped", 1, true, null);
        File.Delete(blocker);
        time.Now += TimeSpan.FromSeconds(10);
        Sample(logger, "still-waiting", 2, true, null);
        Assert.False(File.Exists(loggers.CurrentFile));
        time.Now += TimeSpan.FromSeconds(21);
        Sample(logger, "written", 3, true, null);

        var text = ReadAll(loggers.CurrentFile);
        Assert.Contains("written", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dropped", text, StringComparison.Ordinal);
        Assert.DoesNotContain("still-waiting", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_AfterTheFactoryIsDisposed_WritesNothingAndDoesNotThrow()
    {
        var loggers = new FileLoggerFactory(Options(), time);
        var logger = loggers.CreateLogger("Test");
        Sample(logger, "before", 1, true, null);

        loggers.Dispose();
        Sample(logger, "after", 2, true, null);
        loggers.Dispose();

        var text = ReadAll(loggers.CurrentFile);
        Assert.Contains("before", text, StringComparison.Ordinal);
        Assert.DoesNotContain("after", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_ToAnExistingFile_AppendsToIt()
    {
        using (var first = new FileLoggerFactory(Options(), time))
        {
            Sample(first.CreateLogger("Test"), "first-run", 1, true, null);
        }

        using var second = new FileLoggerFactory(Options(), time);
        Sample(second.CreateLogger("Test"), "second-run", 2, true, null);

        var lines = ReadLines(second.CurrentFile);
        Assert.Equal(2, lines.Count);
        Assert.Contains("first-run", lines[0], StringComparison.Ordinal);
        Assert.Contains("second-run", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Log_NumbersCharactersEnumsAndDurations_KeepTheirJsonTypes()
    {
        using var loggers = new FileLoggerFactory(Options(), time);
        var logger = loggers.CreateLogger("Test");

        Kinds(logger, -5L, ulong.MaxValue, 1.5, TimeSpan.FromSeconds(1.5), 'x', DayOfWeek.Friday, null);
        NotANumber(logger, double.NaN, null);

        var lines = ReadLines(loggers.CurrentFile);
        using var kinds = JsonDocument.Parse(lines[0]);
        var fields = kinds.RootElement.GetProperty("fields");
        Assert.Equal("{literal} -5 18446744073709551615 1.5 1500 x Friday", kinds.RootElement.GetProperty("message").GetString());
        Assert.Equal(-5, fields.GetProperty("Long").GetInt64());
        Assert.Equal(ulong.MaxValue, fields.GetProperty("Unsigned").GetUInt64());
        Assert.Equal(1.5, fields.GetProperty("Real").GetDouble());
        Assert.Equal(1500, fields.GetProperty("Span").GetDouble());
        Assert.Equal("x", fields.GetProperty("Letter").GetString());
        Assert.Equal("Friday", fields.GetProperty("Day").GetString());
        using var notANumber = JsonDocument.Parse(lines[1]);
        Assert.Equal("NaN", notANumber.RootElement.GetProperty("fields").GetProperty("Real").GetString());
    }

    [Fact]
    public void Log_AbsentValue_IsNull()
    {
        using var loggers = new FileLoggerFactory(Options(), time);
        var logger = loggers.CreateLogger("Test");

        Absent(logger, null, null);

        using var json = JsonDocument.Parse(ReadAll(loggers.CurrentFile).TrimEnd());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("fields").GetProperty("Name").ValueKind);
        Assert.Equal("Name was null", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void AddProvider_IsNotSupported()
    {
        using var loggers = new FileLoggerFactory(Options(), time);

        Assert.Throws<NotSupportedException>(() => loggers.AddProvider(null!));
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Constructor_InvalidOptions_Throws(LogFileOptions options) =>
        Assert.Throws<ArgumentException>(() => new FileLoggerFactory(options, TimeProvider.System));

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new FileLoggerFactory(null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new FileLoggerFactory(Options(), null!));
    }

    public static TheoryData<LogFileOptions> InvalidOptions() =>
        new()
        {
            new LogFileOptions { Folder = " " },
            new LogFileOptions { Folder = "logs", FileName = "nested" + Path.DirectorySeparatorChar + "name.log" },
            new LogFileOptions { Folder = "logs", FileName = " " },
            new LogFileOptions { Folder = "logs", MaxFileBytes = 0 },
            new LogFileOptions { Folder = "logs", MaxFiles = 0 },
            new LogFileOptions { Folder = "logs", RetryAfterFailure = TimeSpan.FromSeconds(-1) },
        };

    private static List<string> ReadLines(string path) =>
        [.. ReadAll(path).Split('\n', StringSplitOptions.RemoveEmptyEntries)];

    private static string ReadAll(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private LogFileOptions Options(LogLevel minimum = LogLevel.Information, long maxFileBytes = LogFileOptions.DefaultMaxFileBytes, int maxFiles = LogFileOptions.DefaultMaxFiles) =>
        new()
        {
            Folder = folder,
            Minimum = minimum,
            MaxFileBytes = maxFileBytes,
            MaxFiles = maxFiles,
        };

    private sealed class SettableTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
