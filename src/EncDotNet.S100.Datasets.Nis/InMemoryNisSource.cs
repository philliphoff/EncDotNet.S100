namespace EncDotNet.S100.Datasets.Nis;

/// <summary>
/// An <see cref="INisSource"/> over features and relationships held in
/// memory, for tests and for callers that build the model themselves.
/// </summary>
public sealed class InMemoryNisSource : INisSource
{
    private readonly IReadOnlyList<NisFeature> _features;
    private readonly IReadOnlyList<NisRelationship> _relationships;

    /// <summary>Creates a source.</summary>
    /// <param name="features">The features, including collections.</param>
    /// <param name="relationships">The relationships between them.</param>
    public InMemoryNisSource(IEnumerable<NisFeature> features, IEnumerable<NisRelationship>? relationships = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        _features = features.ToList();
        _relationships = relationships?.ToList() ?? [];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetDatasetNames()
        => _features
            .Select(f => f.DatasetName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <inheritdoc />
    public IEnumerable<NisFeature> GetFeatures(NisQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _features.Where(query.Matches);
    }

    /// <inheritdoc />
    public IEnumerable<NisRelationship> GetRelationships() => _relationships;
}
