using System.Globalization;
using System.Text.RegularExpressions;
using EncDotNet.S100.Datasets.S128;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Indexes an <see cref="S128CatalogueSource"/>: each product entry of an
/// S-128 Catalogue of Nautical Products becomes a catalogue-only item
/// (<see cref="NoItemLocation"/>) carrying the product's identification,
/// currency and coverage.
/// </summary>
public sealed partial class S128CatalogueIndexer : ICollectionSourceIndexer
{
    private const string FingerprintVersion = "s128-v1";

    /// <inheritdoc/>
    public bool CanIndex(CollectionSource source) => source is S128CatalogueSource;

    /// <inheritdoc/>
    public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken)
    {
        var path = PathOf(source);
        var file = new FileInfo(path);
        return ValueTask.FromResult<string?>(file.Exists
            ? string.Create(CultureInfo.InvariantCulture,
                $"{FingerprintVersion}:{file.Length}:{file.LastWriteTimeUtc.Ticks}")
            : null);
    }

    /// <inheritdoc/>
    public ValueTask<SourceIndex> IndexAsync(
        CollectionSource source,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var path = PathOf(source);

        return new ValueTask<SourceIndex>(Task.Run(async () =>
        {
            var fingerprint = await GetFingerprintAsync(source, cancellationToken).ConfigureAwait(false);
            var diagnostics = new List<IndexDiagnostic>();
            if (fingerprint is null)
            {
                diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Error, "Source path not found.", path));
                return new SourceIndex(source.Id, DateTimeOffset.UtcNow, null, [], diagnostics);
            }

            progress?.Report(new IndexProgress(0, path));
            var dataset = S128Dataset.Open(path);
            var items = dataset.Entries.Select(Map).ToArray();
            progress?.Report(new IndexProgress(items.Length, null));

            return new SourceIndex(source.Id, DateTimeOffset.UtcNow, fingerprint, items, diagnostics);
        }, cancellationToken));
    }

    /// <summary>Maps one S-128 product entry onto a neutral item.</summary>
    internal static CollectionItem Map(S128ProductEntry entry)
    {
        var feature = entry.Feature;

        var coverage = feature.ExteriorRing.Count >= 3
            ? GeoCoverage.FromPolygons([new GeoPolygon(feature.ExteriorRing, feature.InteriorRings)])
            : GeoCoverage.FromPolygons([new GeoPolygon(entry.CoverageRing)]);

        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["featureType"] = entry.FeatureType,
        };
        if (feature.Attributes.TryGetValue("serviceStatus", out var service))
            properties["serviceStatus"] = service;
        if (feature.Attributes.TryGetValue("distributionStatus", out var distribution))
            properties["distributionStatus"] = distribution;
        if (!string.IsNullOrWhiteSpace(entry.Classification))
            properties["classification"] = entry.Classification;
        if (entry.NotForNavigation)
            properties["notForNavigation"] = "true";
        if (entry.UpdateDate is { } updateDate)
            properties["updateDate"] = updateDate;

        var edition = ParseInt(entry.EditionNumber);
        if (edition is null && !string.IsNullOrWhiteSpace(entry.EditionNumber))
            properties["editionNumber"] = entry.EditionNumber;
        var update = ParseInt(entry.UpdateNumber);
        if (update is null && !string.IsNullOrWhiteSpace(entry.UpdateNumber))
            properties["updateNumber"] = entry.UpdateNumber;

        var name = entry.ProductNumber ?? entry.Id;
        return new CollectionItem
        {
            Key = name,
            ProductSpec = NormalizeSpecCode(entry.ProductSpecificationName) ?? "Unknown",
            ProductSpecVersion = entry.ProductSpecificationVersion,
            Name = name,
            Edition = edition,
            Update = update,
            IssueDate = ExchangeSetItemReader.ParseDate(entry.IssueDate),
            Status = entry.Status switch
            {
                S128ProductStatus.InForce => CollectionItemStatus.Active,
                S128ProductStatus.Superseded => CollectionItemStatus.Superseded,
                S128ProductStatus.Withdrawn => CollectionItemStatus.Cancelled,
                S128ProductStatus.Planned => CollectionItemStatus.Planned,
                _ => CollectionItemStatus.Unknown,
            },
            Bounds = coverage?.ComputeBounds(),
            Coverage = coverage,
            Location = NoItemLocation.Instance,
            Properties = properties,
        };
    }

    /// <summary>
    /// Reduces a free-form product-specification name (e.g. <c>"S-57
    /// Transfer Standard for Digital Hydrographic Data"</c>) to its canonical
    /// short code (<c>"S-57"</c>), or returns the trimmed input when none is
    /// found.
    /// </summary>
    internal static string? NormalizeSpecCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        var match = SpecCodeRegex().Match(trimmed);
        if (!match.Success)
            return trimmed;

        return "S-" + match.Groups["digits"].Value;
    }

    [GeneratedRegex(@"^S-?(?<digits>\d{2,4})", RegexOptions.IgnoreCase)]
    private static partial Regex SpecCodeRegex();

    private static int? ParseInt(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string PathOf(CollectionSource source) => source switch
    {
        S128CatalogueSource s128 => s128.Path,
        _ => throw new NotSupportedException($"{nameof(S128CatalogueIndexer)} cannot index {source.GetType().Name}."),
    };
}
