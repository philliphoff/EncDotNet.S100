using System.Globalization;

namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>
/// An open NetCDF file: its dimensions, variables and global attributes, and
/// reads of a variable's raw values. NetCDF-3 (classic and 64-bit offset) is
/// read by a small managed reader written from the Unidata NetCDF classic
/// format specification; NetCDF-4 is read through PureHDF.
/// </summary>
/// <remarks>
/// <para>
/// This is the format layer only. Values come back as stored: packing
/// (<c>scale_factor</c>, <c>add_offset</c>) and missing values aren't applied
/// here; <see cref="NetCdfWeatherDataset"/> applies them.
/// </para>
/// <para>
/// Reads are serialised on the file, so one file can serve several threads.
/// The file keeps its stream open until it's disposed.
/// </para>
/// </remarks>
public abstract class NetCdfFile : IDisposable
{
    private readonly object _gate = new();
    private bool _disposed;

    private protected NetCdfFile()
    {
    }

    /// <summary>The on-disk format.</summary>
    public abstract NetCdfFormat Format { get; }

    /// <summary>The file's dimensions, in file order.</summary>
    public IReadOnlyList<NetCdfDimension> Dimensions { get; private protected set; } = [];

    /// <summary>The file's variables, in file order.</summary>
    public IReadOnlyList<NetCdfVariable> Variables { get; private protected set; } = [];

    /// <summary>The file's global attributes, in file order.</summary>
    public IReadOnlyList<NetCdfAttribute> GlobalAttributes { get; private protected set; } = [];

    /// <summary>Finds a variable by name (case-sensitive).</summary>
    /// <param name="name">The variable's name.</param>
    /// <returns>The variable, or <see langword="null"/>.</returns>
    public NetCdfVariable? FindVariable(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var variable in Variables)
        {
            if (string.Equals(variable.Name, name, StringComparison.Ordinal))
                return variable;
        }

