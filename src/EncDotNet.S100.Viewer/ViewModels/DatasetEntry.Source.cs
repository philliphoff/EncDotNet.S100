using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services.Sources;

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
    /// True for a file. Source rows hide what only makes sense for a file
    /// (data source design note D8): reorder controls and the Validation tab.
    /// </summary>
    public bool IsFile => SourceId is null;

    private SourceStatus? _sourceStatus;

    /// <summary>What a loaded source is doing, such as "Live · updated 3 min ago"; <see langword="null"/> for a file or before it loads.</summary>
    public SourceStatus? SourceStatus
    {
        get => _sourceStatus;
        set
        {
            if (Equals(_sourceStatus, value))
                return;
            _sourceStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(SubtitleTooltip));
            OnPropertyChanged(nameof(NeedsAttention));
        }
    }

    /// <summary>The row's second line: a source's status, or a file's product.</summary>
    public string Subtitle => _sourceStatus?.Text ?? ProductSpec;

    /// <summary>The second line's tooltip: a failing source's error, else the line itself.</summary>
    public string SubtitleTooltip => _sourceStatus?.Detail ?? Subtitle;

    /// <summary>True when a source is stale, failing or needs settings, so its row's status stands out.</summary>
    public bool NeedsAttention => _sourceStatus?.Kind is SourceStatusKind.Stale or SourceStatusKind.Failed or SourceStatusKind.NeedsSettings;

    /// <summary>A source's group, such as "Observations"; <see langword="null"/> for a file.</summary>
    public string? SourceGroupLabel { get; private init; }

    /// <summary>True when the row shows a group label.</summary>
    public bool HasSourceGroup => SourceGroupLabel is not null;

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
            SourceGroupLabel = Services.Sources.SourceStatus.GroupLabel(registration.Group),
        };
    }

    /// <summary>The <see cref="Id"/> of the entry for the source <paramref name="sourceId"/>.</summary>
    /// <param name="sourceId">The source's registration id.</param>
    public static MapDatasetId SourceDatasetId(string sourceId) => new(SourceIdPrefix + sourceId);
}
