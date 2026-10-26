using Microsoft.Win32.SafeHandles;

namespace EncDotNet.S100.Datasets.Grib.Internal;

/// <summary>
/// Random-access reads of a GRIB file's bytes. Every implementation is safe
/// to read from several threads at once, since the weather model may decode
/// steps in parallel.
/// </summary>
internal abstract class GribSource : IDisposable
{
    public abstract long Length { get; }

    /// <summary>Fills <paramref name="buffer"/> from <paramref name="offset"/>.</summary>
    /// <exception cref="GribFormatException">The file ends first.</exception>
    public abstract void ReadExactly(long offset, Span<byte> buffer);

    public byte[] ReadBytes(long offset, long length)
    {
        if (length > Array.MaxLength)
            throw new UnsupportedGribException("A GRIB2 section is too large to read into memory.");
        var buffer = new byte[length];
        ReadExactly(offset, buffer);
        return buffer;
    }

    public abstract void Dispose();

    protected static GribFormatException Truncated() => new("The file ends in the middle of a GRIB message.");
}

/// <summary>A file read with <see cref="RandomAccess"/>, which needs no lock.</summary>
internal sealed class FileGribSource : GribSource
{
    private readonly SafeFileHandle _handle;

    public FileGribSource(string path)
    {
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        Length = RandomAccess.GetLength(_handle);
    }

    public override long Length { get; }

    public override void ReadExactly(long offset, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var read = RandomAccess.Read(_handle, buffer, offset);
            if (read == 0)
                throw Truncated();
            buffer = buffer[read..];
            offset += read;
        }
    }

    public override void Dispose() => _handle.Dispose();
}

/// <summary>A seekable stream, read under a lock.</summary>
internal sealed class StreamGribSource(Stream stream, bool leaveOpen) : GribSource
{
    private readonly object _gate = new();

    public override long Length { get; } = stream.Length;

    public override void ReadExactly(long offset, Span<byte> buffer)
    {
        lock (_gate)
        {
            stream.Position = offset;
            try
            {
                stream.ReadExactly(buffer);
            }
            catch (EndOfStreamException ex)
            {
                throw new GribFormatException("The stream ends in the middle of a GRIB message.", ex);
            }
        }
    }

    public override void Dispose()
    {
        if (!leaveOpen)
            stream.Dispose();
    }
}

/// <summary>Bytes already in memory.</summary>
internal sealed class MemoryGribSource(ReadOnlyMemory<byte> bytes) : GribSource
{
    public override long Length => bytes.Length;

    public override void ReadExactly(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > bytes.Length)
            throw Truncated();
        bytes.Span.Slice((int)offset, buffer.Length).CopyTo(buffer);
    }

    public override void Dispose()
    {
    }
}
