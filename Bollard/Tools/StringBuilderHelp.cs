using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bollard;

using System;
using System.Buffers;
using System.IO;
using System.Text;

public static class StringBuilderHelp {

    /// <summary>
    /// Efficiently encodes the StringBuilder to UTF8 and writes it to the stream.
    /// </summary>
    /// <param name="sb">The StringBuilder</param>
    /// <param name="stream">The stream to which it is written.</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <remarks>
    /// <para>Uses a buffer from an <see cref="ArrayPool"/> to improve performance with
    /// repeated calls.
    /// </para>
    /// <para>If, in the future, this is convertd to an Async version, be sure to use
    /// buffer.AsMemory() in the call to Stream.WriteAsync().
    /// </para>
    /// </remarks>
    public static void WriteUtf8(this StringBuilder sb, Stream stream) {
        if (sb is null)
            throw new ArgumentNullException(nameof(sb));
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));

        // One encoder instance to preserve state across chunk boundaries
        var encoder = Encoding.UTF8.GetEncoder(); // Does not geneerate a BOM

        // Rent a reusable byte buffer (size is a trade-off; 8–32 KB are common sweet spots)
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8 * 1024);

        try {
            foreach (var chunk in sb.GetChunks()) {
                ReadOnlySpan<char> span = chunk.Span;
                int charIndex = 0;

                while (charIndex < span.Length) {
                    encoder.Convert(
                        span.Slice(charIndex),      // input chars
                        buffer,                     // output bytes
                        flush: false,               // not final yet
                        out int charsUsed,
                        out int bytesUsed,
                        out _);

                    charIndex += charsUsed;

                    if (bytesUsed > 0) {
                        stream.Write(buffer, 0, bytesUsed);
                    }
                }
            }

            // Flush any remaining encoder state (e.g., dangling surrogate)
            encoder.Convert(
                ReadOnlySpan<char>.Empty,
                buffer,
                flush: true,
                out _,
                out int finalBytes,
                out _);

            if (finalBytes > 0) {
                stream.Write(buffer, 0, finalBytes);
            }
        }
        finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}