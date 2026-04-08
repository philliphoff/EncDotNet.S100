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

    /// <summary>Reads a string attribute value.</summary>
    string ReadStringAttribute(string name);

    /// <summary>
    /// Reads an entire dataset as a flat array of unmanaged values.
    /// For compound datasets the struct layout must match the HDF5 field offsets.
    /// </summary>
    T[] ReadDataset<T>(string name) where T : unmanaged;
}
