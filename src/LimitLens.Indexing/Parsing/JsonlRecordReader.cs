using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace LimitLens.Indexing.Parsing;

public sealed record JsonlLine(string Text, long EndOffset);

public static class JsonlRecordReader
{
    private const int BufferSize = 64 * 1024;
    private const int MaximumLineBytes = 8 * 1024 * 1024;

    public static async IAsyncEnumerable<JsonlLine> ReadCompletedLinesAsync(
        string path,
        long startOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (startOffset < 0 || startOffset > stream.Length)
        {
            startOffset = 0;
        }

        stream.Seek(startOffset, SeekOrigin.Begin);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        using var line = new MemoryStream();
        var absolutePosition = startOffset;
        var oversized = false;
        var firstCompletedLine = startOffset == 0;

        try
        {
            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    yield break;
                }

                var segmentStart = 0;
                for (var index = 0; index < bytesRead; index++)
                {
                    if (buffer[index] != (byte)'\n')
                    {
                        continue;
                    }

                    var segmentLength = index - segmentStart;
                    if (!oversized && line.Length + segmentLength <= MaximumLineBytes)
                    {
                        line.Write(buffer, segmentStart, segmentLength);
                    }
                    else
                    {
                        oversized = true;
                    }

                    var endOffset = absolutePosition + index + 1;
                    if (!oversized)
                    {
                        var data = line.GetBuffer().AsSpan(0, checked((int)line.Length));
                        if (firstCompletedLine && data.StartsWith(Encoding.UTF8.Preamble))
                        {
                            data = data[Encoding.UTF8.Preamble.Length..];
                        }

                        if (data.Length > 0 && data[^1] == (byte)'\r')
                        {
                            data = data[..^1];
                        }

                        yield return new JsonlLine(Encoding.UTF8.GetString(data), endOffset);
                    }

                    firstCompletedLine = false;
                    line.SetLength(0);
                    oversized = false;
                    segmentStart = index + 1;
                }

                var remainderLength = bytesRead - segmentStart;
                if (remainderLength > 0)
                {
                    if (!oversized && line.Length + remainderLength <= MaximumLineBytes)
                    {
                        line.Write(buffer, segmentStart, remainderLength);
                    }
                    else
                    {
                        oversized = true;
                    }
                }

                absolutePosition += bytesRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
