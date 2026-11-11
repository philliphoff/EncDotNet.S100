using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// What every Library row reads its state through (#1396): one shared
/// instance per list instead of a set of services per row. It says whether an
/// item is open or downloaded, gives the clock and the Local/UTC setting, and
/// carries the row's Retry and Cancel.
/// </summary>
internal sealed class LibraryRowContext
{
    /// <param name="time">The clock (forecast ages, staleness).</param>
    /// <param name="timeFormat">The Local/UTC setting; <see langword="null"/> shows UTC.</param>
    /// <param name="loadState">Says whether an item is open; <see langword="null"/> for rows that never open.</param>
    /// <param name="downloader">Says whether an item is downloaded, and cancels its download; <see langword="null"/> for rows that never download.</param>
    /// <param name="retry">Downloads a row again after a failure; <see langword="null"/> hides the Failed tag's retry.</param>
    public LibraryRowContext(
        TimeProvider time,
        ITimeFormatProvider? timeFormat,
        Func<CollectionItem, LibraryLoadState>? loadState,
        ILibraryDownloader? downloader,
        Func<LibraryItemViewModel, Task>? retry)
    {
        ArgumentNullException.ThrowIfNull(time);
        Time = time;
        TimeFormat = timeFormat;
        LoadState = loadState;
        Downloader = downloader;
        Retry = retry;
    }

    /// <summary>A context for rows that only show text, such as package and AML section headers: no state, no downloads.</summary>
    /// <param name="time">The clock.</param>
    public static LibraryRowContext Detached(TimeProvider time) => new(time, null, null, null, null);

    /// <summary>The clock (forecast ages, staleness).</summary>
    public TimeProvider Time { get; }

    /// <summary>The Local/UTC setting; <see langword="null"/> shows UTC.</summary>
    public ITimeFormatProvider? TimeFormat { get; }

    /// <summary>Says whether an item is open.</summary>
    public Func<CollectionItem, LibraryLoadState>? LoadState { get; }

    /// <summary>Says whether an item is downloaded, and how its download goes.</summary>
    public ILibraryDownloader? Downloader { get; }

    /// <summary>Downloads a row again after a failure.</summary>
    public Func<LibraryItemViewModel, Task>? Retry { get; }

    /// <summary>The Local/UTC setting now (UTC without one).</summary>
    public TimeFormat CurrentTimeFormat() => TimeFormat?.Current ?? Viewer.TimeFormat.Utc;
}
