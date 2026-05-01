using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S411;

/// <summary>
/// Exposes an <see cref="S411Dataset"/>'s source GML to the vector portrayal
/// pipeline. The dataset's original XML is passed through unchanged so that
/// the official S-411 portrayal-catalogue stylesheets — which target the
/// <c>ice:IceDataSet</c> / <c>ice:IceFeatureMember</c> / <c>ice:&lt;class&gt;</c>
/// shape — can match against it directly. No projection into the framework's
/// neutral <c>&lt;Dataset&gt;&lt;Features&gt;</c> shape is performed.
/// </summary>
public sealed class S411FeatureXmlSource : IFeatureXmlSource
{
    private readonly S411Dataset _dataset;
    private IReadOnlyList<string>? _featureTypes;

    /// <summary>
    /// Initializes a new <see cref="S411FeatureXmlSource"/> wrapping the given dataset.
    /// </summary>
    public S411FeatureXmlSource(S411Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        _dataset = dataset;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> FeatureTypesPresent
    {
        get
        {
            if (_featureTypes is not null) return _featureTypes;

            var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in _dataset.Features)
                types.Add(f.FeatureType);

            _featureTypes = [.. types];
            return _featureTypes;
        }
    }

    /// <inheritdoc/>
    public System.Xml.XmlReader GetFeatureXml() => _dataset.SourceDocument.CreateReader();
}
