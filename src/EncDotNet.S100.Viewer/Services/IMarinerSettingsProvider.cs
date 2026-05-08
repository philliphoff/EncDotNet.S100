using System;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Supplies the current user-configured <see cref="MarinerSettings"/>
/// snapshot to the dataset pipeline (S-100 Part 9 §4.2). Implementations
/// observe the source view-model and rebuild <see cref="Current"/>
/// whenever any underlying property changes.
/// </summary>
internal interface IMarinerSettingsProvider
{
    /// <summary>The latest snapshot. Always non-null.</summary>
    MarinerSettings Current { get; }

    /// <summary>
    /// Raised after <see cref="Current"/> has been rebuilt. Subscribers
    /// (e.g. <c>DatasetLoaderService</c>) re-render all loaded datasets in
    /// response. The new snapshot is supplied as an argument so
    /// subscribers don't need to read <see cref="Current"/> separately.
    /// </summary>
    event Action<MarinerSettings>? Changed;
}
