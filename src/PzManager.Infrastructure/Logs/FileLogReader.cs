using System.Runtime.CompilerServices;
using System.Text;
using PzManager.Application.Servers;

namespace PzManager.Infrastructure.Logs;

public static class FileLogReader
{
    public static IReadOnlyList<string> Tail(string path, int maxLines)
    {
        maxLines = maxLines <= 0 ? 200 : maxLines;
        if (maxLines > 2000)
        {
            maxLines = 2000;
        }

        var startOffset = FindTailStartOffset(path, maxLines);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (startOffset > fs.Length)
        {
            startOffset = fs.Length;
        }

        fs.Position = startOffset;

        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        if (lines.Count > maxLines)
        {
            return lines.TakeLast(maxLines).ToArray();
        }

        return lines;
    }

    public static long FindTailStartOffset(string path, int tailLines)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = fs.Length;
        if (length == 0)
        {
            return 0;
        }

        if (tailLines <= 0)
        {
            return length;
        }

        var buffer = new byte[8192];
        var pos = length;
        var newlineCount = 0;
        var skippingTrailingLineBreaks = true;

        while (pos > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, pos);
            pos -= toRead;
            fs.Position = pos;

            var read = fs.Read(buffer, 0, toRead);
            for (var i = read - 1; i >= 0; i--)
            {
                var b = buffer[i];

                if (skippingTrailingLineBreaks)
                {
                    if (b == (byte)'\n' || b == (byte)'\r')
                    {
                        continue;
                    }

                    skippingTrailingLineBreaks = false;
                }

                if (b != (byte)'\n')
                {
                    continue;
                }

                newlineCount++;
                if (newlineCount == tailLines)
                {
                    return pos + i + 1;
                }
            }
        }

        return 0;
    }

    public static async IAsyncEnumerable<ServerLogLine> FollowAsync(
        string path,
        long startOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (startOffset < 0)
        {
            startOffset = 0;
        }

        if (startOffset > fs.Length)
        {
            startOffset = fs.Length;
        }

        fs.Position = startOffset;

        var buffer = new byte[4096];
        var bufferCount = 0;
        var bufferIndex = 0;
        var bufferStartOffset = fs.Position;

        using var lineBuffer = new MemoryStream();

        async Task<int> FillAsync()
        {
            var start = fs.Position;
            var read = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            bufferStartOffset = start;
            bufferCount = read;
            bufferIndex = 0;
            return read;
        }

        await FillAsync();

        while (!cancellationToken.IsCancellationRequested)
        {
            var newlinePos = -1;
            for (var i = bufferIndex; i < bufferCount; i++)
            {
                if (buffer[i] == (byte)'\n')
                {
                    newlinePos = i;
                    break;
                }
            }

            if (newlinePos >= 0)
            {
                lineBuffer.Write(buffer, bufferIndex, newlinePos - bufferIndex);
                bufferIndex = newlinePos + 1;

                if (lineBuffer.Length > 0)
                {
                    var raw = lineBuffer.GetBuffer();
                    if (raw[(int)lineBuffer.Length - 1] == (byte)'\r')
                    {
                        lineBuffer.SetLength(lineBuffer.Length - 1);
                    }
                }

                var text = Encoding.UTF8.GetString(lineBuffer.GetBuffer(), 0, (int)lineBuffer.Length);
                lineBuffer.SetLength(0);

                var endOffset = bufferStartOffset + newlinePos + 1;
                yield return new ServerLogLine(text, TimestampUtc: null, Offset: endOffset);
                continue;
            }

            if (bufferIndex < bufferCount)
            {
                lineBuffer.Write(buffer, bufferIndex, bufferCount - bufferIndex);
                bufferIndex = bufferCount;
            }

            if (await FillAsync() > 0)
            {
                continue;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);

            try
            {
                if (fs.Position > fs.Length)
                {
                    fs.Position = 0;
                    lineBuffer.SetLength(0);
                    bufferCount = 0;
                    bufferIndex = 0;
                    bufferStartOffset = 0;
                }
            }
            catch
            {
                // Best-effort.
            }
        }
    }
}
