using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model for a single catalogue entry in the Dataset Catalog panel.
/// Wraps a neutral <see cref="DatasetCatalogEntry"/>.
/// </summary>
internal sealed class CatalogEntryViewModel : ViewModelBase
{
    public DatasetCatalogEntry Entry { get; }

    public CatalogEntryViewModel(DatasetCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
    }

    public string Title => Entry.Title ?? Entry.ProductNumber ?? Entry.Id;
    public string? ProductNumber => Entry.ProductNumber;
    public string? ProductSpec => Entry.ProductSpec;
    public string? ProductSpecVersion => Entry.ProductSpecVersion;
    public string? EditionNumber => Entry.EditionNumber;
    public string? UpdateNumber => Entry.UpdateNumber;
    public string? IssueDate => Entry.IssueDate;
    public string? UpdateDate => Entry.UpdateDate;
    public string? Classification => Entry.Classification;
    public bool NotForNavigation => Entry.NotForNavigation;
    public DatasetCatalogStatus Status => Entry.Status;

    /// <summary>
    /// Display text for the status badge. PascalCase enum names are split
    /// on case boundaries and upper-cased (e.g. <c>InForce</c> →
    /// <c>"IN FORCE"</c>) for a typical pill-style badge.
    /// </summary>
    public string StatusText => FormatStatus(Entry.Status);

    private static string FormatStatus(DatasetCatalogStatus status)
    {
        var name = status.ToString();
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                sb.Append(' ');
            sb.Append(char.ToUpperInvariant(name[i]));
        }
        return sb.ToString();
    }

    /// <summary>Solid colour brush used by the status badge.</summary>
    public IBrush StatusBrush => new SolidColorBrush(Desaturate(GetStatusColor(Entry.Status), 0.35));

    /// <summary>
    /// Border brush for the status badge — a darker tint of
    /// <see cref="StatusBrush"/> for definition against the panel background.
    /// </summary>
    public IBrush StatusBorderBrush
    {
        get
        {
            var c = Desaturate(GetStatusColor(Entry.Status), 0.35);
            byte d(byte v) => (byte)(v * 3 / 4);
            return new SolidColorBrush(Color.FromArgb(c.A, d(c.R), d(c.G), d(c.B)));
        }
    }

    private static Color GetStatusColor(DatasetCatalogStatus status) => status switch
    {
        DatasetCatalogStatus.InForce => Color.Parse("#22c55e"),
        DatasetCatalogStatus.Superseded => Color.Parse("#eab308"),
        DatasetCatalogStatus.Withdrawn => Color.Parse("#ef4444"),
        DatasetCatalogStatus.Planned => Color.Parse("#3b82f6"),
        _ => Color.Parse("#9ca3af"),
    };

    /// <summary>
    /// Mixes <paramref name="color"/> toward neutral gray (#808080) by the
    /// given <paramref name="amount"/> (0 = no change, 1 = fully gray) to
    /// produce a less saturated badge fill.
    /// </summary>
    private static Color Desaturate(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);
        const byte gray = 0x80;
        byte mix(byte v) => (byte)(v + (gray - v) * amount);
        return Color.FromArgb(color.A, mix(color.R), mix(color.G), mix(color.B));
    }

    public string SourceId => Entry.SourceId;

    public string Subtitle
    {
        get
        {
            // Primary form: "S-104 (v1.4.1)" when we have both spec and version.
            if (!string.IsNullOrEmpty(ProductSpec))
            {
                return string.IsNullOrEmpty(ProductSpecVersion)
                    ? ProductSpec!
                    : $"{ProductSpec} (v{ProductSpecVersion})";
            }
            return Entry.Id;
        }
    }

    public IReadOnlyList<KeyValuePair<string, string>> ExtendedPropertyList =>
        Entry.ExtendedProperties.OrderBy(p => p.Key).ToList();

    public bool HasExtendedProperties => Entry.ExtendedProperties.Count > 0;

    public bool HasCoverage => Entry.Coverage is not null;

    /// <summary>Formatted south-west corner of the coverage bounding box.</summary>
    public string CoverageSouthWest =>
        Entry.Coverage is { } c ? FormatLatLon(c.MinLatitude, c.MinLongitude) : string.Empty;

    /// <summary>Formatted north-east corner of the coverage bounding box.</summary>
    public string CoverageNorthEast =>
        Entry.Coverage is { } c ? FormatLatLon(c.MaxLatitude, c.MaxLongitude) : string.Empty;

    /// <summary>
    /// Formats a (lat, lon) pair in degrees-decimal-minutes form, e.g.
    /// <c>"12°34.567'N 056°12.345'W"</c>. Mariner-friendly and matches the
    /// convention typically used on nautical charts.
    /// </summary>
    private static string FormatLatLon(double latitude, double longitude)
    {
        return $"{FormatDegMin(latitude, 'N', 'S', 2)}  {FormatDegMin(longitude, 'E', 'W', 3)}";
    }

    private static string FormatDegMin(double value, char positive, char negative, int degDigits)
    {
        var hemi = value >= 0 ? positive : negative;
        var abs = Math.Abs(value);
        var deg = (int)Math.Floor(abs);
        var min = (abs - deg) * 60.0;
        var degFmt = "D" + degDigits;
        return $"{deg.ToString(degFmt)}°{min:00.000}'{hemi}";
    }
}

/// <summary>
/// View model for the Dataset Catalog panel. Subscribes to an
/// <see cref="IDatasetCatalogSource"/> (typically a
/// <see cref="DatasetCatalogAggregator"/>) and reflects its entries into an
/// observable collection on the UI thread.
/// </summary>
internal sealed class CatalogPanelViewModel : ViewModelBase
{
    private readonly IDatasetCatalogSource _source;

    public ObservableCollection<CatalogEntryViewModel> Entries { get; } = new();

    private CatalogEntryViewModel? _selectedEntry;
    public CatalogEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => _selectedEntry is not null;

    public bool IsEmpty => Entries.Count == 0;

    public string EmptyMessage => Strings.Catalog_EmptyMessage;

    public CatalogPanelViewModel(IDatasetCatalogSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _source.Changed += OnSourceChanged;
        Refresh();
    }

    private void OnSourceChanged(object? sender, DatasetCatalogChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            Refresh();
        else
            Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        var previousId = _selectedEntry?.Entry.Id;

        Entries.Clear();
        foreach (var entry in _source.Entries.OrderBy(e => e.SourceId).ThenBy(e => e.Title))
        {
            Entries.Add(new CatalogEntryViewModel(entry));
        }

        // Try to preserve selection across refresh
        SelectedEntry = previousId is null
            ? null
            : Entries.FirstOrDefault(v => v.Entry.Id == previousId);

        OnPropertyChanged(nameof(IsEmpty));
    }
}
