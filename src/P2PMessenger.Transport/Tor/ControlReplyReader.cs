using System.Text;

namespace P2PMessenger.Transport.Tor;

internal sealed class ControlReplyReader
{
    private const int BufferLength = 2 * (ControlLimits.MaxLineLength + 2);

    private readonly Stream stream;
    private readonly byte[] buffer = new byte[BufferLength];
    private int start;
    private int end;

    public ControlReplyReader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        this.stream = stream;
    }

    public async ValueTask<ControlReply?> ReadAsync(CancellationToken cancellationToken)
    {
        List<ControlReplyLine> lines = [];
        int status = 0;
        int dataLength = 0;
        while (true)
        {
            string? line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                if (lines.Count == 0)
                {
                    return null;
                }

                throw new TorControlException("Tor closed the control connection inside a reply.");
            }

            if (!TryParseStatus(line, out int lineStatus) || (lines.Count > 0 && lineStatus != status))
            {
                throw new TorControlException("Tor sent a malformed control reply line.");
            }

            status = lineStatus;
            char separator = line[3];
            string? data = null;
            if (separator == '+')
            {
                data = await ReadDataAsync(dataLength, cancellationToken).ConfigureAwait(false);
                dataLength += data.Length;
            }
            else if (separator is not '-' and not ' ')
            {
                throw new TorControlException("Tor sent a control reply line with an unknown separator.");
            }

            if (lines.Count == ControlLimits.MaxLinesPerReply)
            {
                throw new TorControlException("Tor sent a control reply with too many lines.");
            }

            lines.Add(new ControlReplyLine(line[4..], data));
            if (separator == ' ')
            {
                return new ControlReply(status, lines);
            }
        }
    }

    private static bool TryParseStatus(string line, out int status)
    {
        status = 0;
        if (line.Length < 4)
        {
            return false;
        }

        for (int index = 0; index < 3; index++)
        {
            char digit = line[index];
            if (digit is < '0' or > '9')
            {
                return false;
            }

            status = (status * 10) + (digit - '0');
        }

        return status is >= 200 and <= 699;
    }

    private static bool IsAllowed(byte value) => value is (>= 0x20 and <= 0x7E) or 0x09;

    private async ValueTask<string> ReadDataAsync(int alreadyRead, CancellationToken cancellationToken)
    {
        StringBuilder builder = new();
        int total = alreadyRead;
        while (true)
        {
            string line = await ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new TorControlException("Tor closed the control connection inside a data reply.");

            if (line == ".")
            {
                return builder.ToString();
            }

            ReadOnlySpan<char> content = line.StartsWith('.') ? line.AsSpan(1) : line.AsSpan();
            total += content.Length + 1;
            if (total > ControlLimits.MaxDataLength)
            {
                throw new TorControlException("Tor sent a data reply above the size limit.");
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(content);
        }
    }

    private async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            int newline = Array.IndexOf(buffer, (byte)'\n', start, end - start);
            if (newline >= 0)
            {
                int lineEnd = newline > start && buffer[newline - 1] == '\r' ? newline - 1 : newline;
                int length = lineEnd - start;
                if (length > ControlLimits.MaxLineLength)
                {
                    throw new TorControlException("Tor sent a control line above the size limit.");
                }

                for (int index = start; index < lineEnd; index++)
                {
                    if (!IsAllowed(buffer[index]))
                    {
                        throw new TorControlException("Tor sent a control line with a character that is not printable ASCII.");
                    }
                }

                string line = Encoding.ASCII.GetString(buffer, start, length);
                start = newline + 1;
                return line;
            }

            if (end - start > ControlLimits.MaxLineLength + 1)
            {
                throw new TorControlException("Tor sent a control line above the size limit.");
            }

            if (start > 0)
            {
                Buffer.BlockCopy(buffer, start, buffer, 0, end - start);
                end -= start;
                start = 0;
            }

            int read = await stream.ReadAsync(buffer.AsMemory(end, buffer.Length - end), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (end == start)
                {
                    return null;
                }

                throw new TorControlException("Tor closed the control connection inside a line.");
            }

            end += read;
        }
    }
}
