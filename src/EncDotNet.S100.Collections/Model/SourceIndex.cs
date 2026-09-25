namespace EncDotNet.S100.Collections;

/// <summary>
/// The result of indexing one <see cref="CollectionSource"/>: its items plus
/// the fingerprint that decides whether a later refresh can reuse it.
/// </summary>
/// <remarks>
/// An index is derived data: hosts cache it separately from the collection
/// definitions and can always rebuild it by re-indexing the source.
/// </remarks>
/// <param name="SourceId">The indexed source's <see cref="CollectionSource.Id"/>.</param>
/// <param name="IndexedAt">When indexing completed.</param>
/// <param name="Fingerprint">
/// An opaque token describing the source's state when indexed, or
/// <see langword="null"/> when the source cannot be fingerprinted (the index is
/// then always rebuilt on refresh).
/// </param>
/// <param name="Items">The indexed items.</param>
/// <param name="Diagnostics">Problems met while indexing; none are fatal.</param>
public sealed record SourceIndex(
    Guid SourceId,
    DateTimeOffset IndexedAt,
    string? Fingerprint,
    IReadOnlyList<CollectionItem> Items,
    IReadOnlyList<IndexDiagnostic> Diagnostics);

/// <summary>A problem met while indexing a source.</summary>
/// <param name="Severity">How serious the problem is.</param>
/// <param name="Message">A human-readable description.</param>
/// <param name="Path">The file or entry concerned, if any.</param>
public sealed record IndexDiagnostic(IndexDiagnosticSeverity Severity, string Message, string? Path = null);

/// <summary>The severity of an <see cref="IndexDiagnostic"/>.</summary>
public enum IndexDiagnosticSeverity
{
    /// <summary>Informational: something was skipped by design.</summary>
    Info = 0,

    /// <summary>An item is incomplete or a file could not be read.</summary>
    Warning,

    /// <summary>A whole exchange set or file could not be indexed.</summary>
    Error,
}

/// <summary>Progress reported while indexing a source.</summary>
/// <param name="ItemsIndexed">Items indexed so far.</param>
/// <param name="CurrentPath">The file or folder being processed, if any.</param>
public readonly record struct IndexProgress(int ItemsIndexed, string? CurrentPath);
