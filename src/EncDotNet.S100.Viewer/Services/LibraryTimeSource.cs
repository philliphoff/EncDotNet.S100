using Avalonia.Threading;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>Where a Library dataset's data is, for the Timeline's bands (#711, handoff E2).</summary>
internal enum LibraryTimedState
{
    /// <summary>Online, not downloaded: a dashed band.</summary>
    Online,

    /// <summary>Downloaded, not loaded: an outlined band.</summary>
    OnDisk,

    /// <summary>Loaded on the map: drawn by its loaded lane.</summary>
    Loaded,
}

/// <summary>
/// One window of data the Library knows about (#711, handoff G1): a forecast
/// run's valid window, or a dataset's time coverage, with where it is.
/// </summary>
/// <param name="ItemId">The Library item id (<c>&lt;sourceId&gt;:&lt;key&gt;</c>), as the Library MCP tools use.</param>
/// <param name="Name">The dataset name, e.g. <c>111US00_CBOFS_US4MD1DD</c>.</param>
/// <param name="Spec">The product specification.</param>
/// <param name="Start">The window's start (UTC).</param>
/// <param name="End">The window's end (UTC).</param>
/// <param name="State">Where the data is.</param>
internal sealed record LibraryTimedEntry(string ItemId, string Name, string Spec, DateTime Start, DateTime End, LibraryTimedState State)
{
    /// <summary>The forecast run, if any (UTC).</summary>
    public DateTime? Run { get; init; }

    /// <summary>The forecast model (e.g. <c>cbofs</c>), if any.</summary>
    public string? Model { get; init; }

    /// <summary>True for a newer run online than the copy on disk ("New run").</summary>
    public bool IsNewRun { get; init; }

    /// <summary>True when the copy on disk is a forecast whose window has ended ("Expired").</summary>
    public bool IsExpired { get; init; }

    /// <summary>The download size, for online data.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>The dataset's footprint, if known.</summary>
    public GeoBounds? Bounds { get; init; }

    /// <summary>What matches the entry to a loaded dataset: <see cref="KeyOf"/> of its name.</summary>
    public string MatchKey => KeyOf(Name);

    /// <summary>
    /// The key matching a Library dataset to a loaded one: a tiled forecast's
    /// model and tile (whatever run the file name carries), else the name
    /// without its extension.
    /// </summary>
    public static string KeyOf(string name) =>
        ForecastRunNames.ModelAndTile(Path.GetFileNameWithoutExtension(name)) is { } tile
            ? $"{tile.Model}/{tile.Tile}"
            : Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
}

/// <summary>
/// The time windows the Library knows about, from its cached indexes, so the
/// Timeline can show data that is online or on disk but not loaded and act on
/// it (#711, handoff E2, E5, G1, G3).
/// </summary>
internal interface ILibraryTimeSource
{
    /// <summary>Every known window.</summary>
    IReadOnlyList<LibraryTimedEntry> Entries { get; }

    /// <summary>Raised when the entries change (an index, a download, a load, the clock).</summary>
    event Action? Changed;

    /// <summary>Raised as downloads progress.</summary>
    event Action? ProgressChanged;

    /// <summary>The fraction downloaded while the entry's data downloads, else null.</summary>
    double? ProgressOf(LibraryTimedEntry entry);

    /// <summary>Downloads the entries' data, then loads it ("Get").</summary>
    Task GetAsync(IReadOnlyList<LibraryTimedEntry> entries);

    /// <summary>Loads the entries' data from disk ("Load").</summary>
    Task LoadAsync(IReadOnlyList<LibraryTimedEntry> entries);

    /// <summary>Shows the entry's dataset in the Library panel ("Reveal in Library").</summary>
    void Reveal(LibraryTimedEntry entry);
}

/// <summary>
/// <see cref="ILibraryTimeSource"/> over the Library panel's rows, so each
/// entry's state is exactly what the Library shows. Entries are rebuilt
/// lazily after a change.
/// </summary>
internal sealed class LibraryTimeSource : ILibraryTimeSource
{
    private readonly LibraryPanelViewModel _panel;
    private readonly Action<Action> _dispatch;
    private readonly Dictionary<string, LibraryItemViewModel> _rows = new(StringComparer.Ordinal);
    private readonly ITimer _clock;
    private IReadOnlyList<LibraryTimedEntry>? _entries;
    private bool _changePosted;

    public LibraryTimeSource(LibraryPanelViewModel panel, LibraryService library, ILibraryLoader loader, TimeProvider clock, Action<Action>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(clock);
        _panel = panel;
        _dispatch = dispatch ?? PostToUiThread;
        library.Changed += (_, _) => Invalidate();
        loader.Changed += (_, _) => Invalidate();
        panel.Downloader.Changed += (_, _) => Invalidate();
        panel.Downloader.ProgressChanged += (_, _) => _dispatch(() => ProgressChanged?.Invoke());

        // Runs expire by the clock.
        _clock = clock.CreateTimer(_ => Invalidate(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public event Action? ProgressChanged;

    /// <inheritdoc />
    public IReadOnlyList<LibraryTimedEntry> Entries => _entries ??= Build();

    /// <inheritdoc />
    public double? ProgressOf(LibraryTimedEntry entry) =>
        _rows.TryGetValue(entry.ItemId, out var row)
        && _panel.Downloader.StatusOf(row.Item) is { State: LibraryDownloadItemState.Running or LibraryDownloadItemState.Queued } status
            ? status.Fraction ?? 0d
            : null;

    /// <inheritdoc />
    public Task GetAsync(IReadOnlyList<LibraryTimedEntry> entries)
    {
        var rows = Rows(entries).Where(row => row.CanDownload).ToArray();
        return rows.Length == 0 ? Task.CompletedTask : _panel.DownloadRowsAsync(rows, load: true);
    }

    /// <inheritdoc />
    public Task LoadAsync(IReadOnlyList<LibraryTimedEntry> entries)
    {
        var rows = Rows(entries).Where(row => row.CanLoad).ToArray();
        return rows.Length == 0 ? Task.CompletedTask : _panel.LoadRowsAsync(rows, defer: false);
    }

    /// <inheritdoc />
    public void Reveal(LibraryTimedEntry entry)
    {
        if (!_rows.TryGetValue(entry.ItemId, out var row))
            return;
        _panel.Reveal(row.Source.Id, row.Item.Key);
    }

    private IEnumerable<LibraryItemViewModel> Rows(IEnumerable<LibraryTimedEntry> entries) =>
        entries.Select(e => _rows.GetValueOrDefault(e.ItemId)).OfType<LibraryItemViewModel>().Distinct();

    private void Invalidate()
    {
        _entries = null;
        if (_changePosted)
            return;
        _changePosted = true;
        _dispatch(() =>
        {
            _changePosted = false;
            Changed?.Invoke();
        });
    }

    private IReadOnlyList<LibraryTimedEntry> Build()
    {
        _rows.Clear();
        var entries = new List<LibraryTimedEntry>();
        foreach (var source in _panel.Collections.SelectMany(c => c.Sources))
        {
            foreach (var item in source.Index?.Items ?? [])
            {
                var forecast = ForecastRuns.IsForecast(item);
                if (!forecast && LibraryItemViewModel.TimeCoverage(item) is null)
                    continue;
                var row = _panel.CreateItem(item, source);
                var id = $"{source.Id}:{item.Key}";
                _rows[id] = row;
                entries.AddRange(EntriesOf(row, id));
            }
        }
        return entries;
    }

    /// <summary>
    /// A row's windows: its copy (on disk or loaded; Expired once ended), and
    /// a newer run online (New run) or the online run when there is no copy.
    /// </summary>
    internal static IEnumerable<LibraryTimedEntry> EntriesOf(LibraryItemViewModel row, string id)
    {
        var item = row.Item;
        var size = (item.Location as RemoteItemLocation)?.SizeBytes;
        var model = ForecastRuns.ModelOf(item);
        LibraryTimedEntry Entry(DateTime start, DateTime end, LibraryTimedState state) =>
            new(id, item.Name, item.ProductSpec, start, end, state) { Model = model, Bounds = item.Bounds };

        var availability = row.Availability;
        var local = availability is not LibraryAvailability.Online and not LibraryAvailability.Listed and not LibraryAvailability.Missing;
        var copyState = row.IsLoadedNow ? LibraryTimedState.Loaded : LibraryTimedState.OnDisk;
        if (!row.IsForecast)
        {
            if (row.ValidWindow is { } window && availability is not LibraryAvailability.Listed and not LibraryAvailability.Missing)
            {
                yield return Entry(window.Start, window.End, local ? copyState : LibraryTimedState.Online) with
                {
                    SizeBytes = local ? null : size,
                };
            }
            yield break;
        }

        if (local && row.ValidWindow is { } copy)
        {
            yield return Entry(copy.Start, copy.End, copyState) with
            {
                Run = row.LocalRunTime ?? copy.Start,
                IsExpired = availability == LibraryAvailability.Expired,
            };
        }
        if (row.CatalogueRun is { } online && (!local || availability == LibraryAvailability.Outdated))
        {
            yield return Entry(online.Run, online.End, LibraryTimedState.Online) with
            {
                Run = online.Run,
                IsNewRun = local,
                SizeBytes = size,
            };
        }
    }

    private static void PostToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
