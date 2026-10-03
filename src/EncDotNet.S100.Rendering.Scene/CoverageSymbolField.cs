using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// The oriented symbols (e.g. S-111 current arrows) of one styled coverage
/// grid, flattened to row-major arrays in EPSG:3857: one entry per grid cell,
/// with the resolved symbol band, the band's symbol scale, the rotation and the
/// thinning priority. Shared by the Mapsui and headless Skia arrow renderers so
/// both place, size and thin the same arrows.
/// </summary>
public sealed class CoverageSymbolField
{
    private CoverageSymbolField(int rows, int cols)
    {
        Rows = rows;
        Cols = cols;
        int count = rows * cols;
        X = new double[count];
        Y = new double[count];
        Scale = new float[count];
        Rotation = new float[count];
        Priority = new float[count];
        Bands = new SymbolBand?[count];
    }

    /// <summary>Number of grid rows.</summary>
    public int Rows { get; }

    /// <summary>Number of grid columns.</summary>
    public int Cols { get; }

    /// <summary>Per-cell EPSG:3857 x of the cell centre.</summary>
    public double[] X { get; }

    /// <summary>Per-cell EPSG:3857 y of the cell centre.</summary>
    public double[] Y { get; }

    /// <summary>
    /// Per-cell symbol scale from the band (<see cref="SymbolBand.ScaleFor"/>),
    /// NaN for a cell without a symbol (no data, or no band for the value).
    /// </summary>
    public float[] Scale { get; }

    /// <summary>Per-cell rotation, degrees clockwise from north.</summary>
    public float[] Rotation { get; }

    /// <summary>Per-cell thinning priority: the band-selecting value (for S-111, the speed).</summary>
    public float[] Priority { get; }

    /// <summary>Per-cell resolved symbol band, null for a cell without a symbol.</summary>
    public SymbolBand?[] Bands { get; }

    /// <summary>
    /// Builds the field for <paramref name="layer"/>'s symbol scheme.
    /// </summary>
    /// <param name="layer">A styled coverage layer with a <see cref="StyledCoverageLayer.SymbolScheme"/>.</param>
    /// <param name="nativeToWgs84">Transform from the grid's native CRS to WGS84.</param>
    /// <returns>The field; every cell is marked symbol-less when the layer has no symbol scheme.</returns>
    public static CoverageSymbolField Build(StyledCoverageLayer layer, ICrsTransform nativeToWgs84)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(nativeToWgs84);

        var scheme = layer.SymbolScheme;
        var sampled = layer.Coverage;
        if (scheme is null)
        {
            var empty = new CoverageSymbolField(0, 0);
            return empty;
        }

        var valueData = sampled.GetField(scheme.ValueFieldName);
        var rotationData = sampled.GetField(scheme.RotationFieldName);
        int rows = valueData.GetLength(0);
        int cols = valueData.GetLength(1);
        var field = new CoverageSymbolField(rows, cols);

        float noData = layer.NoDataValue;
        bool noDataIsNaN = float.IsNaN(noData);
        var georeferencer = layer.Georeferencer;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int i = r * cols + c;

                var (nativeX, nativeY) = georeferencer.ToNative(r, c);
                double lon, lat;
                if (nativeToWgs84.IsIdentity) { lon = nativeX; lat = nativeY; }
                else { (lon, lat) = nativeToWgs84.Transform(nativeX, nativeY); }
                (field.X[i], field.Y[i]) = WebMercator.FromLonLat(lon, lat);

                field.Scale[i] = float.NaN;
                field.Priority[i] = float.NaN;

                float value = valueData[r, c];
                float direction = rotationData[r, c];
                bool valueMissing = noDataIsNaN ? float.IsNaN(value) : value == noData;
                bool directionMissing = noDataIsNaN ? float.IsNaN(direction) : direction == noData;

                // S-111 Annex H Rule 2: no arrow where speed or direction is null.
                if (valueMissing || directionMissing)
                    continue;

                var band = scheme.Resolve(value);
                if (band is null)
                    continue;

                double scale = band.ScaleFor(value);
                if (!(scale > 0))
                    continue;

                field.Bands[i] = band;
                field.Scale[i] = (float)scale;
                field.Rotation[i] = direction;
                field.Priority[i] = value;
            }
        }

        return field;
    }
}
