using System.Globalization;
using System.Text.RegularExpressions;
using PureHDF;
using PureHDF.Selections;
using PureHDF.VOL.Native;

namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>
/// Reads NetCDF-4 files through PureHDF. A NetCDF-4 file is an HDF5 file in
/// which every dimension is an HDF5 dimension scale (a dataset with
/// <c>CLASS = "DIMENSION_SCALE"</c>) and every variable names its dimensions
/// in a <c>DIMENSION_LIST</c> attribute of object references (NetCDF User
/// Guide, "NetCDF-4 Format"; HDF5 Dimension Scale Specification).
/// </summary>
/// <remarks>
/// <para>
/// Only the root group is read; a file whose variables sit in sub-groups fails
/// with a message naming them. A dimension with no coordinate variable is a
/// dimension scale whose <c>NAME</c> starts "This is a netCDF dimension but
/// not a netCDF variable"; it's a dimension and not a variable. A variable
/// with no <c>DIMENSION_LIST</c> (a plain HDF5 file) is matched to dimension
/// scales by length when that's unambiguous, and otherwise gets anonymous
/// <c>phony_dim_N</c> dimensions, as the NetCDF library does.
/// </para>
/// <para>
/// PureHDF decodes the deflate and shuffle filters NetCDF-4 files normally
/// use (and Fletcher-32, N-bit and scale-offset). Any other filter, such as
/// Zstandard or Blosc, fails with a <see cref="NetCdfFormatException"/>
/// naming it.
/// </para>
/// </remarks>
internal sealed partial class NetCdf4File : NetCdfFile
{
    // The NAME a dimension scale with no coordinate variable carries.
    private const string PureDimensionPrefix = "This is a netCDF dimension but not a netCDF variable";

    // HDF5 and NetCDF-4 bookkeeping attributes, not user attributes.
    private static readonly HashSet<string> HiddenAttributes = new(StringComparer.Ordinal)
    {
        "CLASS", "NAME", "REFERENCE_LIST", "DIMENSION_LIST", "_Netcdf4Dimid", "_Netcdf4Coordinates",
        "_NCProperties", "_nc3_strict", "_SuperblockVersion", "_IsNetcdf4",
    };

    // HDF5 filter ids (HDF Group registered filter plugins) by name.
    private static readonly Dictionary<int, string> FilterNames = new()
    {
        [4] = "SZIP",
        [307] = "bzip2",
        [32001] = "Blosc",
        [32004] = "LZ4",
        [32008] = "Bitshuffle",
        [32013] = "ZFP",
        [32015] = "Zstandard",
        [32017] = "SZ",
        [32026] = "Blosc2",
    };

    private readonly NativeFile _file;
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly Dictionary<NetCdfVariable, IH5Dataset> _datasets = new(ReferenceEqualityComparer.Instance);

    public NetCdf4File(Stream stream, bool leaveOpen)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        try
        {
            _file = H5File.Open(stream, leaveOpen: true);
        }
        catch (Exception ex)
        {
            throw new NetCdfFormatException($"The file has the HDF5 signature but can't be read as HDF5: {ex.Message}", ex);
        }

