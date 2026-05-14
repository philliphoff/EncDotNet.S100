namespace EncDotNet.S100.Core;

/// <summary>
/// A read-only, seekable <see cref="Stream"/> backed by a
/// <see cref="ReadOnlyMemory{Byte}"/>. Disposing the stream does not
/// release or copy the backing memory, which makes it safe for serving
/// cached <see cref="AssetBytes"/> repeatedly without allocations.
/// </summary>
internal sealed class ReadOnlyMemoryStream : Stream
{
    private readonly ReadOnlyMemory<byte> _memory;
    private long _position;

    public ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory)
    {
        _memory = memory;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _memory.Length;

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    public override void Flush()
    {
        // No-op: read-only stream has nothing to flush.
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        if (_position >= _memory.Length)
        {
            return 0;
        }

        int remaining = (int)Math.Min(buffer.Length, _memory.Length - _position);
        _memory.Span.Slice((int)_position, remaining).CopyTo(buffer);
        _position += remaining;
        return remaining;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<int>(cancellationToken);
        }

        try
        {
            return Task.FromResult(Read(buffer, offset, count));
        }
        catch (Exception ex)
        {
            return Task.FromException<int>(ex);
        }
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<int>(cancellationToken);
        }

        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override int ReadByte()
    {
        if (_position >= _memory.Length)
        {
            return -1;
        }

        byte value = _memory.Span[(int)_position];
        _position++;
        return value;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _memory.Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        ArgumentOutOfRangeException.ThrowIfNegative(target);
        _position = target;
        return _position;
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override void Write(ReadOnlySpan<byte> buffer) =>
        throw new NotSupportedException();
}
