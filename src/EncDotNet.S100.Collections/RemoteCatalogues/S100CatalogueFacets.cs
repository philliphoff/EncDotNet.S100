using System.Globalization;

namespace EncDotNet.S100.Collections.RemoteCatalogues;

/// <summary>
/// A top-level folder of a remote S-100 catalogue (a region, for NOAA's
/// S-102) with the folders below it that hold datasets (its areas).
/// </summary>
/// <param name="Folder">The region's folder (e.g. <c>Northeast</c>); empty for datasets at the root.</param>
/// <param name="Name">The display name ("Northeast", "Great Lakes Region").</param>
/// <param name="Areas">
/// The dataset folders in the region (e.g. <c>Northeast/Boston</c>) with
/// their dataset counts and known sizes; a region whose datasets lie directly
/// in it has one area, its own folder.
/// </param>
public sealed record S100CatalogueRegion(string Folder, string Name, IReadOnlyList<CatalogFacetValue> Areas);

/// <summary>A navigation purpose found in a remote S-100 catalogue.</summary>
/// <param name="Value">The <c>navigationPurpose</c> value (e.g. <c>port</c>).</param>
/// <param name="Count">How many datasets declare it.</param>
/// <param name="GridResolution">The most common grid resolution among them in metres, if declared.</param>
public sealed record S100CataloguePurpose(string Value, int Count, double? GridResolution);

/// <summary>Summarises a remote S-100 catalogue's items for choosing an <see cref="S100CatalogueFilter"/>.</summary>
public static class S100CatalogueFacets
{
    /// <summary>The catalogue's regions and areas, each sorted by name.</summary>
    public static IReadOnlyList<S100CatalogueRegion> Regions(IEnumerable<CollectionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .GroupBy(RemoteS100Catalogue.FolderOf, StringComparer.Ordinal)
            .Select(g => new CatalogFacetValue(g.Key, g.Count(), g.Sum(i => (i.Location as RemoteItemLocation)?.SizeBytes ?? 0)))
            .GroupBy(a => RemoteS100Catalogue.TopFolder(a.Value), StringComparer.Ordinal)
            .Select(g => new S100CatalogueRegion(
                g.Key,
                RemoteS100Catalogue.FolderName(g.Key),
                g.OrderBy(a => RemoteS100Catalogue.FolderName(a.Value), StringComparer.CurrentCulture).ToArray()))
            .OrderBy(r => r.Name, StringComparer.CurrentCulture)
            .ToArray();
    }

    /// <summary>The navigation purposes declared, finest resolution first.</summary>
    public static IReadOnlyList<S100CataloguePurpose> NavigationPurposes(IEnumerable<CollectionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .Where(i => i.Properties.ContainsKey(RemoteS100Catalogue.NavigationPurposeProperty))
            .GroupBy(i => i.Properties[RemoteS100Catalogue.NavigationPurposeProperty], StringComparer.OrdinalIgnoreCase)
            .Select(g => new S100CataloguePurpose(
                g.Key,
                g.Count(),
                g.Select(i => i.Properties.GetValueOrDefault(RemoteS100Catalogue.GridResolutionProperty))
                    .OfType<string>()
                    .GroupBy(r => r, StringComparer.Ordinal)
                    .OrderByDescending(r => r.Count())
                    .Select(r => double.TryParse(r.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : (double?)null)
                    .FirstOrDefault()))
            .OrderBy(p => p.GridResolution ?? double.MaxValue)
            .ThenBy(p => p.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Returns <paramref name="items"/> with the sizes and dates of the
    /// <paramref name="objects"/> listed for their URLs; items not listed
    /// keep what they had.
    /// </summary>
    public static IReadOnlyList<CollectionItem> WithSizes(
        IEnumerable<CollectionItem> items, IReadOnlyDictionary<Uri, S3Object> objects)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(objects);
        return items.Select(item => item.Location is RemoteItemLocation remote && objects.TryGetValue(remote.Uri, out var listed)
                ? item with { Location = remote with { SizeBytes = listed.SizeBytes, LastModified = listed.LastModified } }
                : item)
            .ToArray();
    }
}
