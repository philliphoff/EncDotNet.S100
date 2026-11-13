using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed partial class DatasetEntry
{
    /// <summary>The prefix of a source entry's <see cref="Id"/>.</summary>
    public const string SourceIdPrefix = "source:";

    /// <summary>
    /// The id of the <see cref="SourceRegistration"/> this entry was built
    /// from, such as <c>ndbc.realtime</c>, or <see langword="null"/> for a
    /// file (design note D3). A source entry has no <see cref="FilePath"/>.
    /// </summary>
    public string? SourceId { get; private init; }

    /// <summary>The options the source's processor is built with; <see langword="null"/> for the registration's defaults.</summary>
    public object? SourceOptions { get; private init; }

    /// <summary>True when the entry is a data source rather than a file.</summary>
    public bool IsSource => SourceId is not null;

    /// <summary>
    /// Creates an entry for a source. Its id is <c>source:</c> and the
    /// registration's id, so a source is listed at most once.
    /// </summary>
    /// <param name="registration">The source.</param>
    /// <param name="options">Its options; <see langword="null"/> for the registration's defaults.</param>
    public static DatasetEntry ForSource(SourceRegistration registration, object? options = null)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return new DatasetEntry(
            filePath: string.Empty,
            productSpec: registration.Spec,
            source: null,
            relativePath: null,
            displayName: registration.DisplayName,
            id: SourceDatasetId(registration.Id))
        {
            SourceId = registration.Id,
            SourceOptions = options,
        };
    }

    /// <summary>The <see cref="Id"/> of the entry for the source <paramref name="sourceId"/>.</summary>
    /// <param name="sourceId">The source's registration id.</param>
    public static MapDatasetId SourceDatasetId(string sourceId) => new(SourceIdPrefix + sourceId);
}
