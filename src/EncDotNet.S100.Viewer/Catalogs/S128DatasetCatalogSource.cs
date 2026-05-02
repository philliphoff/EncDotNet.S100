using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using EncDotNet.S100.Datasets.S128;

namespace EncDotNet.S100.Viewer.Catalogs;

/// <summary>
/// An <see cref="IDatasetCatalogSource"/> backed by one or more loaded
/// <see cref="S128Dataset"/> instances. Each <see cref="S128ProductEntry"/>
/// is mapped onto a neutral <see cref="DatasetCatalogEntry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Datasets are tracked by a caller-supplied <c>sourceLabel</c> (typically
/// the dataset filename). Adding or removing a dataset raises
/// <see cref="Changed"/>; the entries collection is replaced wholesale on
/// each mutation.
/// </para>
/// </remarks>
internal sealed class S128DatasetCatalogSource : IDatasetCatalogSource
{
    private readonly Dictionary<string, S128Dataset> _datasets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private ImmutableArray<DatasetCatalogEntry> _entries = ImmutableArray<DatasetCatalogEntry>.Empty;

    /// <summary>Initializes a new source.</summary>
    /// <param name="id">Stable id (used by the aggregator).</param>
    /// <param name="displayName">Human-readable label shown in the panel UI.</param>
    public S128DatasetCatalogSource(string id = "s128-loaded", string displayName = "Loaded S-128 datasets")
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(displayName);
        Id = id;
        DisplayName = displayName;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public string DisplayName { get; }

    /// <inheritdoc/>
    public IReadOnlyList<DatasetCatalogEntry> Entries
    {
        get { lock (_lock) return _entries; }
    }

    /// <inheritdoc/>
    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed;

    /// <summary>
    /// Registers (or replaces) a parsed S-128 dataset under the given label.
    /// Raises <see cref="Changed"/>.
    /// </summary>
    public void AddDataset(string sourceLabel, S128Dataset dataset)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceLabel);
        ArgumentNullException.ThrowIfNull(dataset);

        lock (_lock)
        {
            _datasets[sourceLabel] = dataset;
            _entries = Rebuild();
        }

        Changed?.Invoke(this, new DatasetCatalogChangedEventArgs(this));
    }

    /// <summary>
    /// Removes a previously-registered dataset. Returns <see langword="true"/>
    /// when a dataset was actually removed and raises <see cref="Changed"/>
    /// in that case.
    /// </summary>
    public bool RemoveDataset(string sourceLabel)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceLabel);

        bool removed;
        lock (_lock)
        {
            removed = _datasets.Remove(sourceLabel);
            if (removed)
                _entries = Rebuild();
        }

        if (removed)
            Changed?.Invoke(this, new DatasetCatalogChangedEventArgs(this));
        return removed;
    }

    /// <summary>Removes every registered dataset. Raises <see cref="Changed"/> when non-empty.</summary>
    public void Clear()
    {
        bool changed;
        lock (_lock)
        {
            changed = _datasets.Count > 0;
            _datasets.Clear();
            _entries = ImmutableArray<DatasetCatalogEntry>.Empty;
        }

        if (changed)
            Changed?.Invoke(this, new DatasetCatalogChangedEventArgs(this));
    }

    private ImmutableArray<DatasetCatalogEntry> Rebuild()
    {
        if (_datasets.Count == 0)
            return ImmutableArray<DatasetCatalogEntry>.Empty;

        var builder = ImmutableArray.CreateBuilder<DatasetCatalogEntry>();
        foreach (var (label, ds) in _datasets)
        {
            foreach (var entry in ds.Entries)
            {
                builder.Add(Map(label, entry));
            }
        }

        return builder.ToImmutable();
    }

    private DatasetCatalogEntry Map(string sourceLabel, S128ProductEntry entry)
    {
        var feature = entry.Feature;

        var ext = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        ext["sourceLabel"] = sourceLabel;
        ext["featureType"] = entry.FeatureType;
        if (feature.Attributes.TryGetValue("serviceStatus", out var svc))
            ext["serviceStatus"] = svc;
        if (feature.Attributes.TryGetValue("distributionStatus", out var dist))
            ext["distributionStatus"] = dist;
        if (entry.NotForNavigation)
            ext["notForNavigation"] = "true";

        return new DatasetCatalogEntry
        {
            Id = $"{sourceLabel}#{entry.Id}",
            SourceId = Id,
            ProductSpec = NormalizeSpecCode(entry.ProductSpecificationName),
            ProductSpecVersion = entry.ProductSpecificationVersion,
            ProductNumber = entry.ProductNumber,
            Title = entry.ProductNumber ?? entry.Id,
            EditionNumber = entry.EditionNumber,
            UpdateNumber = entry.UpdateNumber,
            IssueDate = entry.IssueDate,
            UpdateDate = entry.UpdateDate,
            Status = MapStatus(entry.Status),
            Classification = entry.Classification,
            NotForNavigation = entry.NotForNavigation,
            Coverage = DatasetCatalogCoverage.FromRing(entry.CoverageRing),
            ExtendedProperties = ext.ToImmutable(),
        };
    }

    private static DatasetCatalogStatus MapStatus(S128ProductStatus status) =>
        status switch
        {
            S128ProductStatus.InForce => DatasetCatalogStatus.InForce,
            S128ProductStatus.Superseded => DatasetCatalogStatus.Superseded,
            S128ProductStatus.Withdrawn => DatasetCatalogStatus.Withdrawn,
            S128ProductStatus.Planned => DatasetCatalogStatus.Planned,
            _ => DatasetCatalogStatus.Unknown,
        };

    /// <summary>
    /// Reduces free-form product-specification names found in S-128 datasets
    /// (e.g. <c>"S-57 Transfer Standard for Digital Hydrographic Data"</c> or
    /// <c>"S-101"</c>) to the canonical short code (<c>"S-57"</c>,
    /// <c>"S-101"</c>) when one can be detected at the start of the string.
    /// Returns the input unchanged if no match is found.
    /// </summary>
    private static string? NormalizeSpecCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var trimmed = raw.Trim();
        var m = Regex.Match(trimmed, @"^S-?\d{2,4}", RegexOptions.IgnoreCase);
        if (!m.Success) return trimmed;
        var token = m.Value.ToUpperInvariant();
        return token.StartsWith("S-") ? token : "S-" + token[1..];
    }
}
