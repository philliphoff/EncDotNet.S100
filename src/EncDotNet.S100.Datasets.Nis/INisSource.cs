namespace EncDotNet.S100.Datasets.Nis;

/// <summary>
/// An S-57 Nautical Information System to read: its features, the
/// relationships between them, and the cells they came from.
/// </summary>
/// <remarks>
/// <para>
/// The NIS model is the same whatever the NIS is stored in, so the
/// translation to S-57 never depends on how it is read. This library reads a
/// file geodatabase with <see cref="FileGdb.FileGdbNisSource"/>; an ArcGIS Pro
/// add-in can implement this interface over Pro's geodatabase API, which also
/// covers enterprise geodatabases, edit versions and unsaved edits.
/// </para>
/// <para>
/// The methods are synchronous. A source over an API with thread affinity,
/// such as Pro's, is called on the thread that API needs.
/// </para>
/// </remarks>
public interface INisSource
{
    /// <summary>
    /// The distinct cell names (<see cref="NisFeature.DatasetName"/>) the
    /// features came from, in ordinal order.
    /// </summary>
    /// <returns>The cell names.</returns>
    IReadOnlyList<string> GetDatasetNames();

    /// <summary>The features that meet a query, including collections.</summary>
    /// <param name="query">Which features to return.</param>
    /// <returns>The features.</returns>
    IEnumerable<NisFeature> GetFeatures(NisQuery query);

    /// <summary>
    /// The relationships between features. A source may return relationships
    /// whose features a query would not return; the translator keeps only
    /// those between features it converts.
    /// </summary>
    /// <returns>The relationships.</returns>
    IEnumerable<NisRelationship> GetRelationships();
}
