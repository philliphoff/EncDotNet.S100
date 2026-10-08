using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Mcp.Tools.Library;

/// <summary>
/// Builds the Library tools' results from the Library core, so every host
/// (the viewer, <c>s100 mcp serve</c>) reports items and sources in the same
/// shape (#792).
/// </summary>
public static class LibraryToolResults
{
    /// <summary>An item as query_library_items reports it.</summary>
    /// <param name="state">The item's state.</param>
    /// <param name="tags">Its tags' text, as its row shows them.</param>
    public static LibraryItemInfo Item(LibraryItemState state, IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(tags);
        var item = state.Item;
        var run = S100ForecastFeedIndexer.RunOf(item);
        var horizon = ForecastRuns.Horizon(item);
        return new LibraryItemInfo(
            state.Id,
            state.Source.Id,
            item.Name,
            item.Title,
            item.ProductSpec,
            LibraryAvailabilityNames.Of(state.Availability),
            [.. tags],
            item.Edition,
            item.Update,
            item.IssueDate,
            item.Location is RemoteItemLocation remote ? remote.SizeBytes : null,
            item.Bounds is { } b ? new LibraryBounds(b.South, b.West, b.North, b.East) : null,
            state.EffectiveItem.Location is LocalItemLocation local ? LibraryAvailabilityResolver.ResolvePath(local) : null,
            ForecastRuns.ModelOf(item),
            run,
            run is { } start && horizon is { } length ? start + length : null,
            state.NotForNavigation);
    }

    /// <summary>An item with its details, as describe_library_item reports it.</summary>
    /// <param name="state">The item's state.</param>
    /// <param name="tags">Its tags' text.</param>
    /// <param name="details">Its details groups.</param>
    public static LibraryItemDetail Detail(LibraryItemState state, IEnumerable<string> tags, IEnumerable<LibraryDetailGroup> details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return new LibraryItemDetail(
            Item(state, tags),
            [.. details.Select(group => new LibraryDetailGroupInfo(
                group.Title,
                [.. group.Fields.Select(field => new LibraryDetailFieldInfo(field.Label, field.Value))]))]);
    }

    /// <summary>An Online Catalogue directory entry, as list_known_sources reports it.</summary>
    /// <param name="source">The known source.</param>
    /// <param name="userAdded">True when the user added it (Custom).</param>
    public static KnownSourceInfo KnownSource(KnownCatalogueSource source, bool userAdded)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new KnownSourceInfo(
            source.Id,
            source.Name,
            source.Provider,
            source.Region,
            source.Format.ToString(),
            source.Format == KnownCatalogueFormat.S100Feed ? LibraryTextFormat.MaskToken(source.CatalogUri) : source.CatalogUri.AbsoluteUri,
            source.Homepage?.AbsoluteUri,
            source.Coverage.ToString(),
            source.Editions,
            source.Sizes,
            source.Product,
            source.Note,
            source.NotForNavigation,
            source.Pilot,
            userAdded,
            [.. source.Models.Select(model => new KnownForecastModelInfo(model.Id, model.Name, model.CadenceHours, model.HorizonHours))]);
    }

    /// <summary>A source's URL as the tools show it (a shared feed's access token masked), or null for a local source.</summary>
    /// <param name="source">The source.</param>
    public static string? SourceUrl(CollectionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return LibraryNodeText.SourceUrl(source) is { } uri
            ? source is S100FeedSource ? LibraryTextFormat.MaskToken(uri) : uri.AbsoluteUri
            : null;
    }

    /// <summary>Item counts by state name.</summary>
    /// <param name="states">The items' availability.</param>
    public static IReadOnlyDictionary<string, int> Counts(IEnumerable<LibraryAvailability> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        return states
            .Select(LibraryAvailabilityNames.Of)
            .GroupBy(state => state, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }
}
