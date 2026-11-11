using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// One value that can be chosen when adding a Library source (#792): a NOAA
/// state, district or region, a USACE river, a feed's product, a community
/// list's entry, a manifest group, a remote catalogue's area, a forecast model.
/// Hosts bind to it directly; ticking it is what selects it.
/// </summary>
public sealed class LibraryChoice : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _detail;

    /// <summary>Creates a choice for a catalogue facet, its detail "N cells · X MB".</summary>
    /// <param name="value">The facet value and its totals.</param>
    /// <param name="label">The label shown.</param>
    public LibraryChoice(CatalogFacetValue value, string label)
        : this(
            (value ?? throw new ArgumentNullException(nameof(value))).Value,
            label,
            string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_FacetDetailFormat"),
                value.CellCount, LibraryTextFormat.Bytes(value.TotalBytes)))
    {
    }

    /// <summary>Creates a choice.</summary>
    /// <param name="value">The value passed back when it is chosen.</param>
    /// <param name="label">The label shown.</param>
    /// <param name="detail">A detail shown with it, e.g. a count and size; empty for none.</param>
    public LibraryChoice(string value, string label, string detail)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(label);
        Value = value;
        Label = label;
        _detail = detail ?? string.Empty;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The value (state code, district or region number, river name, product, entry number, …).</summary>
    public string Value { get; }

    /// <summary>The display label.</summary>
    public string Label { get; }

    /// <summary>"N cells · X MB", or other detail (a community entry's publication date; a remote area's size once listed).</summary>
    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value ?? string.Empty);
    }

    /// <summary>A collection-manifest group's first path, as written in the manifest; otherwise null.</summary>
    public string? PathText { get; init; }

    /// <summary>"+2" when a manifest group lists more paths than <see cref="PathText"/>; otherwise null.</summary>
    public string? MorePathsText { get; init; }

    /// <summary>Every path of a manifest group, one per line, for a tooltip.</summary>
    public string? PathsTooltip { get; init; }

    /// <summary>True when some path of a manifest group does not exist on disk.</summary>
    public bool IsMissing { get; init; }

    /// <summary>True when the choice shows paths (a manifest group) rather than <see cref="Detail"/>.</summary>
    public bool HasPaths => PathText is not null;

    /// <summary>True when <see cref="MorePathsText"/> is set.</summary>
    public bool HasMorePaths => MorePathsText is not null;

    /// <summary>A second line under the label (a regional forecast model's "Overlaps cbofs, dbofs"), if any.</summary>
    public string? Note { get; init; }

    /// <summary>True when <see cref="Note"/> is set.</summary>
    public bool HasNote => Note is not null;

    /// <summary>Whether the value is chosen.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
