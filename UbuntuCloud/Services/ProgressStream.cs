namespace UbuntuCloud.Services
{
    public class ProgressStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _totalBytes;
        private long _bytesRead;
        private readonly Action<long, long> _onProgress;

        public ProgressStream(Stream baseStream, long totalBytes, Action<long, long> onProgress)
        {
            _baseStream = baseStream;
            _totalBytes = totalBytes;
            _onProgress = onProgress;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await _baseStream.ReadAsync(buffer, offset, count, cancellationToken);
            _bytesRead += read;
            _onProgress?.Invoke(_bytesRead, _totalBytes);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await _baseStream.ReadAsync(buffer, cancellationToken);
            _bytesRead += read;
            _onProgress?.Invoke(_bytesRead, _totalBytes);
            return read;
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => _baseStream.CanSeek;
        public override bool CanWrite => _baseStream.CanWrite;
        public override long Length => _baseStream.Length;
        public override long Position { get => _baseStream.Position; set => _baseStream.Position = value; }
        public override void Flush() => _baseStream.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _baseStream.Read(buffer, offset, count);
            _bytesRead += read;
            _onProgress?.Invoke(_bytesRead, _totalBytes);
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => _baseStream.Seek(offset, origin);
        public override void SetLength(long value) => _baseStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _baseStream.Write(buffer, offset, count);
    }
}