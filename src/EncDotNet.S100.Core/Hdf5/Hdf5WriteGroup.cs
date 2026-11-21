using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace EncDotNet.S100.Hdf5;

/// <summary>
/// A group in an HDF5 file being built in memory: its attributes, child
/// groups and datasets. The root group stands for the whole file.
/// </summary>
/// <remarks>
/// <para>
/// This is the writing side of <see cref="IHdf5Group"/>: an encoder such as
/// an S-104 writer builds the file's tree here, independent of any HDF5
/// library, and an HDF5 backend writes it out (in this repository,
/// <c>PureHdfWriter</c> in <c>EncDotNet.S100.Hdf5.PureHdf</c>).
/// </para>
/// <para>
/// Names keep the order they're added in. Setting a name again replaces
/// its value. A name can be a group or a dataset, not both.
/// </para>
/// </remarks>
public sealed class Hdf5WriteGroup
{
    private readonly OrderedDictionary<string, object> _attributes = new(StringComparer.Ordinal);
    private readonly OrderedDictionary<string, Hdf5WriteGroup> _groups = new(StringComparer.Ordinal);
    private readonly OrderedDictionary<string, Hdf5WriteDataset> _datasets = new(StringComparer.Ordinal);

    /// <summary>
    /// The attributes, by name. A value is a <see cref="string"/>, a
    /// <see cref="byte"/>, <see cref="ushort"/>, <see cref="uint"/>,
    /// <see cref="int"/> or <see cref="long"/>, a <see cref="float"/> or
    /// <see cref="double"/>, or an enumeration value.
    /// </summary>
    public IReadOnlyDictionary<string, object> Attributes => new ReadOnlyDictionary<string, object>(_attributes);

    /// <summary>The child groups, by name.</summary>
    public IReadOnlyDictionary<string, Hdf5WriteGroup> Groups => new ReadOnlyDictionary<string, Hdf5WriteGroup>(_groups);

    /// <summary>The datasets, by name.</summary>
    public IReadOnlyDictionary<string, Hdf5WriteDataset> Datasets => new ReadOnlyDictionary<string, Hdf5WriteDataset>(_datasets);

    /// <summary>Returns the child group with a name, adding it when there's none.</summary>
    /// <param name="name">The group's name, without a <c>/</c>.</param>
    /// <returns>The child group.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty or has a <c>/</c>, or a dataset has the name.
    /// </exception>
    public Hdf5WriteGroup Group(string name)
    {
        ValidateName(name);
        if (_datasets.ContainsKey(name))
        {
            throw new ArgumentException($"'{name}' is already a dataset.", nameof(name));
        }

        if (!_groups.TryGetValue(name, out var group))
        {
            group = new Hdf5WriteGroup();
            _groups.Add(name, group);
        }

        return group;
    }

    /// <summary>Sets a variable-length UTF-8 string attribute.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This group.</returns>
    public Hdf5WriteGroup SetAttribute(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SetAttributeValue(name, value);
    }

    /// <summary>Sets an unsigned 8-bit integer attribute.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This group.</returns>
    public Hdf5WriteGroup SetAttribute(string name, byte value) => SetAttributeValue(name, value);

    /// <summary>Sets an unsigned 16-bit integer attribute.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This group.</returns>
    public Hdf5WriteGroup SetAttribute(string name, ushort value) => SetAttributeValue(name, value);

    /// <summary>Sets an unsigned 32-bit integer attribute.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This group.</returns>
    public Hdf5WriteGroup SetAttribute(string name, uint value) => SetAttributeValue(name, value);

    /// <summary>Sets a signed 32-bit integer attribute.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This group.</returns>
    public Hdf5WriteGroup SetAttribute(string name, int value) => SetAttributeValue(name, value);

    /// <summary>Sets a signed 64-bit integer attribute.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This group.</returns>
    public Hdf5WriteGroup SetAttribute(string name, long value) => SetAttributeValue(name, value);

    /// <summary>Sets a 32-bit floating-point attribute.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This group.</returns>
    public Hdf5WriteGroup SetAttribute(string name, float value) => SetAttributeValue(name, value);

    /// <summary>Sets a 64-bit floating-point attribute.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This group.</returns>
    public Hdf5WriteGroup SetAttribute(string name, double value) => SetAttributeValue(name, value);

    /// <summary>
    /// Sets an enumeration attribute. It's written as an HDF5 enumeration
    /// over the enum's underlying integer type, with the enum's member names
    /// as its labels; S-100 enumerations are unsigned 8-bit.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration type.</typeparam>
    /// <param name="name">The attribute's name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This group.</returns>
    public Hdf5WriteGroup SetEnumAttribute<TEnum>(string name, TEnum value)
        where TEnum : struct, Enum => SetAttributeValue(name, value);

    /// <summary>Adds or replaces a one-dimensional string dataset.</summary>
    /// <param name="name">The dataset's name, without a <c>/</c>.</param>
    /// <param name="values">The strings, written as variable-length UTF-8.</param>
    /// <returns>This group.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty or has a <c>/</c>, or a group has the name.
    /// </exception>
    public Hdf5WriteGroup SetDataset(string name, string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return AddDataset(name, new Hdf5WriteDataset(values, typeof(string), [(ulong)values.Length]));
    }

    /// <summary>
    /// Adds or replaces a dataset of numbers or of compound records. A
    /// struct element type is written as an HDF5 compound with one member
    /// per public field, named by <see cref="Hdf5MemberAttribute"/> or else
    /// the field's name; <see cref="string"/> fields are variable-length.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="name">The dataset's name, without a <c>/</c>.</param>
    /// <param name="values">The values in row-major order.</param>
    /// <param name="dimensions">
    /// The dimensions, such as rows and columns for a grid. Empty for a
    /// one-dimensional dataset of <paramref name="values"/>' length.
    /// </param>
    /// <returns>This group.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty or has a <c>/</c>, a group has the
    /// name, or the dimensions don't multiply to the number of values.
    /// </exception>
    public Hdf5WriteGroup SetDataset<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(
        string name,
        T[] values,
        params ulong[] dimensions)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(dimensions);
        ulong[] shape = dimensions.Length == 0 ? [(ulong)values.Length] : [.. dimensions];
        ulong count = 1;
        foreach (var dimension in shape)
        {
            count *= dimension;
        }

        if (count != (ulong)values.Length)
        {
            throw new ArgumentException(
                $"The dimensions [{string.Join(", ", shape)}] hold {count} values, not {values.Length}.",
                nameof(dimensions));
        }

        return AddDataset(name, new Hdf5WriteDataset(values, typeof(T), shape));
    }

    private Hdf5WriteGroup SetAttributeValue(string name, object value)
    {
        ValidateName(name);
        _attributes[name] = value;
        return this;
    }

    private Hdf5WriteGroup AddDataset(string name, Hdf5WriteDataset dataset)
    {
        ValidateName(name);
        if (_groups.ContainsKey(name))
        {
            throw new ArgumentException($"'{name}' is already a group.", nameof(name));
        }

        _datasets[name] = dataset;
        return this;
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{name}' has a '/'; give each group's name on its own.", nameof(name));
        }
    }
}
