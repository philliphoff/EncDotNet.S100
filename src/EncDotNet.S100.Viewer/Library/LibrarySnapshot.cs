using EncDotNet.S100.Collections;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// An immutable view of one collection in the library, with the current
/// indexing state of each of its sources.
/// </summary>
/// <param name="Definition">The persisted collection definition.</param>
/// <param name="Sources">The collection's sources, in definition order.</param>
/// <param name="IsSession">
/// True for the transient "Session" collection holding S-128 datasets loaded
/// this session; it is never persisted.
/// </param>
internal sealed record LibraryCollection(
    DatasetCollection Definition,
    IReadOnlyList<LibrarySource> Sources,
    bool IsSession = false)
{
    /// <summary>The collection's id.</summary>
    public Guid Id => Definition.Id;

    /// <summary>The number of indexed items across every source.</summary>
    public int ItemCount => Sources.Sum(s => s.Index?.Items.Count ?? 0);

    /// <summary>True while any source is being indexed.</summary>
    public bool IsIndexing => Sources.Any(s => s.State == LibrarySourceState.Indexing);
}

/// <summary>An immutable view of one collection source and its index.</summary>
/// <param name="Definition">The persisted source definition.</param>
/// <param name="Index">The latest index, or <see langword="null"/> before the first one completes.</param>
/// <param name="State">The indexing state.</param>
/// <param name="Error">Why the last indexing attempt failed, when <see cref="State"/> is <see cref="LibrarySourceState.Failed"/>.</param>
internal sealed record LibrarySource(
    CollectionSource Definition,
    SourceIndex? Index,
    LibrarySourceState State,
    string? Error = null)
{
    /// <summary>The source's id.</summary>
    public Guid Id => Definition.Id;
}

/// <summary>The indexing state of a <see cref="LibrarySource"/>.</summary>
internal enum LibrarySourceState
{
    /// <summary>Waiting for its first index.</summary>
    Pending,

    /// <summary>Being indexed now.</summary>
    Indexing,

    /// <summary>Indexed (possibly with diagnostics).</summary>
    Ready,

    /// <summary>The last indexing attempt threw; any previous index is kept.</summary>
    Failed,
}
