using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S57.ExchangeSets;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Where an exchange set lives, independent of whether it is a folder or a
/// ZIP entry prefix.
/// </summary>
/// <param name="RootPath">The folder or ZIP that item locations resolve against.</param>
/// <param name="IsZip">True when <paramref name="RootPath"/> is a ZIP.</param>
/// <param name="Prefix">
/// The exchange set's directory relative to <paramref name="RootPath"/>, with
/// forward slashes and a trailing slash, or empty for the root.
/// </param>
/// <param name="CatalogueFileName">The catalogue file name within the exchange set.</param>
/// <param name="GroupKey">The group key of the exchange set's items.</param>
/// <param name="Open">
/// Opens a file by its path relative to the exchange set, or returns
/// <see langword="null"/> when it does not exist.
/// </param>
internal sealed record ExchangeSetContext(
    string RootPath,
    bool IsZip,
    string Prefix,
    string CatalogueFileName,
    string GroupKey,
    Func<string, Stream?> Open)
{
    /// <summary>The catalogue's path relative to <see cref="RootPath"/>.</summary>
    public string CatalogueRelativePath => Prefix + CatalogueFileName;

    /// <summary>Maps an exchange-set-relative path to a <see cref="RootPath"/>-relative one.</summary>
    public string ToRootRelative(string setRelativePath) =>
        Prefix + setRelativePath.Replace('\\', '/').TrimStart('/');
}

/// <summary>
/// Builds <see cref="CollectionItem"/>s from S-100 (<c>CATALOG.XML</c>) and
/// S-57 (<c>CATALOG.031</c>) exchange-set catalogues.
/// </summary>
internal static class ExchangeSetItemReader
{
    /// <summary>
    /// Builds one item per S-100 dataset. ISO 8211 datasets (S-101) with
    /// sequential updates (<c>.001</c>, …) in the same set are folded into
    /// their base dataset's item.
    /// </summary>
    public static IEnumerable<CollectionItem> ReadS100(
        ExchangeCatalogue catalogue, ExchangeSetContext context, List<IndexDiagnostic> diagnostics)
    {
        var datasets = catalogue.DatasetDiscoveryMetadata;

        // Group numbered ISO 8211 files by directory + cell stem.
        var groups = new Dictionary<string, List<(int Update, DatasetDiscoveryMetadata Dataset)>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var dataset in datasets)
        {
            if (TryGetSequence(dataset, out var cell, out var update))
            {
                if (!groups.TryGetValue(cell, out var members))
                    groups[cell] = members = [];
                members.Add((update, dataset));
            }
        }

