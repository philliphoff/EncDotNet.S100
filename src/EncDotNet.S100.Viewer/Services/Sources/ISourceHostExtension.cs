using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Sources;

/// <summary>
/// SoundCharts behaviour layered over one kind of source while its entry is
/// loaded, such as forwarding the own-ship fix to NMEA weather, or the vessel
/// list and pirate mode following AIS (data source design note D4: add-ons
/// stay in SoundCharts, not in the registration).
/// </summary>
internal interface ISourceHostExtension
{
    /// <summary>The registration id of the source this extension follows, such as <c>nmea.weather</c>.</summary>
    string SourceId { get; }

    /// <summary>
    /// Starts following a loaded entry of the source. Disposing the result
    /// stops it; the entry is about to be removed or unloaded.
    /// </summary>
    /// <param name="entry">The loaded entry.</param>
    /// <param name="processor">Its processor.</param>
    /// <returns>What to dispose when the entry goes, or <see langword="null"/> when there's nothing to follow.</returns>
    IDisposable? Attach(DatasetEntry entry, IDatasetProcessor processor);
}
