using System.Globalization;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Adapter that exposes the geometry of an <see cref="S101Dataset"/> through the
/// product-agnostic <see cref="IFeatureGeometryProvider"/> contract used by the
/// unified Mapsui display-list renderer.
/// </summary>
/// <remarks>
/// S-101 feature references emitted by the Lua portrayal are the integer
/// <c>RecordId</c> of the FRID record formatted as a decimal string.
/// </remarks>
public sealed class S101FeatureGeometryProvider : IFeatureGeometryProvider
{
    private readonly Dictionary<long, FeatureGeometry> _byId;

    /// <summary>
    /// Builds a provider over the supplied dataset by enumerating its vector features.
    /// </summary>
    public S101FeatureGeometryProvider(S101Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var source = new S101VectorSource(dataset);
        var features = source.GetFeatures();
        _byId = new Dictionary<long, FeatureGeometry>(features.Count);
        foreach (var f in features)
        {
            _byId[f.Id] = new FeatureGeometry
            {
                Type = f.GeometryType,
                Coordinates = f.Coordinates,
            };
        }
    }

    /// <inheritdoc />
    public FeatureGeometry? GetGeometry(string featureReference)
    {
        if (!long.TryParse(featureReference, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return null;
        return _byId.TryGetValue(id, out var g) ? g : null;
    }
}
