using System.Globalization;
using Avalonia.Media;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Legends;

namespace EncDotNet.S100.Viewer.ViewModels.Legends;

/// <summary>
/// One collapsible section of the Legend tab: a product or overlay's header
/// (product chip, name, source and time) and its rows (legends design note
/// D4). Kept across rebuilds by <see cref="Id"/>, so its expander and focus
/// survive a pan or a redraw.
/// </summary>
internal sealed class LegendSectionViewModel : ViewModelBase
{
    private readonly LegendService _service;
    private LegendSection _section;
    private IBrush _tile;
    private string _detail;
    private bool _isExpanded;
    private IReadOnlyList<LegendGroupViewModel> _groups;
    private IReadOnlyList<LegendGroupViewModel> _visibleGroups;

    /// <summary>Creates the section's view model.</summary>
    /// <param name="service">Remembers the expanded state.</param>
    /// <param name="section">The section.</param>
    /// <param name="tile">The palette's chart background, under each glyph (D10).</param>
    /// <param name="detail">The header's source and time summary.</param>
    public LegendSectionViewModel(LegendService service, LegendSection section, IBrush tile, string detail)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(detail);
        _service = service;
        _section = section;
        _tile = tile;
        _detail = detail;
        _isExpanded = section.IsExpanded;
        _groups = BuildGroups(section, tile, []);
        _visibleGroups = _groups;
    }

    /// <summary>The section's id (<see cref="LegendSection.Id"/>).</summary>
    public string Id => _section.Id;

    /// <summary>The section this view model shows now.</summary>
    public LegendSection Section => _section;

    /// <summary>The product chip: "S-111", "GRIB2", "AML".</summary>
    public string Spec => _section.Spec;

    /// <summary>The product's name: "Surface currents".</summary>
    public string Title => _section.Title;

    /// <summary>The header's source and time ("111US00_a.h5 · 08:00Z"), or "No data at 14:20" (D5).</summary>
    public string Detail => _detail;

    /// <summary>Whether the product has no data at the view time, so the header warns.</summary>
    public bool HasNoData => _section.HasNoDataAtViewTime;

    /// <summary>The header's tooltip: the name, then the source and time in full.</summary>
    public string HeaderTooltip => string.Format(CultureInfo.CurrentCulture, Strings.Legend_SectionTooltipFormat, Spec, Title, Detail);

    /// <summary>The expander's accessible name, "S-111 Surface currents, 9 entries" (D11).</summary>
    public string AccessibleName => string.Format(
        CultureInfo.CurrentCulture,
        RowCount == 1 ? Strings.Legend_SectionNameOneFormat : Strings.Legend_SectionNameFormat,
        Spec,
        Title,
        RowCount);

    /// <summary>The expander's automation id, <c>Legend.Section.{productId}</c> (D11).</summary>
    public string AutomationId => "Legend.Section." + Id;

    /// <summary>How many rows the section has, filtered or not.</summary>
    public int RowCount => _groups.Sum(g => g.Rows.Count);

    /// <summary>Whether a section's product meets the map view (D5).</summary>
    public bool IsInView => _section.IsInView;

    /// <summary>The unit or rule caption ("Speed, kn · longer arrow = faster"), or null.</summary>
    public string? Caption => _section.Caption;

    /// <summary>Whether <see cref="Caption"/> shows.</summary>
    public bool HasCaption => !string.IsNullOrEmpty(Caption);

    /// <summary>The Mariner settings the rows follow ("Safety contour 10 m"), or null.</summary>
    public string? MarinerSettings => _section.MarinerSettings;

    /// <summary>Whether <see cref="MarinerSettings"/> shows.</summary>
    public bool HasMarinerSettings => !string.IsNullOrEmpty(MarinerSettings);

    /// <summary>Whether the section shows its rows; remembered per section by <see cref="LegendService"/>.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
                _service.SetExpanded(Id, value);
        }
    }

    /// <summary>Every group of rows, top of the stack first.</summary>
    public IReadOnlyList<LegendGroupViewModel> Groups => _groups;

    /// <summary>The groups with a row the filter matches, each with only those rows.</summary>
    public IReadOnlyList<LegendGroupViewModel> VisibleGroups
    {
        get => _visibleGroups;
        private set => SetProperty(ref _visibleGroups, value);
    }

    /// <summary>Every row of every group, in order.</summary>
    public IEnumerable<LegendRowViewModel> Rows => _groups.SelectMany(g => g.Rows);

    /// <summary>
    /// Takes a rebuilt section. Rows are kept while they and the palette are
    /// unchanged, so their glyphs aren't drawn again on every pan.
    /// </summary>
    /// <param name="section">The section, rebuilt.</param>
    /// <param name="tile">The palette's chart background.</param>
    /// <param name="detail">The header's source and time summary.</param>
    public void Update(LegendSection section, IBrush tile, string detail)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(detail);
        if (!string.Equals(section.Id, Id, StringComparison.Ordinal))
            throw new ArgumentException("The section has another id.", nameof(section));

        var reuse = ReferenceEquals(tile, _tile) ? _groups : [];
        _section = section;
        _tile = tile;
        _detail = detail;
        _groups = BuildGroups(section, tile, reuse);
        if (_isExpanded != section.IsExpanded)
        {
            _isExpanded = section.IsExpanded;
            OnPropertyChanged(nameof(IsExpanded));
        }
        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Shows only the rows <paramref name="filter"/> matches: all of them when
    /// it matches the section's name or chip, or a group's name.
    /// </summary>
    /// <param name="filter">The filter text; empty for every row.</param>
    /// <returns>True when the section has something to show.</returns>
    public bool ApplyFilter(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Length == 0 || Matches(Title, filter) || Matches(Spec, filter))
        {
            VisibleGroups = _groups;
            return true;
        }

        var groups = new List<LegendGroupViewModel>();
        foreach (var group in _groups)
        {
            if (Matches(group.Title, filter))
            {
                groups.Add(group);
                continue;
            }
            var rows = group.Rows.Where(r => Matches(r.Label, filter)).ToArray();
            if (rows.Length > 0)
                groups.Add(rows.Length == group.Rows.Count ? group : group.WithRows(rows));
        }
        VisibleGroups = groups;
        return groups.Count > 0;
    }

    private static bool Matches(string text, string filter) =>
        text.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    private static LegendGroupViewModel[] BuildGroups(
        LegendSection section,
        IBrush tile,
        IReadOnlyList<LegendGroupViewModel> previous)
    {
        var showTitles = section.Groups.Count > 1;
        return [.. section.Groups.Select(group =>
        {
            var old = previous.FirstOrDefault(g => string.Equals(g.Key, group.Key, StringComparison.Ordinal));
            var rows = old is not null && old.Rows.Select(r => r.Entry).SequenceEqual(group.Rows)
                ? old.Rows
                : [.. group.Rows.Select(entry => new LegendRowViewModel(entry, tile))];
            return new LegendGroupViewModel(group.Key, group.Title, showTitles, rows);
        })];
    }
}

/// <summary>The rows of one sub-layer in a Legend tab section, with its name when the section has several.</summary>
/// <param name="key">The sub-layer key.</param>
/// <param name="title">The sub-layer's name, "Tropical cyclone track".</param>
/// <param name="showTitle">Whether the name shows: the section has more than one group.</param>
/// <param name="rows">The rows.</param>
internal sealed class LegendGroupViewModel(string key, string title, bool showTitle, IReadOnlyList<LegendRowViewModel> rows)
{
    /// <summary>The sub-layer key.</summary>
    public string Key { get; } = key;

    /// <summary>The sub-layer's name.</summary>
    public string Title { get; } = title;

    /// <summary>Whether <see cref="Title"/> shows above the rows.</summary>
    public bool ShowTitle { get; } = showTitle;

    /// <summary>The rows shown.</summary>
    public IReadOnlyList<LegendRowViewModel> Rows { get; } = rows;

    /// <summary>The same group with only some of its rows, for the filter.</summary>
    /// <param name="subset">The rows to show.</param>
    public LegendGroupViewModel WithRows(IReadOnlyList<LegendRowViewModel> subset) => new(Key, Title, ShowTitle, subset);
}
