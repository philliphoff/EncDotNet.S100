using System.Globalization;
using EncDotNet.S100.Datasets.Grib.Internal;
using EncDotNet.S100.Datasets.Weather;

namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// A GRIB2 file opened for reading: its fields, indexed when it opens, and
/// their values, decoded on demand.
/// </summary>
/// <remarks>
/// <para>
/// Opening reads Section 0 of each message and sections 1, 3, 4 and 5 of
/// each field, and skips the bitmaps and data, so a 500 MB GFS file indexes
/// in a few thousand small reads. <see cref="ReadValues"/> then reads one
/// field's bitmap and data section and unpacks them. The file stays open
/// until <see cref="Dispose"/>, and reads are safe from several threads at
/// once.
/// </para>
/// <para>
/// Supported: grid template 3.0 (regular latitude/longitude, any scanning
/// mode except staggered), product templates 4.0 and 4.8, and data templates
/// 5.0 (simple), 5.2 (complex), 5.3 (complex with spatial differencing of
/// order 1 or 2), 5.40 (JPEG 2000) and 5.41 (PNG), with or without a bitmap.
/// Fields using any other template are listed with
/// <see cref="GribField.UnsupportedReason"/> set, and reading them throws
/// <see cref="UnsupportedGribException"/> naming the template: notably 5.42
/// (CCSDS). A GRIB
/// edition 1 message can't be indexed at all, so opening a file that holds
/// one throws.
/// </para>
/// </remarks>
public sealed class GribFile : IDisposable
{
    private readonly GribSource _source;
    private bool _disposed;

    private GribFile(GribSource source)
    {
        _source = source;
        try
        {
            Fields = GribIndexer.Index(source);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>The fields of every message, in file order.</summary>
    public IReadOnlyList<GribField> Fields { get; }

    /// <summary>Opens and indexes a GRIB2 file.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The opened file. Dispose it when done.</returns>
    /// <exception cref="GribFormatException">The file isn't valid GRIB2.</exception>
    /// <exception cref="UnsupportedGribException">The file holds a GRIB edition 1 message, or another edition.</exception>
    public static GribFile Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new GribFile(new FileGribSource(path));
    }

    /// <summary>Opens and indexes GRIB2 data from a seekable stream.</summary>
    /// <param name="stream">A readable, seekable stream positioned anywhere; it's read from the start.</param>
    /// <param name="leaveOpen">Whether disposing the file leaves <paramref name="stream"/> open.</param>
    /// <returns>The opened file. Dispose it when done.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> can't read or seek.</exception>
    /// <exception cref="GribFormatException">The data isn't valid GRIB2.</exception>
    /// <exception cref="UnsupportedGribException">The data holds a GRIB edition 1 message, or another edition.</exception>
    public static GribFile Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("The stream must be readable and seekable; copy it to a MemoryStream or a file first.", nameof(stream));
        return new GribFile(new StreamGribSource(stream, leaveOpen));
    }

    /// <summary>Opens and indexes GRIB2 data already in memory.</summary>
    /// <param name="bytes">The data. It isn't copied, so don't change it while the file is in use.</param>
    /// <returns>The opened file.</returns>
    /// <exception cref="GribFormatException">The data isn't valid GRIB2.</exception>
    /// <exception cref="UnsupportedGribException">The data holds a GRIB edition 1 message, or another edition.</exception>
    public static GribFile Open(ReadOnlyMemory<byte> bytes) => new(new MemoryGribSource(bytes));

