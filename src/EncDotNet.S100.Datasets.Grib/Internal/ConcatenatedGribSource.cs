namespace EncDotNet.S100.Datasets.Grib.Internal;

/// <summary>
/// Several GRIB sources read as one, one after another, as if the files were
/// appended (#1075: a folder holding one model run, one file per forecast
/// hour). Safe from several threads when its parts are.
/// </summary>
internal sealed class ConcatenatedGribSource : GribSource
{
    private readonly GribSource[] _parts;
    private readonly long[] _starts;

    public ConcatenatedGribSource(IReadOnlyList<GribSource> parts)
    {
        _parts = [.. parts];
        _starts = new long[_parts.Length];
        long start = 0;
        for (int i = 0; i < _parts.Length; i++)
        {
            _starts[i] = start;
            start += _parts[i].Length;
        }

        Length = start;
    }

    public override long Length { get; }

    public override void ReadExactly(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > Length)
            throw Truncated();
        while (!buffer.IsEmpty)
        {
            var part = Array.BinarySearch(_starts, offset);
            if (part < 0)
                part = ~part - 1;
            // Skip empty parts that start at the same offset.
            while (part < _parts.Length - 1 && _starts[part + 1] <= offset)
                part++;
            var within = offset - _starts[part];
            var count = (int)Math.Min(buffer.Length, _parts[part].Length - within);
            _parts[part].ReadExactly(within, buffer[..count]);
            buffer = buffer[count..];
            offset += count;
        }
    }

    public override void Dispose()
    {
        foreach (var part in _parts)
            part.Dispose();
    }
}
