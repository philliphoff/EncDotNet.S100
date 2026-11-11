using System.Reflection;
using PureHDF;

namespace EncDotNet.S100.Testing.Scenarios.Hdf5;

/// <summary>
/// Writes S-102 HDF5 files (S-102 Ed 3.0.0 §10): one
/// <c>BathymetryCoverage.01</c> instance on a regular geographic grid with a
/// <c>Group_001</c> of depth and uncertainty values.
/// </summary>
/// <remarks>
/// Attributes are written in the order given after the ones this builder
/// sets, so the same arguments give the same bytes.
/// </remarks>
public static class S102FileBuilder
{
    /// <summary>The S-102 fill value for depth and uncertainty (S-102 Ed 3.0.0 Table 10-3).</summary>
    public const float FillValue = 1_000_000f;

    /// <summary>One grid value: depth (metres, positive down) and its uncertainty.</summary>
    public struct BathymetryRow
    {
        /// <summary>Depth in metres below the vertical datum.</summary>
        [H5Name("depth")] public float Depth;

        /// <summary>Vertical uncertainty in metres.</summary>
        [H5Name("uncertainty")] public float Uncertainty;
    }

    /// <summary>A geographic regular grid's georeferencing (S-102 Ed 3.0.0 §10.2).</summary>
    public readonly record struct GridGeoreference(
        double OriginLatitude,
        double OriginLongitude,
        double SpacingLatitudinal,
        double SpacingLongitudinal,
        int NumPointsLatitudinal,
        int NumPointsLongitudinal);

    /// <summary>Writes the file.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="grid">The grid.</param>
    /// <param name="values">Row-major values, south row first.</param>
    /// <param name="rootAttributes">Root attributes (product specification, CRS, vertical datum, extent, issue date, …).</param>
    /// <param name="instanceAttributes">Extra <c>BathymetryCoverage.01</c> attributes after the grid ones, or <c>null</c>.</param>
    /// <returns><paramref name="path"/>.</returns>
    public static string Write(
        string path,
        GridGeoreference grid,
        BathymetryRow[] values,
        IReadOnlyDictionary<string, object> rootAttributes,
        IReadOnlyDictionary<string, object>? instanceAttributes = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(rootAttributes);
        if (values.Length != grid.NumPointsLatitudinal * grid.NumPointsLongitudinal)
        {
            throw new ArgumentException("The values don't match the grid size.", nameof(values));
        }

        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = grid.OriginLatitude,
                ["gridOriginLongitude"] = grid.OriginLongitude,
                ["gridSpacingLatitudinal"] = grid.SpacingLatitudinal,
                ["gridSpacingLongitudinal"] = grid.SpacingLongitudinal,
                ["numPointsLatitudinal"] = grid.NumPointsLatitudinal,
                ["numPointsLongitudinal"] = grid.NumPointsLongitudinal,
                ["numGRP"] = 1,
            },
            ["Group_001"] = new H5Group { ["values"] = values },
        };
        foreach (var (name, value) in instanceAttributes ?? new Dictionary<string, object>())
        {
            instance.Attributes[name] = value;
        }

        var feature = new H5Group
        {
            Attributes = new()
            {
                ["dataCodingFormat"] = (byte)2,
                ["dimension"] = (byte)2,
                ["numInstances"] = (byte)1,
            },
            ["BathymetryCoverage.01"] = instance,
        };

        var file = new H5File { ["BathymetryCoverage"] = feature };
        foreach (var (name, value) in rootAttributes)
        {
            file.Attributes[name] = value;
        }

        file.Write(path, new H5WriteOptions(FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name));
        return path;
    }
}
