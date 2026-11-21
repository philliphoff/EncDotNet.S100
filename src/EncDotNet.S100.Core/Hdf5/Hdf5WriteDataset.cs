namespace EncDotNet.S100.Hdf5;

/// <summary>
/// A dataset in an HDF5 file being built with <see cref="Hdf5WriteGroup"/>:
/// its values in row-major order and the shape they're written with.
/// </summary>
public sealed class Hdf5WriteDataset
{
    internal Hdf5WriteDataset(Array values, Type elementType, IReadOnlyList<ulong> dimensions)
    {
        Values = values;
        ElementType = elementType;
        Dimensions = dimensions;
    }

    /// <summary>
    /// The values, a one-dimensional array in row-major order. A struct
    /// element type is written as a compound, with each field a member.
    /// </summary>
    public Array Values { get; }

    /// <summary>The element type of <see cref="Values"/>.</summary>
    public Type ElementType { get; }

    /// <summary>The dataset's dimensions; one for a list, two for a grid.</summary>
    public IReadOnlyList<ulong> Dimensions { get; }
}
