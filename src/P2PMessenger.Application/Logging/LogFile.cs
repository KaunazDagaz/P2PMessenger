using System.Globalization;
using P2PMessenger.Core;

namespace P2PMessenger.Application.Logging;

internal sealed class LogFile : IDisposable
{
    private readonly LogFileOptions options;
    private readonly TimeProvider timeProvider;
    private readonly object gate = new();
    private FileStream? stream;
    private long size;
    private DateTimeOffset suspendedUntil = DateTimeOffset.MinValue;
    private bool disposed;

    public LogFile(LogFileOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.options = options;
        this.timeProvider = timeProvider;
    }

    public string PathOf(int number)
    {
        string name = number == 0
            ? options.FileName
            : Path.GetFileNameWithoutExtension(options.FileName) + "." + number.ToString(CultureInfo.InvariantCulture) + Path.GetExtension(options.FileName);
        return Path.Combine(options.Folder, name);
    }

    public void Append(ReadOnlySpan<byte> line)
    {
        lock (gate)
        {
            if (disposed || timeProvider.GetUtcNow() < suspendedUntil)
            {
                return;
            }

            try
            {
                FileStream open = Open();
                if (size > 0 && size + line.Length > options.MaxFileBytes)
                {
                    open = Roll();
                }

                open.Write(line);
                open.Flush();
                size += line.Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Close();
                suspendedUntil = timeProvider.GetUtcNow() + options.RetryAfterFailure;
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            Close();
        }
    }

    private FileStream Open()
    {
        if (stream is not null)
        {
            return stream;
        }

        CreateFolder();
        FileStreamOptions streamOptions = new()
        {
            Mode = FileMode.Append,
            Access = FileAccess.Write,
            Share = FileShare.Read,
        };
        if (!OperatingSystem.IsWindows())
        {
            streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        stream = new FileStream(PathOf(0), streamOptions);
        size = stream.Length;
        return stream;
    }

    private FileStream Roll()
    {
        Close();
        File.Delete(PathOf(options.MaxFiles - 1));
        for (int number = options.MaxFiles - 2; number >= 1; number--)
        {
            if (File.Exists(PathOf(number)))
            {
                File.Move(PathOf(number), PathOf(number + 1), true);
            }
        }

        if (options.MaxFiles > 1)
        {
            File.Move(PathOf(0), PathOf(1), true);
        }
        else
        {
            File.Delete(PathOf(0));
        }

        return Open();
    }

    private void CreateFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            _ = Directory.CreateDirectory(options.Folder);
            return;
        }

        _ = Directory.CreateDirectory(options.Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private void Close()
    {
        FileStream? open = stream;
        stream = null;
        size = 0;
        if (open is null)
        {
            return;
        }

        try
        {
            open.Dispose();
        }
        catch (Exception exception) when (Failures.IsRecoverable(exception))
        {
            return;
        }
    }
}