        foreach (var dataset in datasets)
        {
            if (!TryGetSequence(dataset, out var cell, out var update))
            {
                yield return BuildS100Item(catalogue, dataset, [], context);
                continue;
            }

            var members = groups[cell];
            var hasBase = members.Any(m => m.Update == 0);
            if (hasBase && update != 0)
                continue;

            if (!hasBase)
            {
                diagnostics.Add(new IndexDiagnostic(
                    IndexDiagnosticSeverity.Warning,
                    "Update has no base dataset in this exchange set.",
                    context.ToRootRelative(dataset.RelativePath)));
                yield return BuildS100Item(catalogue, dataset, [], context);
                continue;
            }

            var updates = members
                .Where(m => m.Update > 0)
                .OrderBy(m => m.Update)
                .Select(m => m.Dataset)
                .ToArray();
            yield return BuildS100Item(catalogue, dataset, updates, context);
        }
    }

    private static CollectionItem BuildS100Item(
        ExchangeCatalogue catalogue,
        DatasetDiscoveryMetadata dataset,
        IReadOnlyList<DatasetDiscoveryMetadata> updates,
        ExchangeSetContext context)
    {
        var latest = updates.Count > 0 ? updates[^1] : dataset;
        var (spec, version) = ResolveSpec(dataset.ProductSpecification ?? catalogue.ProductSpecification);
        var relativePath = dataset.RelativePath;

        var coverage = GeoCoverage.FromPolygons(
            dataset.DataCoverages.SelectMany(c => GmlCoverageParser.Parse(c.BoundingPolygon)));
        var bounds = dataset.BoundingBox is { } box
            ? new GeoBounds(box.SouthBoundLatitude, box.WestBoundLongitude, box.NorthBoundLatitude, box.EastBoundLongitude)
            : coverage?.ComputeBounds();

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        AddIfPresent(properties, "producingAgency", dataset.ProducingAgency);
        AddIfPresent(properties, "classification", dataset.Classification);
        AddIfPresent(properties, "navigationPurpose", dataset.NavigationPurpose);
        AddIfPresent(properties, "encodingFormat", dataset.EncodingFormat);
        AddIfPresent(properties, "purpose", dataset.Purpose);
        if (dataset.DataProtection)
            properties["dataProtection"] = "true";
        if (dataset.NotForNavigation)
            properties["notForNavigation"] = "true";

        return new CollectionItem
        {
            Key = context.GroupKey + "/" + relativePath,
            ProductSpec = spec,
            ProductSpecVersion = version,
            Name = Path.GetFileNameWithoutExtension(relativePath),
            Title = string.IsNullOrWhiteSpace(dataset.Description) ? null : dataset.Description.Trim(),
            GroupKey = context.GroupKey,
            Edition = dataset.EditionNumber,
            Update = latest.UpdateNumber ?? dataset.UpdateNumber,
            IssueDate = ParseDate(latest.IssueDate) ?? ParseDate(dataset.IssueDate),
            UpdateApplicationDate = ParseDate(dataset.UpdateApplicationDate),
            MinimumDisplayScale = dataset.ResolveMinimumDisplayScale(),
            MaximumDisplayScale = dataset.ResolveMaximumDisplayScale(),
            Status = CollectionItemStatus.Unknown,
            Bounds = bounds,
            Coverage = coverage,
            Location = new LocalItemLocation(
                context.RootPath,
                context.ToRootRelative(relativePath),
                updates.Select(u => context.ToRootRelative(u.RelativePath)).ToArray(),
                context.CatalogueRelativePath,
                context.IsZip),
            Properties = properties,
        };
    }

    /// <summary>
    /// Builds one item per S-57 base cell, reading each cell's (and its latest
    /// update's) <c>DSID</c> for the edition, update and issue date that
    /// <c>CATALOG.031</c> does not carry.
    /// </summary>
    public static IEnumerable<CollectionItem> ReadS57(
        S57Catalog catalogue,
        ExchangeSetContext context,
        List<IndexDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (var cell in S57ExchangeSetCatalog.SelectBaseCells(catalogue))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = cell.RelativePath.Replace('\\', '/');
            var updates = cell.UpdateRelativePaths.Select(u => u.Replace('\\', '/')).ToArray();

            var header = ReadHeader(relativePath, context, diagnostics);
            var latest = updates.Length > 0 ? ReadHeader(updates[^1], context, diagnostics) : null;

            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            if (header?.ProducingAgency is { } agency)
                properties["producingAgency"] = agency.ToString(CultureInfo.InvariantCulture);
            if (header?.IntendedUsage is { } usage)
                properties["intendedUsage"] = usage.ToString(CultureInfo.InvariantCulture);

            yield return new CollectionItem
            {
                Key = context.GroupKey + "/" + cell.CellName,
                ProductSpec = "S-57",
                Name = cell.CellName,
                Title = cell.LongFileName is { } title
                    && !title.Equals(Path.GetFileName(relativePath), StringComparison.OrdinalIgnoreCase)
                        ? title
                        : null,
                GroupKey = context.GroupKey,
                Edition = header?.EditionNumber,
                Update = latest?.UpdateNumber ?? header?.UpdateNumber,
                IssueDate = latest?.IssueDate ?? header?.IssueDate,
                UpdateApplicationDate = header?.UpdateApplicationDate,
                CompilationScale = header?.CompilationScale,
                UsageBand = UsageBand.FromCellName(cell.CellName),
                Status = CollectionItemStatus.Unknown,
                Bounds = cell.BoundingBox is { } box
                    ? new GeoBounds(box.SouthBoundLatitude, box.WestBoundLongitude, box.NorthBoundLatitude, box.EastBoundLongitude)
                    : null,
                Location = new LocalItemLocation(
                    context.RootPath,
                    context.ToRootRelative(relativePath),
                    updates.Select(context.ToRootRelative).ToArray(),
                    context.CatalogueRelativePath,
                    context.IsZip),
                Properties = properties,
            };
        }
    }

    private static S57DatasetHeader? ReadHeader(
        string relativePath, ExchangeSetContext context, List<IndexDiagnostic> diagnostics)
    {
        try
        {
            using var stream = context.Open(relativePath);
            if (stream is null)
            {
                diagnostics.Add(new IndexDiagnostic(
                    IndexDiagnosticSeverity.Warning,
                    "Catalogued file is missing.",
                    context.ToRootRelative(relativePath)));
                return null;
            }

            var header = S57DatasetHeader.Read(stream);
            if (header is null)
            {
                diagnostics.Add(new IndexDiagnostic(
                    IndexDiagnosticSeverity.Warning,
                    "Could not read the cell's DSID record.",
                    context.ToRootRelative(relativePath)));
            }

            return header;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Warning, ex.Message, context.ToRootRelative(relativePath)));
            return null;
        }
    }

    /// <summary>
    /// Recognises a sequentially-updated ISO 8211 dataset file (numeric
    /// three-digit extension), returning its cell key (directory + stem) and
    /// update number (the catalogue's, else the extension's).
    /// </summary>
    private static bool TryGetSequence(DatasetDiscoveryMetadata dataset, out string cell, out int update)
    {
        var path = dataset.RelativePath;
        var ext = Path.GetExtension(path);
        if (ext.Length != 4
            || !int.TryParse(ext.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var fromExtension))
        {
            cell = string.Empty;
            update = 0;
            return false;
        }

        cell = path[..^ext.Length];
        update = dataset.UpdateNumber ?? fromExtension;
        return true;
    }

    /// <summary>
    /// Resolves a canonical product specification name and version from
    /// catalogue metadata, or <c>("Unknown", null)</c>.
    /// </summary>
    internal static (string Spec, string? Version) ResolveSpec(ProductSpecification? productSpecification)
    {
        if (productSpecification is null)
            return ("Unknown", null);

        if (productSpecification.TryToSpecRef(out var specRef))
            return (specRef.Name, productSpecification.Version ?? specRef.Edition.ToString());

        if (SpecName.TryNormalize(productSpecification.ProductIdentifier, out var fromIdentifier))
            return (fromIdentifier, productSpecification.Version);

        if (SpecName.TryNormalize(productSpecification.Name, out var fromName))
            return (fromName, productSpecification.Version);

        if (productSpecification.Number is int number)
            return (string.Create(CultureInfo.InvariantCulture, $"S-{number}"), productSpecification.Version);

        return ("Unknown", productSpecification.Version);
    }

    /// <summary>Parses <c>yyyy-MM-dd</c>, <c>yyyyMMdd</c>, or an ISO 8601 date-time's date.</summary>
    internal static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        if (text.Length > 10 && text[10] is 'T' or ' ')
            text = text[..10];

        return DateOnly.TryParseExact(
                text, ["yyyy-MM-dd", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static void AddIfPresent(Dictionary<string, string> properties, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            properties[key] = value.Trim();
    }
}

/// <summary>Derives the ENC usage band (1–6) from an S-57 cell name.</summary>
internal static class UsageBand
{
    /// <summary>
    /// Returns the usage band encoded as the third character of an S-57 cell
    /// name (S-57 Appendix B.1, e.g. <c>US5MA1BO</c> → 5), or
    /// <see langword="null"/>.
    /// </summary>
    public static int? FromCellName(string? cellName)
    {
        if (cellName is not { Length: >= 3 })
            return null;

        var c = cellName[2];
        return c is >= '1' and <= '6' ? c - '0' : null;
    }
}
