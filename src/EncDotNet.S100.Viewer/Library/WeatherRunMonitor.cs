using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Weather;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Keeps loaded weather up to date on its own (#1301, #1395; design note
/// vector-weather.md §6.4). A source whose service refreshes on its own
/// (<see cref="WeatherService.RefreshHours"/>: WPC's surface analysis, every
/// 3 hours) is re-read once that long has passed since its last index while
/// its latest item is loaded, and a newer item is downloaded and loaded in
/// its place. NHC's storms are re-read the same way, but a new advisory is
/// announced rather than loaded (<see cref="StormAdvisoryWatch"/>).
/// </summary>
/// <remarks>Checks once a minute, on the UI thread.</remarks>
internal sealed class WeatherRunMonitor : IDisposable
{
    private readonly CollectionLibrary _library;
    private readonly LibraryRows _rows;
    private readonly LoadLatestWeatherRunWorkflow _loadLatest;
    private readonly StormAdvisoryWatch _storms;
    private readonly TimeProvider _time;
    private readonly ITimer _clock;
    private bool _refreshing;

    /// <summary>Creates the monitor and starts its minute clock.</summary>
    /// <param name="library">The Library the weather sources are in.</param>
    /// <param name="rows">Says which items are loaded.</param>
    /// <param name="loadLatest">Replaces a loaded item with the newer one.</param>
    /// <param name="storms">Announces new advisories of loaded storms.</param>
    /// <param name="time">The clock.</param>
    /// <param name="dispatch">Runs each check on the UI thread.</param>
    public WeatherRunMonitor(
        CollectionLibrary library,
        LibraryRows rows,
        LoadLatestWeatherRunWorkflow loadLatest,
        StormAdvisoryWatch storms,
        TimeProvider time,
        Action<Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(loadLatest);
        ArgumentNullException.ThrowIfNull(storms);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(dispatch);
        _library = library;
        _rows = rows;
        _loadLatest = loadLatest;
        _storms = storms;
        _time = time;
        _clock = time.CreateTimer(_ => dispatch(() => _ = RefreshDueAsync()), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Refreshes the weather sources that refresh on their own and are due: a
    /// <see cref="WeatherService.RefreshHours"/> since their last index, with
    /// their latest (unpinned) item loaded.
    /// </summary>
    /// <returns>The sources refreshed.</returns>
    public async Task<IReadOnlyList<Guid>> RefreshDueAsync()
    {
        if (_refreshing)
            return [];
        _refreshing = true;
        try
        {
            var now = _time.GetUtcNow();
            var due = _library.Collections
                .SelectMany(c => c.Sources)
                .Where(s => s.Definition is WeatherFeedSource { Service.RefreshHours: > 0 } feed
                    && s.Index is { } index
                    && now - index.IndexedAt >= TimeSpan.FromHours(feed.Service.RefreshHours))
                .Select(s => (s.Id, Keys: s.Index!.Items
                    .Where(i => !WeatherRuns.IsPinned(i) && _rows.IsOpen(i))
                    .Select(i => i.Key)
                    .ToHashSet(StringComparer.Ordinal)))
                .Where(s => s.Keys.Count > 0)
                .ToArray();
            foreach (var (id, keys) in due)
            {
                if (_storms.IsStormSource(id))
                    await _storms.CheckAsync(id, keys).ConfigureAwait(true);
                else
                    await _loadLatest.RunAsync(id, keys).ConfigureAwait(true);
            }

            return [.. due.Select(d => d.Id)];
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _clock.Dispose();
}
