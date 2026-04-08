namespace EncDotNet.S100.Hdf5;

/// <summary>
/// Abstraction over an HDF5 file, providing access to the root group.
/// </summary>
public interface IHdf5File : IDisposable
{
    /// <summary>Gets the root group of the HDF5 file.</summary>
    IHdf5Group Root { get; }
}