    /// <summary>
    /// Decodes a field's values.
    /// </summary>
    /// <param name="field">A field from <see cref="Fields"/>.</param>
    /// <param name="cancellationToken">Signals that the values are no longer needed.</param>
    /// <returns>
    /// One value per grid point, row by row: <c>row × Ni + column</c>, rows
    /// and columns running the way <see cref="GribField.ToWeatherNativeGrid"/>
    /// says (the scanning mode's column-major and boustrophedon orders are
    /// undone). Points the bitmap or the complex packing's missing value
    /// management marks missing are <see cref="float.NaN"/>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="field"/> isn't one of this file's fields.</exception>
    /// <exception cref="UnsupportedGribException">The field uses an unsupported template (<see cref="GribField.UnsupportedReason"/>).</exception>
    /// <exception cref="GribFormatException">The field's bitmap or data section is malformed.</exception>
    /// <exception cref="ObjectDisposedException">The file has been disposed.</exception>
    public float[] ReadValues(GribField field, CancellationToken cancellationToken = default)
    {
        CheckField(field);
        if (field.UnsupportedReason is { } reason)
            throw new UnsupportedGribException(reason);
        cancellationToken.ThrowIfCancellationRequested();

        var drs = field.DataRepresentation;
        var data = _source.ReadBytes(field.DataOffset, field.DataLength);
        var packed = drs.TemplateNumber switch
        {
            0 => SimpleUnpacking.Unpack(data, drs, cancellationToken),
            2 or 3 => ComplexUnpacking.Unpack(data, drs, cancellationToken),
            40 => Jpeg2000Unpacking.Unpack(data, drs, cancellationToken),
            41 => PngUnpacking.Unpack(data, drs, cancellationToken),
            _ => throw new UnsupportedGribException(
                string.Create(CultureInfo.InvariantCulture, $"Data representation template 5.{drs.TemplateNumber} isn't supported.")),
        };

        var points = checked((int)field.Grid.NumberOfPoints);
        float[] values;
        if (field.HasBitmap)
        {
            var bitmap = _source.ReadBytes(field.BitmapOffset, field.BitmapLength);
            values = ApplyBitmap(packed, bitmap, points);
        }
        else
        {
            if (packed.Length != points)
            {
                throw new GribFormatException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Field #{field.Index} has {packed.Length} packed values for {points} grid points and no bitmap."));
            }

            values = packed;
        }

        return Reorder(values, field.Grid.LatLon!);
    }

    /// <summary>
    /// Returns a <see cref="WeatherStepDecoder"/> that decodes
    /// <paramref name="field"/> with <see cref="ReadValues"/>, for a
    /// <see cref="WeatherTimeStep"/>. The file must stay open while the
    /// decoder is in use.
    /// </summary>
    /// <param name="field">A field from <see cref="Fields"/>.</param>
    /// <returns>The decoder.</returns>
    /// <exception cref="ArgumentException"><paramref name="field"/> isn't one of this file's fields.</exception>
    public WeatherStepDecoder CreateStepDecoder(GribField field)
    {
        CheckField(field);
        return cancellationToken => ReadValues(field, cancellationToken);
    }

    /// <summary>Closes the file. Decoders made by <see cref="CreateStepDecoder"/> stop working.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _source.Dispose();
    }

    private void CheckField(GribField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (field.Index < 0 || field.Index >= Fields.Count || !ReferenceEquals(Fields[field.Index], field))
            throw new ArgumentException("The field isn't one of this file's fields.", nameof(field));
    }

    private static float[] ApplyBitmap(float[] packed, byte[] bitmap, int points)
    {
        if ((long)bitmap.Length * 8 < points)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The bitmap has {bitmap.Length * 8L} bits for {points} grid points."));
        }

        var values = new float[points];
        var n = 0;
        for (var i = 0; i < points; i++)
        {
            if ((bitmap[i >> 3] & (0x80 >> (i & 7))) != 0)
            {
                if (n == packed.Length)
                    throw new GribFormatException("The bitmap marks more points present than there are packed values.");
                values[i] = packed[n++];
            }
            else
            {
                values[i] = float.NaN;
            }
        }

        if (n != packed.Length)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The bitmap marks {n} points present, but {packed.Length} values are packed."));
        }

        return values;
    }

    private static float[] Reorder(float[] values, GribLatLonGrid grid)
    {
        var columnMajor = (grid.ScanningMode & GribScanningMode.JConsecutive) != 0;
        var boustrophedon = (grid.ScanningMode & GribScanningMode.Boustrophedon) != 0;
        if (!columnMajor && !boustrophedon)
            return values;

        int ni = grid.Ni, nj = grid.Nj;
        var result = new float[values.Length];
        for (var row = 0; row < nj; row++)
        {
            for (var col = 0; col < ni; col++)
            {
                int source;
                if (columnMajor)
                {
                    // Stored column by column; with boustrophedon, odd columns run backwards.
                    var j = boustrophedon && (col & 1) == 1 ? nj - 1 - row : row;
                    source = (col * nj) + j;
                }
                else
                {
                    var i = (row & 1) == 1 ? ni - 1 - col : col;
                    source = (row * ni) + i;
                }

                result[(row * ni) + col] = values[source];
            }
        }

        return result;
    }
}