        try
        {
            ReadStructure();
        }
        catch (NetCdfFormatException)
        {
            _file.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _file.Dispose();
            throw new NetCdfFormatException($"The NetCDF-4 file's structure can't be read: {ex.Message}", ex);
        }
    }

    public override NetCdfFormat Format => NetCdfFormat.NetCdf4;

    private void ReadStructure()
    {
        var datasets = new List<IH5Dataset>();
        var groups = new List<string>();
        foreach (var child in _file.Children())
        {
            switch (child)
            {
                case IH5Dataset dataset:
                    datasets.Add(dataset);
                    break;
                case IH5Group group:
                    groups.Add(group.Name);
                    break;
            }
        }

        // Dimensions: every dimension scale at the root.
        var dimensions = new List<NetCdfDimension>();
        var dimensionsByName = new Dictionary<string, NetCdfDimension>(StringComparer.Ordinal);
        var pureDimensions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dataset in datasets)
        {
            if (!IsDimensionScale(dataset))
                continue;

            var space = dataset.Space;
            long length = space.Rank == 1 ? (long)space.Dimensions[0] : 0;
            bool unlimited = space.Rank == 1 && space.MaxDimensions.Length == 1 && space.MaxDimensions[0] == ulong.MaxValue;
            var dimension = new NetCdfDimension(dataset.Name, length, unlimited);
            dimensions.Add(dimension);
            dimensionsByName[dataset.Name] = dimension;
            if (ReadStringAttribute(dataset, "NAME") is { } name && name.StartsWith(PureDimensionPrefix, StringComparison.Ordinal))
                pureDimensions.Add(dataset.Name);
        }

        // Variables: every other dataset, and every dimension scale that's a coordinate variable.
        var variables = new List<NetCdfVariable>();
        int phony = 0;
        foreach (var dataset in datasets)
        {
            if (pureDimensions.Contains(dataset.Name))
                continue;

            var space = dataset.Space;
            var shape = space.Rank == 0 ? [] : space.Dimensions.Select(d => (long)d).ToArray();
            IReadOnlyList<NetCdfDimension> variableDimensions;
            if (IsDimensionScale(dataset) && dimensionsByName.TryGetValue(dataset.Name, out var own))
            {
                variableDimensions = [own];
            }
            else if (ResolveDimensionList(dataset, shape, dimensionsByName) is { } listed)
            {
                variableDimensions = listed;
            }
            else
            {
                variableDimensions = MatchByLength(shape, dimensions, ref phony);
            }

            var variable = new NetCdfVariable(
                dataset.Name,
                ToDataType(dataset.Type),
                variableDimensions,
                ReadAttributes(dataset));
            variables.Add(variable);
            _datasets[variable] = dataset;
        }

        if (variables.Count == 0 && groups.Count > 0)
        {
            throw new NetCdfFormatException(
                $"The NetCDF-4 file keeps its variables in groups ({string.Join(", ", groups)}); only variables in the root group are read.");
        }

        Dimensions = dimensions;
        Variables = variables;
        GlobalAttributes = ReadAttributes(_file);
    }

    private protected override double[] ReadCore(NetCdfVariable variable, long[] start, long[] count, int total)
    {
        var dataset = _datasets[variable];
        Selection? selection = null;
        if (start.Length > 0)
        {
            selection = new HyperslabSelection(
                start.Length,
                start.Select(s => (ulong)s).ToArray(),
                count.Select(c => (ulong)c).ToArray());
        }

        ulong[] memoryDims = [(ulong)total];
        try
        {
            return variable.DataType switch
            {
                NetCdfDataType.Float => Widen(dataset.Read<float[]>(selection, null, memoryDims)),
                NetCdfDataType.Double => dataset.Read<double[]>(selection, null, memoryDims),
                NetCdfDataType.Byte => Widen(dataset.Read<sbyte[]>(selection, null, memoryDims)),
                NetCdfDataType.UByte => Widen(dataset.Read<byte[]>(selection, null, memoryDims)),
                NetCdfDataType.Short => Widen(dataset.Read<short[]>(selection, null, memoryDims)),
                NetCdfDataType.UShort => Widen(dataset.Read<ushort[]>(selection, null, memoryDims)),
                NetCdfDataType.Int => Widen(dataset.Read<int[]>(selection, null, memoryDims)),
                NetCdfDataType.UInt => Widen(dataset.Read<uint[]>(selection, null, memoryDims)),
                NetCdfDataType.Int64 => Widen(dataset.Read<long[]>(selection, null, memoryDims)),
                NetCdfDataType.UInt64 => Widen(dataset.Read<ulong[]>(selection, null, memoryDims)),
                _ => throw new ArgumentException($"Variable '{variable.Name}' isn't numeric.", nameof(variable)),
            };
        }
        catch (Exception ex) when (FindMissingFilter(ex) is { } filter)
        {
            throw new NetCdfFormatException(
                $"Variable '{variable.Name}' is compressed with the {filter} HDF5 filter, which isn't supported; only deflate and shuffle are. Rewrite the file with deflate (for example 'nccopy -d 4 in.nc out.nc').",
                ex);
        }
        catch (Exception ex) when (ex is not (ArgumentException or OutOfMemoryException or OperationCanceledException))
        {
            throw new NetCdfFormatException($"Variable '{variable.Name}' can't be read: {Innermost(ex).Message}", ex);
        }
    }

    private protected override void DisposeCore()
    {
        _file.Dispose();
        if (!_leaveOpen)
            _stream.Dispose();
    }

    private static bool IsDimensionScale(IH5Dataset dataset) =>
        string.Equals(ReadStringAttribute(dataset, "CLASS"), "DIMENSION_SCALE", StringComparison.Ordinal);

    // The dimensions a DIMENSION_LIST names, or null when there's no usable list.
    private IReadOnlyList<NetCdfDimension>? ResolveDimensionList(
        IH5Dataset dataset, long[] shape, Dictionary<string, NetCdfDimension> dimensionsByName)
    {
        if (shape.Length == 0 || !dataset.AttributeExists("DIMENSION_LIST"))
            return null;

        NativeObjectReference1[][] references;
        try
        {
            references = dataset.Attribute("DIMENSION_LIST").Read<NativeObjectReference1[][]>();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }

        if (references.Length != shape.Length)
            return null;

        var result = new NetCdfDimension[shape.Length];
        for (int i = 0; i < shape.Length; i++)
        {
            if (references[i].Length == 0)
                return null;

            string name;
            try
            {
                name = _file.Get(references[i][0]).Name;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }

            if (!dimensionsByName.TryGetValue(name, out var dimension))
                return null;

            // An unlimited dimension's scale may be shorter than a variable
            // written later; the variable's own extent is what it holds.
            result[i] = dimension.Length == shape[i] ? dimension : dimension with { Length = shape[i] };
        }

        return result;
    }

    // Dimensions for a variable with no DIMENSION_LIST: the dimension scale of
    // the same length when exactly one has it, else an anonymous dimension.
    private static IReadOnlyList<NetCdfDimension> MatchByLength(long[] shape, List<NetCdfDimension> dimensions, ref int phony)
    {
        var result = new NetCdfDimension[shape.Length];
        for (int i = 0; i < shape.Length; i++)
        {
            var candidates = dimensions.Where(d => d.Length == shape[i]).ToArray();
            result[i] = candidates.Length == 1
                ? candidates[0]
                : new NetCdfDimension(string.Create(CultureInfo.InvariantCulture, $"phony_dim_{phony++}"), shape[i]);
        }

        return result;
    }

    private static List<NetCdfAttribute> ReadAttributes(IH5Object owner)
    {
        var attributes = new List<NetCdfAttribute>();
        foreach (var attribute in owner.Attributes())
        {
            if (HiddenAttributes.Contains(attribute.Name))
                continue;
            if (ReadAttribute(attribute) is { } read)
                attributes.Add(read);
        }

        return attributes;
    }

    private static NetCdfAttribute? ReadAttribute(IH5Attribute attribute)
    {
        try
        {
            var type = ToDataType(attribute.Type);
            bool scalar = attribute.Space.Rank == 0;
            switch (type)
            {
                case NetCdfDataType.String:
                    var text = scalar
                        ? attribute.Read<string>()
                        : string.Join("\n", attribute.Read<string[]>());
                    return new NetCdfAttribute(attribute.Name, TrimNulls(text));
                case NetCdfDataType.Other:
                case NetCdfDataType.Char:
                    return null;
                default:
                    return new NetCdfAttribute(attribute.Name, type, ReadNumbers(attribute, type, scalar));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An attribute of a type PureHDF can't read isn't one CF needs.
            return null;
        }
    }

    private static double[] ReadNumbers(IH5Attribute attribute, NetCdfDataType type, bool scalar) => type switch
    {
        NetCdfDataType.Float => scalar ? [attribute.Read<float>()] : Widen(attribute.Read<float[]>()),
        NetCdfDataType.Double => scalar ? [attribute.Read<double>()] : attribute.Read<double[]>(),
        NetCdfDataType.Byte => scalar ? [attribute.Read<sbyte>()] : Widen(attribute.Read<sbyte[]>()),
        NetCdfDataType.UByte => scalar ? [attribute.Read<byte>()] : Widen(attribute.Read<byte[]>()),
        NetCdfDataType.Short => scalar ? [attribute.Read<short>()] : Widen(attribute.Read<short[]>()),
        NetCdfDataType.UShort => scalar ? [attribute.Read<ushort>()] : Widen(attribute.Read<ushort[]>()),
        NetCdfDataType.Int => scalar ? [attribute.Read<int>()] : Widen(attribute.Read<int[]>()),
        NetCdfDataType.UInt => scalar ? [attribute.Read<uint>()] : Widen(attribute.Read<uint[]>()),
        NetCdfDataType.Int64 => scalar ? [attribute.Read<long>()] : Widen(attribute.Read<long[]>()),
        NetCdfDataType.UInt64 => scalar ? [attribute.Read<ulong>()] : Widen(attribute.Read<ulong[]>()),
        _ => [],
    };

    private static string? ReadStringAttribute(IH5Object owner, string name)
    {
        try
        {
            if (!owner.AttributeExists(name))
                return null;
            var attribute = owner.Attribute(name);
            return ToDataType(attribute.Type) == NetCdfDataType.String ? TrimNulls(attribute.Read<string>()) : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static NetCdfDataType ToDataType(IH5DataType type)
    {
        switch (type.Class)
        {
            case H5DataTypeClass.FloatingPoint:
                return type.Size switch
                {
                    4 => NetCdfDataType.Float,
                    8 => NetCdfDataType.Double,
                    _ => NetCdfDataType.Other,
                };
            case H5DataTypeClass.FixedPoint:
                bool signed = type.FixedPoint.IsSigned;
                return type.Size switch
                {
                    1 => signed ? NetCdfDataType.Byte : NetCdfDataType.UByte,
                    2 => signed ? NetCdfDataType.Short : NetCdfDataType.UShort,
                    4 => signed ? NetCdfDataType.Int : NetCdfDataType.UInt,
                    8 => signed ? NetCdfDataType.Int64 : NetCdfDataType.UInt64,
                    _ => NetCdfDataType.Other,
                };
            case H5DataTypeClass.String:
                return NetCdfDataType.String;
            case H5DataTypeClass.VariableLength:
                return type.VariableLength.BaseType.Class == H5DataTypeClass.FixedPoint && type.VariableLength.BaseType.Size == 1
                    ? NetCdfDataType.String
                    : NetCdfDataType.Other;
            default:
                return NetCdfDataType.Other;
        }
    }

    private static string? FindMissingFilter(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            var match = MissingFilterPattern().Match(e.Message);
            if (!match.Success)
                continue;

            int id = int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture);
            if (FilterNames.TryGetValue(id, out var known))
                return string.Create(CultureInfo.InvariantCulture, $"{known} (id {id})");
            var name = match.Groups["name"].Value;
            return string.IsNullOrWhiteSpace(name)
                ? string.Create(CultureInfo.InvariantCulture, $"id {id}")
                : string.Create(CultureInfo.InvariantCulture, $"'{name}' (id {id})");
        }

        return null;
    }

    private static Exception Innermost(Exception exception)
    {
        var e = exception;
        while (e.InnerException is not null)
            e = e.InnerException;
        return e;
    }

    private static string TrimNulls(string text) => text.TrimEnd('\0');

    private static double[] Widen<T>(T[] values)
        where T : struct, IConvertible
    {
        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
            result[i] = values[i].ToDouble(CultureInfo.InvariantCulture);
        return result;
    }

    // PureHDF's message for a filter that isn't registered:
    // "Could not find filter 'zstd' with ID '32015'. ..."
    [GeneratedRegex(@"Could not find filter '(?<name>[^']*)' with ID '(?<id>\d+)'")]
    private static partial Regex MissingFilterPattern();
}
