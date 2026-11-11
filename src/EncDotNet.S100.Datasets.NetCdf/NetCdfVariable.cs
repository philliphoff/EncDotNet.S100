namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>
/// A NetCDF variable: a name, a type, the dimensions it's laid out along
/// (slowest first) and its attributes.
/// </summary>
public sealed class NetCdfVariable
{
    /// <summary>Creates a variable.</summary>
    /// <param name="name">The variable's name.</param>
    /// <param name="dataType">The type of its values.</param>
    /// <param name="dimensions">Its dimensions, slowest-varying first; empty for a scalar.</param>
    /// <param name="attributes">Its attributes.</param>
    public NetCdfVariable(
        string name,
        NetCdfDataType dataType,
        IReadOnlyList<NetCdfDimension> dimensions,
        IReadOnlyList<NetCdfAttribute> attributes)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(attributes);
        Name = name;
        DataType = dataType;
        Dimensions = dimensions.ToArray();
        Attributes = attributes.ToArray();
    }

    /// <summary>The variable's name, for example <c>"VHM0"</c>.</summary>
    public string Name { get; }

    /// <summary>The type of its values.</summary>
    public NetCdfDataType DataType { get; }

    /// <summary>Its dimensions, slowest-varying first; empty for a scalar.</summary>
    public IReadOnlyList<NetCdfDimension> Dimensions { get; }

    /// <summary>Its attributes, in file order.</summary>
    public IReadOnlyList<NetCdfAttribute> Attributes { get; }

    /// <summary>The number of points along each dimension.</summary>
    public IReadOnlyList<long> Shape => Dimensions.Select(d => d.Length).ToArray();

    /// <summary>Whether the variable is numeric (and so can be read with <see cref="NetCdfFile.Read"/>).</summary>
    public bool IsNumeric => DataType is not (NetCdfDataType.Char or NetCdfDataType.String or NetCdfDataType.Other);

    /// <summary>
    /// Whether the variable is a coordinate variable: one dimension, with the
    /// same name as the variable (NetCDF User Guide, "Coordinate Variables").
    /// </summary>
    public bool IsCoordinateVariable =>
        Dimensions.Count == 1 && string.Equals(Dimensions[0].Name, Name, StringComparison.Ordinal);

    /// <summary>Finds an attribute by name (case-sensitive, as NetCDF names are).</summary>
    /// <param name="name">The attribute's name.</param>
    /// <returns>The attribute, or <see langword="null"/>.</returns>
    public NetCdfAttribute? FindAttribute(string name) => NetCdfFile.Find(Attributes, name);

    /// <summary>The text of attribute <paramref name="name"/>, trimmed, or <see langword="null"/> when it's absent or numeric.</summary>
    /// <param name="name">The attribute's name.</param>
    public string? GetText(string name) => FindAttribute(name)?.Text?.Trim();

    /// <summary>Returns the name and dimensions, for example <c>"VHM0(time, latitude, longitude)"</c>.</summary>
    public override string ToString() => $"{Name}({string.Join(", ", Dimensions.Select(d => d.Name))})";
}
