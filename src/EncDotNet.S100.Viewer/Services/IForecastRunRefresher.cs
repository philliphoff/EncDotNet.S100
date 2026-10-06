using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Looks for newer forecast runs: the Timeline's "Check for new runs" once
/// every loaded forecast has ended (#713 B3).
/// </summary>
internal interface IForecastRunRefresher
{
    /// <summary>True when the Library has a forecast source to refresh.</summary>
    bool HasForecastSources { get; }

    /// <summary>Refreshes every forecast source, as the Library's Refresh does for each.</summary>
    void RefreshForecastSources();
}

/// <summary>Default <see cref="IForecastRunRefresher"/> over the Library's forecast feeds.</summary>
internal sealed class LibraryForecastRunRefresher(CollectionLibrary library) : IForecastRunRefresher
{
    private readonly CollectionLibrary _library = library ?? throw new ArgumentNullException(nameof(library));

    /// <inheritdoc />
    public bool HasForecastSources => ForecastSources().Any();

    /// <inheritdoc />
    public void RefreshForecastSources()
    {
        foreach (var (collection, source) in ForecastSources().ToArray())
            _library.Refresh(collection, source);
    }

    private IEnumerable<(Guid Collection, Guid Source)> ForecastSources() =>
        _library.Collections
            .Where(c => !c.IsSession)
            .SelectMany(c => c.Sources
                .Where(s => s.Definition is S100ForecastFeedSource)
                .Select(s => (c.Id, s.Id)));
}
