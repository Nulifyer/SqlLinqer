#nullable disable
// Benchmark-only control copied from core at 0b7d946 before JSON retirement.
// System.Text.Json performs parsing; this stream only adapts SQL Server fragments.
using System;
using System.Buffers;
using System.Data.Common;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NativeProbe.Control
{
    // Exposes SQL Server's text fragments as one UTF-8 document. A single encoder
    // preserves surrogate pairs across both text-read and database-row boundaries.
    // The caller owns the DbDataReader; this stream owns only its current TextReader.
    internal sealed class JsonFragmentStream : Stream
    {
        private readonly DbDataReader reader;
        private readonly char[] chars;
        private readonly byte[] bytes;
        private readonly Encoder encoder = new UTF8Encoding(false, true).GetEncoder();
        private TextReader fragment;
        private int position, length;
        private bool finished;
        private bool disposed;

        // The mapper has already advanced to the first transport row.
        internal JsonFragmentStream(DbDataReader reader)
        {
            this.reader = reader;
            fragment = reader.GetTextReader(0);
            chars = ArrayPool<char>.Shared.Rent(4096);
            try { bytes = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(chars.Length)); }
            catch { ArrayPool<char>.Shared.Return(chars, true); fragment.Dispose(); throw; }
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (disposed) throw new ObjectDisposedException(nameof(JsonFragmentStream));
            if (count == 0) return 0;
            cancellationToken.ThrowIfCancellationRequested();
            if (position == length)
            {
                if (finished) return 0;
                int filled = 0;
                // Coalesce small database fragments. Returning after every fragment
                // makes the serializer refill and resume once per database row.
                // Read-ahead remains bounded by this fixed character buffer.
                while (filled < chars.Length && !finished)
                {
                    if (fragment == null)
                    {
                        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        { finished = true; break; }
                        fragment = reader.GetTextReader(0);
                    }
                    int read = await fragment.ReadAsync(chars, filled, chars.Length - filled).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (read == 0) { fragment.Dispose(); fragment = null; }
                    else filled += read;
                }
                length = encoder.GetBytes(chars, 0, filled, bytes, 0, finished);
                position = 0;
            }
            int copied = Math.Min(count, length - position);
            Buffer.BlockCopy(bytes, position, buffer, offset, copied);
            position += copied;
            return copied;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                try { fragment?.Dispose(); }
                finally
                {
                    fragment = null;
                    ArrayPool<char>.Shared.Return(chars, true);
                    ArrayPool<byte>.Shared.Return(bytes, true);
                }
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }
}
