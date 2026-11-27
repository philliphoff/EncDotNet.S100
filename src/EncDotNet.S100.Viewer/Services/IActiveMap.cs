using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Which map the user is working in (split map D6): what services that follow
/// the active pane, rather than serve every pane, read. <see cref="MapViews"/>
/// implements it.
/// </summary>
internal interface IActiveMap
{
    /// <summary>The active map's id, or <see langword="null"/> when there's no map.</summary>
    MapViewId? ActiveId { get; }

    /// <summary>Raised after the active map changes.</summary>
    event EventHandler? ActiveChanged;
}

/// <summary>Visibility as the active pane sees it.</summary>
internal static class ActiveMapExtensions
{
    /// <summary>
    /// Whether the active map shows <paramref name="entry"/> (split map step 2
    /// §2.6): the Legend, DEPTHS, the AML product counts and feedback reports
    /// follow the active pane. With no map, whether any map shows it.
    /// </summary>
    /// <param name="map">The active map.</param>
    /// <param name="entry">The dataset.</param>
    /// <returns><see langword="true"/> when the active map shows the dataset.</returns>
    public static bool Shows(this IActiveMap map, DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(entry);
        return map.ActiveId is { } active ? entry.IsVisibleIn(active) : entry.IsVisibleAnywhere;
    }
}
