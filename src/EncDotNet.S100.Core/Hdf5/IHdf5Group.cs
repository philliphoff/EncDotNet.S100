namespace EncDotNet.S100.Hdf5;

/// <summary>
/// Abstraction over an HDF5 group, providing navigation and typed data access.
/// </summary>
public interface IHdf5Group
{
    /// <summary>Opens a child group by name.</summary>
    IHdf5Group OpenGroup(string name);

    /// <summary>Gets the names of all child groups.</summary>
    IReadOnlyList<string> GroupNames { get; }

    /// <summary>Returns <c>true</c> if an attribute with the given name exists on this group.</summary>
    bool AttributeExists(string name);

    /// <summary>Reads a scalar unmanaged attribute value.</summary>
    T ReadAttribute<T>(string name) where T : unmanaged;

    /// <summary>
    /// Reads a floating-point attribute as <see cref="double"/>, accepting
    /// either <c>H5T_IEEE_F32*</c> or <c>H5T_IEEE_F64*</c> on disk. The
    /// S-100 specs leave the on-disk width of many grid-georef attributes
    /// (origin, spacing) to producer choice, so readers should not hard-code
    /// the width.
    /// </summary>
    double ReadDoubleAttribute(string name);

    /// <summary>
    /// Reads a fixed-point attribute as <see cref="long"/>, accepting any
    /// of the 8/16/32/64-bit signed or unsigned integer types on disk. The
    /// S-100 specs leave the on-disk width of many count attributes
    /// (<c>numPointsLatitudinal</c>, <c>numberOfTimes</c>, …) to producer
    /// choice, so readers should not hard-code the width.
    /// </summary>
    long ReadInt64Attribute(string name);

    /// <summary>
    /// Reads a string attribute value. Trailing <c>NUL</c> characters from
    /// fixed-length <c>H5T_STR_NULLPAD</c> strings are stripped so that the
    /// returned value is suitable for direct parsing.
    /// </summary>
    string ReadStringAttribute(string name);

    /// <summary>
    /// Reads an entire dataset as a flat array of unmanaged values.
    /// For compound datasets the struct layout must match the HDF5 field offsets.
    /// </summary>
    T[] ReadDataset<T>(string name) where T : unmanaged;

    /// <summary>
    /// Reads a compound dataset as raw bytes plus member metadata. Callers
    /// project rows into product-specific value types so that small
    /// spec-conforming variations between producers (member renames,
    /// numeric width changes) can be tolerated without changing public
    /// value-struct layouts.
    /// </summary>
    RawCompoundDataset ReadRawCompoundDataset(string name);
}
