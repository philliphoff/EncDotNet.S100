namespace EncDotNet.S100.Datasets.Nis;

/// <summary>
/// One feature of an S-57 Nautical Information System: a row of one of its
/// feature classes or of its collections table, with the storage details of
/// the source already decoded.
/// </summary>
/// <remarks>
/// <para>
/// A source decodes how the row is stored: the subtype code becomes the
/// S-57 object class acronym, and the shape becomes a <see cref="NisGeometry"/>.
/// It does not interpret NIS conventions, such as list values held as
/// comma-separated text or <c>-32767</c> for an unknown value; the translator
/// does that, the same way for every source.
/// </para>
/// <para>
/// <see cref="Attributes"/> holds every field the row has a value in, by
/// field name, including the Esri bookkeeping fields. Values are
/// <see cref="string"/>, <see cref="int"/>, <see cref="long"/>,
/// <see cref="double"/>, <see cref="DateTime"/> or <see cref="Guid"/>.
/// </para>
/// </remarks>
public sealed class NisFeature
{
    /// <summary>The name of the feature class or table the row is in, such as <c>AidsToNavigationP</c>.</summary>
    public required string FeatureClass { get; init; }

    /// <summary>The S-57 object class acronym, such as <c>BOYLAT</c>, <c>M_COVR</c> or <c>C_AGGR</c>.</summary>
    public required string ObjectClass { get; init; }

    /// <summary>The row's object id in its feature class.</summary>
    public long ObjectId { get; init; }

    /// <summary>
    /// The feature's S-57 long name as the NIS stores it (<c>LNAM</c>), such as
    /// <c>US119234008300003</c>, or <see langword="null"/> when it has none.
    /// See <see cref="NisLongName"/>.
    /// </summary>
    public string? LongName { get; init; }

    /// <summary>The name of the cell the feature came from (<c>DSNM</c>), such as <c>US5CA98M.005</c>.</summary>
    public string? DatasetName { get; init; }

    /// <summary>The compilation scale the feature was captured at (<c>PLTS_COMP_SCALE</c>).</summary>
    public int? CompilationScale { get; init; }

    /// <summary>Whether the row is marked deleted (<c>IS_DELETE</c>).</summary>
    public bool IsDeleted { get; init; }

    /// <summary>The geometry, or <see langword="null"/> for a collection or a feature without one.</summary>
    public NisGeometry? Geometry { get; init; }

    /// <summary>The values of the row's fields, by field name; fields with no value are absent.</summary>
    public IReadOnlyDictionary<string, object> Attributes { get; init; } = new Dictionary<string, object>();
}
