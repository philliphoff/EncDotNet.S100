using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A content fingerprint of a bound <see cref="VectorScene"/>, folded into the
/// warm tile disk-cache namespace so two scenes that draw different content can
/// never share tiles.
/// </summary>
/// <remarks>
/// <para>The <c>styleStateHash</c> hashes the drawing instructions, which carry
/// feature references and styles but not feature geometry. Two datasets of one
/// product with structurally identical instructions (for example S-421 routes
/// from one producer with the same feature ids and leg count but different
/// waypoints), or a re-issued dataset whose geometry moved, would otherwise
/// share a namespace, and tiles keyed only by <c>(band, x, y, scale class)</c>
/// would cross-serve between them.</para>
/// <para>The fingerprint hashes, per op, its kind, SCAMIN bounds and EPSG:3857
/// coordinates, plus label text and pattern names, and the scene's
/// out-of-band cap. Colours, widths and symbols are left to the
/// <c>styleStateHash</c>. Coordinates are appended straight from their backing
/// arrays where possible, so binding a large S-101 cell costs one pass of
/// SHA-256 over its vertex data.</para>
/// </remarks>
internal static class VectorSceneFingerprint
{
    private const int BufferSize = 64 * 1024;

    /// <summary>Returns a lowercase hex SHA-256 fingerprint of <paramref name="scene"/>'s content.</summary>
    public static string Compute(VectorScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var writer = new Writer(BufferSize);
        try
        {
            writer.WriteDouble(scene.ScaleMinimumCap ?? double.NaN);
            writer.WriteInt(scene.Ops.Count);

            foreach (var op in scene.Ops)
            {
                switch (op)
                {
                    case AreaPaintOp area:
                        writer.WriteHeader(1, op);
                        writer.WriteRing(area.WorldShell);
                        writer.WriteRings(area.WorldHoles);
                        break;
                    case PatternAreaPaintOp pattern:
                        writer.WriteHeader(2, op);
                        writer.WriteString(pattern.PatternReference);
                        writer.WriteRing(pattern.WorldShell);
                        writer.WriteRings(pattern.WorldHoles);
                        break;
                    case LinePaintOp line:
                        writer.WriteHeader(3, op);
                        writer.WriteRing(line.World);
                        break;
                    case PointPaintOp point:
                        writer.WriteHeader(4, op);
                        writer.WriteDouble(point.World.X);
                        writer.WriteDouble(point.World.Y);
                        break;
                    case TextPaintOp text:
                        writer.WriteHeader(5, op);
                        writer.WriteDouble(text.World.X);
                        writer.WriteDouble(text.World.Y);
                        writer.WriteString(text.Text);
                        break;
                    default:
                        writer.WriteHeader(0, op);
                        writer.WriteString(op.GetType().FullName ?? string.Empty);
                        break;
                }
            }

            return writer.Finish();
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>
    /// Buffers small writes and flushes them to an incremental SHA-256 in
    /// large blocks, so the per-op overhead stays a few memory copies.
    /// </summary>
    private struct Writer : IDisposable
    {
        private readonly IncrementalHash _hash;
        private readonly byte[] _buffer;
        private int _length;

        public Writer(int size)
        {
            _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            _buffer = ArrayPool<byte>.Shared.Rent(size);
            _length = 0;
        }

        public void WriteHeader(byte kind, PaintOp op)
        {
            Ensure(1 + 2 * sizeof(double));
            _buffer[_length++] = kind;
            WriteDouble(op.ScaleMinimum ?? double.NaN);
            WriteDouble(op.ScaleMaximum ?? double.NaN);
        }

        public void WriteInt(int value)
        {
            Ensure(sizeof(int));
            Unsafe.WriteUnaligned(ref _buffer[_length], value);
            _length += sizeof(int);
        }

        public void WriteDouble(double value)
        {
            Ensure(sizeof(double));
            Unsafe.WriteUnaligned(ref _buffer[_length], value);
            _length += sizeof(double);
        }

        public void WriteString(string value)
        {
            WriteInt(value.Length);
            WriteBytes(MemoryMarshal.AsBytes(value.AsSpan()));
        }

        public void WriteRings(IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings)
        {
            WriteInt(rings.Count);
            foreach (var ring in rings)
                WriteRing(ring);
        }

        public void WriteRing(IReadOnlyList<(double X, double Y)> ring)
        {
            WriteInt(ring.Count);
            switch (ring)
            {
                case (double X, double Y)[] array:
                    WriteBytes(MemoryMarshal.AsBytes(array.AsSpan()));
                    break;
                case List<(double X, double Y)> list:
                    WriteBytes(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(list)));
                    break;
                default:
                    foreach (var (x, y) in ring)
                    {
                        WriteDouble(x);
                        WriteDouble(y);
                    }
                    break;
            }
        }

        public string Finish()
        {
            Flush();
            return Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
        }

        public void Dispose()
        {
            _hash.Dispose();
            ArrayPool<byte>.Shared.Return(_buffer);
        }

        private void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            // Large runs bypass the buffer; small ones coalesce with their
            // neighbours so the hash sees few, large appends.
            if (bytes.Length >= _buffer.Length / 4)
            {
                Flush();
                _hash.AppendData(bytes);
                return;
            }

            Ensure(bytes.Length);
            bytes.CopyTo(_buffer.AsSpan(_length));
            _length += bytes.Length;
        }

        private void Ensure(int bytes)
        {
            if (_length + bytes > _buffer.Length)
                Flush();
        }

        private void Flush()
        {
            if (_length == 0)
                return;
            _hash.AppendData(_buffer, 0, _length);
            _length = 0;
        }
    }
}
