namespace EncDotNet.S100.Hdf5;

/// <summary>
/// Names the HDF5 compound member a struct field is written as, when the
/// struct is the element type of a dataset added with
/// <see cref="Hdf5WriteGroup.SetDataset{T}(string, T[], ulong[])"/>.
/// </summary>
/// <remarks>
/// S-100 member names are camelCase (<c>waterLevelHeight</c>) or carry dots
/// (<c>uom.name</c>), which C# field names can't follow. A field without the
/// attribute is written under its own name.
/// </remarks>
/// <param name="name">The compound member's name in the file.</param>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class Hdf5MemberAttribute(string name) : Attribute
{
    /// <summary>The compound member's name in the file.</summary>
    public string Name { get; } = name;
}
