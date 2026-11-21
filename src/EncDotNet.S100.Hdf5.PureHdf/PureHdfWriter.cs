using System.Reflection;
using PureHDF;

namespace EncDotNet.S100.Hdf5.PureHdf;

/// <summary>
/// Writes an HDF5 file built with <see cref="Hdf5WriteGroup"/> using
/// PureHDF.
/// </summary>
/// <remarks>
/// Strings are variable-length UTF-8, enumerations are HDF5 enumerations
/// with the enum's member names as labels, and a struct dataset is a
/// compound named by <see cref="Hdf5MemberAttribute"/>. PureHDF's writer
/// relies on runtime generic instantiation, so writing works under trimming
/// but not in a NativeAOT-compiled application; reading does.
/// </remarks>
public static class PureHdfWriter
{
    /// <summary>Writes a file to a path, replacing any file there.</summary>
    /// <param name="root">The root group.</param>
    /// <param name="path">The file to write.</param>
    public static void Write(Hdf5WriteGroup root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ToFile(root).Write(path, Options);
    }

    /// <summary>Writes a file to a stream.</summary>
    /// <param name="root">The root group.</param>
    /// <param name="stream">A writable, seekable stream; it's left open.</param>
    public static void Write(Hdf5WriteGroup root, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(stream);
        ToFile(root).Write(stream, Options);
    }

    private static H5WriteOptions Options { get; } = new(
        FieldNameMapper: field => field.GetCustomAttribute<Hdf5MemberAttribute>()?.Name ?? field.Name);

    private static H5File ToFile(Hdf5WriteGroup root)
    {
        var file = new H5File();
        Fill(file, root);
        return file;
    }

    private static void Fill(H5Group target, Hdf5WriteGroup source)
    {
        foreach (var (name, value) in source.Attributes)
        {
            target.Attributes[name] = value;
        }

        foreach (var (name, dataset) in source.Datasets)
        {
            target[name] = dataset.Dimensions.Count == 1
                ? dataset.Values
                : new H5Dataset(dataset.Values, fileDims: [.. dataset.Dimensions]);
        }

        foreach (var (name, group) in source.Groups)
        {
            var child = new H5Group();
            Fill(child, group);
            target[name] = child;
        }
    }
}
