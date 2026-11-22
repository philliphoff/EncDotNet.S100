namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// How a <see cref="ReferenceHit"/> matched the query of
/// <see cref="ChartReferenceIndex.Search"/>. Lower values rank first.
/// </summary>
public enum ReferenceMatch
{
    /// <summary>The query is the entry's code or S-57 acronym.</summary>
    ExactCode,

    /// <summary>The entry's name or code starts with the query.</summary>
    NamePrefix,

    /// <summary>
    /// The query occurs in other text: the name, the definition, a symbol
    /// description, or the value labels of the entry's rule conditions.
    /// </summary>
    Text,
}
