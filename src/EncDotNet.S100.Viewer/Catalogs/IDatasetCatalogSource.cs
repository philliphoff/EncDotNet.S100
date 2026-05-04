using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Viewer.Catalogs;

/// <summary>
/// A pluggable supplier of <see cref="DatasetCatalogEntry"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// The Dataset Catalog panel consumes one or more sources via a
/// <see cref="DatasetCatalogAggregator"/>. Implementations include the
/// in-process adapter over loaded S-128 datasets and may include
/// future JSON-file or online sources.
/// </para>
/// <para>
/// Sources are expected to publish a coarse <see cref="Changed"/>
/// notification when their <see cref="Entries"/> snapshot changes; the
/// panel re-queries the entries on each event rather than relying on
/// item-level diffs.
/// </para>
/// </remarks>
internal interface IDatasetCatalogSource
{
    /// <summary>A stable id for this source (used for grouping and entry namespacing).</summary>
    string Id { get; }

    /// <summary>A human-readable label for the source (shown in the panel UI).</summary>
    string DisplayName { get; }

    /// <summary>The current entries surfaced by this source.</summary>
    IReadOnlyList<DatasetCatalogEntry> Entries { get; }

    /// <summary>
    /// Raised whenever <see cref="Entries"/> changes. Listeners should re-read
    /// the snapshot rather than diff individual items.
    /// </summary>
    event EventHandler<DatasetCatalogChangedEventArgs>? Changed;
}

/// <summary>
/// Event arguments raised by an <see cref="IDatasetCatalogSource"/> when its
/// <see cref="IDatasetCatalogSource.Entries"/> snapshot changes.
/// </summary>
internal sealed class DatasetCatalogChangedEventArgs : EventArgs
{
    /// <summary>The source whose contents changed.</summary>
    public IDatasetCatalogSource Source { get; }

    /// <summary>Initializes a new instance.</summary>
    public DatasetCatalogChangedEventArgs(IDatasetCatalogSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }
}
