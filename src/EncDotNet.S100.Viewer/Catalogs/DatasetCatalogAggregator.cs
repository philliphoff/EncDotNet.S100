using System;
using System.Collections.Generic;
using System.Linq;

namespace EncDotNet.S100.Viewer.Catalogs;

/// <summary>
/// Combines multiple <see cref="IDatasetCatalogSource"/> instances into a
/// single source. Re-raises <see cref="IDatasetCatalogSource.Changed"/>
/// whenever any registered child source changes (or when sources are added /
/// removed).
/// </summary>
internal sealed class DatasetCatalogAggregator : IDatasetCatalogSource
{
    private readonly List<IDatasetCatalogSource> _sources = new();
    private readonly object _lock = new();

    /// <summary>Initializes a new aggregator.</summary>
    public DatasetCatalogAggregator(string id = "aggregator", string displayName = "All catalogues")
    {
        Id = id;
        DisplayName = displayName;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public string DisplayName { get; }

    /// <summary>The currently registered child sources.</summary>
    public IReadOnlyList<IDatasetCatalogSource> Sources
    {
        get { lock (_lock) return _sources.ToArray(); }
    }

    /// <inheritdoc/>
    public IReadOnlyList<DatasetCatalogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _sources.SelectMany(s => s.Entries).ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed;

    /// <summary>Adds a child source. Raises <see cref="Changed"/>.</summary>
    public void Add(IDatasetCatalogSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_lock)
        {
            if (_sources.Contains(source))
                return;
            _sources.Add(source);
            source.Changed += OnChildChanged;
        }

        RaiseChanged();
    }

    /// <summary>Removes a child source. Raises <see cref="Changed"/> when the source was registered.</summary>
    public bool Remove(IDatasetCatalogSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        bool removed;
        lock (_lock)
        {
            removed = _sources.Remove(source);
            if (removed)
                source.Changed -= OnChildChanged;
        }

        if (removed)
            RaiseChanged();
        return removed;
    }

    private void OnChildChanged(object? sender, DatasetCatalogChangedEventArgs e) => RaiseChanged();

    private void RaiseChanged() =>
        Changed?.Invoke(this, new DatasetCatalogChangedEventArgs(this));
}