        return null;
    }

    /// <summary>Finds a global attribute by name (case-sensitive).</summary>
    /// <param name="name">The attribute's name.</param>
    /// <returns>The attribute, or <see langword="null"/>.</returns>
    public NetCdfAttribute? FindGlobalAttribute(string name) => Find(GlobalAttributes, name);

    /// <summary>The text of global attribute <paramref name="name"/>, trimmed, or <see langword="null"/> when absent, numeric or empty.</summary>
    /// <param name="name">The attribute's name.</param>
    public string? GetGlobalText(string name) =>
        FindGlobalAttribute(name)?.Text?.Trim() is { Length: > 0 } text ? text : null;

    /// <summary>
    /// Reads a hyperslab of a numeric variable's raw values: <paramref name="count"/>
    /// points from <paramref name="start"/> along each dimension, row-major
    /// (the last dimension varies fastest).
    /// </summary>
    /// <param name="variable">A numeric variable of this file.</param>
    /// <param name="start">The first index along each dimension.</param>
    /// <param name="count">The number of points along each dimension.</param>
    /// <returns>The values, as stored (no packing or missing-value handling).</returns>
    /// <exception cref="ArgumentException">The hyperslab doesn't fit the variable, or the variable isn't numeric.</exception>
    /// <exception cref="NetCdfFormatException">The data can't be read, for example because it uses an unsupported compression filter.</exception>
    /// <exception cref="ObjectDisposedException">The file was disposed.</exception>
    public double[] Read(NetCdfVariable variable, ReadOnlySpan<long> start, ReadOnlySpan<long> count)
    {
        ArgumentNullException.ThrowIfNull(variable);
        if (!variable.IsNumeric)
            throw new ArgumentException($"Variable '{variable.Name}' holds {variable.DataType} values, which aren't numeric.", nameof(variable));

        int rank = variable.Dimensions.Count;
        if (start.Length != rank || count.Length != rank)
            throw new ArgumentException($"Variable '{variable.Name}' has {rank} dimensions; the hyperslab has {start.Length}.", nameof(start));

        long total = 1;
        for (int i = 0; i < rank; i++)
        {
            long length = variable.Dimensions[i].Length;
            if (start[i] < 0 || count[i] < 0 || start[i] + count[i] > length)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"The hyperslab [{start[i]}, {start[i] + count[i]}) is outside dimension '{variable.Dimensions[i].Name}' of variable '{variable.Name}' (length {length})."),
                    nameof(count));
            }

            total *= count[i];
        }

        if (total > Array.MaxLength)
            throw new ArgumentException($"The hyperslab of '{variable.Name}' has {total} values, too many for one read.", nameof(count));

        var startCopy = start.ToArray();
        var countCopy = count.ToArray();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return total == 0 ? [] : ReadCore(variable, startCopy, countCopy, (int)total);
        }
    }

    /// <summary>Reads every raw value of a numeric variable, row-major.</summary>
    /// <param name="variable">A numeric variable of this file.</param>
    /// <returns>The values, as stored.</returns>
    public double[] ReadAll(NetCdfVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        var start = new long[variable.Dimensions.Count];
        var count = variable.Dimensions.Select(d => d.Length).ToArray();
        return Read(variable, start, count);
    }

    /// <summary>
    /// Returns the format the leading bytes of a file announce: NetCDF-3
    /// classic (<c>CDF\x01</c>), 64-bit offset (<c>CDF\x02</c>), or HDF5 (the
    /// container of NetCDF-4), or <see langword="null"/> for anything else,
    /// including CDF-5 (<c>CDF\x05</c>).
    /// </summary>
    /// <param name="leadingBytes">At least the first 8 bytes of the file.</param>
    public static NetCdfFormat? DetectFormat(ReadOnlySpan<byte> leadingBytes)
    {
        if (leadingBytes.StartsWith("CDF\u0001"u8))
            return NetCdfFormat.Classic;
        if (leadingBytes.StartsWith("CDF\u0002"u8))
            return NetCdfFormat.Offset64;
        if (leadingBytes.StartsWith(Hdf5Signature))
            return NetCdfFormat.NetCdf4;
        return null;
    }

    /// <summary>Opens the NetCDF file at <paramref name="path"/>.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The open file; dispose it to close the file.</returns>
    /// <exception cref="NetCdfFormatException">The file isn't a NetCDF file this library can read.</exception>
    public static NetCdfFile Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var stream = File.OpenRead(path);
        try
        {
            return Open(stream, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Opens a NetCDF file from a seekable stream.</summary>
    /// <param name="stream">The file's bytes, positioned anywhere; it must be readable and seekable.</param>
    /// <param name="leaveOpen">Whether disposing the file leaves <paramref name="stream"/> open.</param>
    /// <returns>The open file.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> isn't readable and seekable.</exception>
    /// <exception cref="NetCdfFormatException">The bytes aren't a NetCDF file this library can read.</exception>
    public static NetCdfFile Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("A NetCDF file needs a readable, seekable stream.", nameof(stream));

        Span<byte> magic = stackalloc byte[8];
        stream.Position = 0;
        int read = stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false);
        stream.Position = 0;
        var leading = magic[..read];

        if (leading.StartsWith("CDF\u0005"u8))
        {
            throw new NetCdfFormatException(
                "The file is NetCDF CDF-5 (64-bit data, 'CDF\\x05'), which isn't supported; only NetCDF-3 classic, 64-bit offset and NetCDF-4 are.");
        }

        return DetectFormat(leading) switch
        {
            NetCdfFormat.Classic or NetCdfFormat.Offset64 => new NetCdf3File(stream, leaveOpen),
            NetCdfFormat.NetCdf4 => new NetCdf4File(stream, leaveOpen),
            _ => throw new NetCdfFormatException(
                "The file isn't a NetCDF file: it starts with neither 'CDF\\x01', 'CDF\\x02' (NetCDF-3) nor the HDF5 signature (NetCDF-4)."),
        };
    }

    /// <summary>Closes the file.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            DisposeCore();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Reads a validated, non-empty hyperslab. Called under the file's lock.</summary>
    private protected abstract double[] ReadCore(NetCdfVariable variable, long[] start, long[] count, int total);

    /// <summary>Releases the file's resources. Called once, under the file's lock.</summary>
    private protected abstract void DisposeCore();

    internal static NetCdfAttribute? Find(IReadOnlyList<NetCdfAttribute> attributes, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var attribute in attributes)
        {
            if (string.Equals(attribute.Name, name, StringComparison.Ordinal))
                return attribute;
        }

        return null;
    }

    // HDF5 File Format Specification §II.A, the format signature.
    private static ReadOnlySpan<byte> Hdf5Signature => [0x89, (byte)'H', (byte)'D', (byte)'F', 0x0D, 0x0A, 0x1A, 0x0A];
}
