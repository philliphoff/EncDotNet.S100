using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels.Activities;

namespace EncDotNet.S100.Viewer.ViewModels.Legends;

/// <summary>
/// The Legend tab of the right dock (legends design note D2, D4, #1404): a
/// toolbar (<b>In view | All loaded</b>, a filter, <b>Collapse all</b>), the
/// fixed rows above the sections (<see cref="PinnedRows"/>, D17) and one
/// collapsible section per product from <see cref="LegendService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The tab never opens by itself (D14). It opens from View › Legend and from
/// <see cref="LegendService.Show"/> (the Layer stack's <b>Legend</b> link, D8),
/// which raises <see cref="ContentBecameAvailable"/> so the right dock selects
/// it.
/// </para>
/// <para>
/// Section view models are kept by id across rebuilds, so a pan or a redraw
/// doesn't reset their expanders. Call on the UI thread; the service raises
/// <see cref="LegendService.Changed"/> there.
/// </para>
/// </remarks>
internal sealed class LegendViewModel : ViewModelBase, IActivityTabShown, IActivityTabContentSignal, IDisposable
{
    /// <summary>The toolbar's scope index of In view.</summary>
    public const int InViewScope = 0;

    /// <summary>The toolbar's scope index of All loaded.</summary>
    public const int AllLoadedScope = 1;

    private static readonly Dictionary<PaletteType, IBrush> Tiles = [];

    private readonly LegendService _service;
    private readonly ITimeFormatProvider _timeFormat;
    private readonly IViewTime _viewTime;
    private readonly Dictionary<string, LegendSectionViewModel> _byId = new(StringComparer.Ordinal);
    private int _scopeIndex = InViewScope;
    private string _filter = string.Empty;
    private int _notInViewCount;
    private LegendRowViewModel? _highlighted;

    /// <summary>Creates the tab over the service.</summary>
    /// <param name="service">The sections.</param>
    /// <param name="viewTime">The view time, for "No data at 14:20".</param>
    /// <param name="timeFormat">The user's Local/UTC setting.</param>
    public LegendViewModel(LegendService service, IViewTime viewTime, ITimeFormatProvider timeFormat)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(viewTime);
        ArgumentNullException.ThrowIfNull(timeFormat);
        _service = service;
        _viewTime = viewTime;
        _timeFormat = timeFormat;
        CollapseAllCommand = new RelayCommand(CollapseAll, () => Sections.Any(s => s.IsExpanded));
        ShowAllLoadedCommand = new RelayCommand(() => ScopeIndex = AllLoadedScope);
        ClearFilterCommand = new RelayCommand(() => Filter = string.Empty);
        PinnedRows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPinnedRows));
        _service.Changed += OnServiceChanged;
        _service.ShowRequested += OnShowRequested;
        _timeFormat.TimeFormatChanged += OnTimeFormatChanged;
        Rebuild();
    }

    /// <summary>The sections shown: in the scope, with a row the filter matches; top of the stack first.</summary>
    public ObservableCollection<LegendSectionViewModel> Sections { get; } = [];

    /// <summary>
    /// Rows fixed above the sections, under the toolbar, that don't scroll or
    /// collapse: the S-98 position legend (D17, #1407). Any view model; the
    /// Legend view picks its template by type.
    /// </summary>
    public ObservableCollection<object> PinnedRows { get; } = [];

    /// <summary>Whether <see cref="PinnedRows"/> has a row.</summary>
    public bool HasPinnedRows => PinnedRows.Count > 0;

    /// <summary><see cref="InViewScope"/> or <see cref="AllLoadedScope"/>: the toolbar's <b>In view | All loaded</b>.</summary>
    public int ScopeIndex
    {
        get => _scopeIndex;
        set
        {
            if (value is not (InViewScope or AllLoadedScope) || !SetProperty(ref _scopeIndex, value))
                return;
            OnPropertyChanged(nameof(IsAllLoaded));
            Rebuild();
        }
    }

    /// <summary>Whether the tab lists every loaded product, not only those in view.</summary>
    public bool IsAllLoaded
    {
        get => _scopeIndex == AllLoadedScope;
        set => ScopeIndex = value ? AllLoadedScope : InViewScope;
    }

    /// <summary>The filter text: shows only the sections and rows whose names contain it.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasFilter));
                Rebuild();
            }
        }
    }

    /// <summary>Whether a filter is typed.</summary>
    public bool HasFilter => _filter.Trim().Length > 0;

    /// <summary>Collapses every section shown.</summary>
    public IRelayCommand CollapseAllCommand { get; }

    /// <summary>Switches to All loaded, from the "not in view" line.</summary>
    public IRelayCommand ShowAllLoadedCommand { get; }

    /// <summary>Clears the filter.</summary>
    public IRelayCommand ClearFilterCommand { get; }

    /// <summary>In view: how many loaded products with a legend aren't in view.</summary>
    public int NotInViewCount
    {
        get => _notInViewCount;
        private set
        {
            if (SetProperty(ref _notInViewCount, value))
            {
                OnPropertyChanged(nameof(HasNotInView));
                OnPropertyChanged(nameof(NotInViewText));
            }
        }
    }

    /// <summary>Whether the muted "not in view" line shows (D4).</summary>
    public bool HasNotInView => _notInViewCount > 0 && !IsAllLoaded;

    /// <summary>"2 more loaded, not in view".</summary>
    public string NotInViewText => string.Format(
        CultureInfo.CurrentCulture,
        _notInViewCount == 1 ? Strings.Legend_NotInViewOneFormat : Strings.Legend_NotInViewFormat,
        _notInViewCount);

    /// <summary>Whether no section shows.</summary>
    public bool IsEmpty => Sections.Count == 0;

    /// <summary>The empty state's heading.</summary>
    public string EmptyTitle =>
        _service.Sections.Count == 0 ? Strings.Legend_EmptyTitle
        : HasFilter ? Strings.Legend_NoMatchTitle
        : Strings.Legend_NothingInViewTitle;

    /// <summary>The empty state's explanation.</summary>
    public string EmptyDescription =>
        _service.Sections.Count == 0 ? Strings.Legend_EmptyDescription
        : HasFilter ? string.Format(CultureInfo.CurrentCulture, Strings.Legend_NoMatchDescriptionFormat, _filter.Trim())
        : Strings.Legend_NothingInViewDescription;

    /// <summary>The palette's chart background, under every glyph (D10).</summary>
    public IBrush ChartBackground => TileOf(_service.Palette);

    /// <inheritdoc />
    public bool IsTabShown
    {
        get => _service.IsTabShown;
        set
        {
            if (_service.IsTabShown == value)
                return;
            _service.IsTabShown = value;
            if (!value)
                Highlight(null);
            OnPropertyChanged();
        }
    }

    /// <inheritdoc />
    /// <remarks>Raised only by <see cref="LegendService.Show"/>: the tab never opens on its own (D14).</remarks>
    public event EventHandler? ContentBecameAvailable;

    /// <summary>
    /// Raised after <see cref="ShowSection"/> so the view scrolls the section
    /// (and the ringed row, when there is one) into view.
    /// </summary>
    public event EventHandler<LegendRevealEventArgs>? RevealRequested;

    /// <summary>
    /// Shows a section: All loaded when it isn't in view, no filter when the
    /// filter hides it, then expands it, rings the named row and asks the
    /// view to scroll to it.
    /// </summary>
    /// <param name="sectionId">The section id.</param>
    /// <param name="rowLabel">The label of the row to ring, or null.</param>
    /// <returns>False when no loaded product has that section.</returns>
    public bool ShowSection(string sectionId, string? rowLabel = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sectionId);
        var section = _service.Sections.FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.Ordinal));
        if (section is null)
            return false;

        if (!section.IsInView && !IsAllLoaded)
            ScopeIndex = AllLoadedScope;
        if (!Sections.Any(s => string.Equals(s.Id, sectionId, StringComparison.Ordinal)))
            Filter = string.Empty;

        var vm = _byId[sectionId];
        vm.IsExpanded = true;
        var row = rowLabel is null ? null : vm.Rows.FirstOrDefault(r => string.Equals(r.Label, rowLabel, StringComparison.Ordinal));
        Highlight(row);
        RevealRequested?.Invoke(this, new LegendRevealEventArgs(vm, row));
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _service.Changed -= OnServiceChanged;
        _service.ShowRequested -= OnShowRequested;
        _timeFormat.TimeFormatChanged -= OnTimeFormatChanged;
    }

    private void OnServiceChanged(object? sender, EventArgs e)
    {
        Rebuild();
        OnPropertyChanged(nameof(IsTabShown));
    }

    private void OnShowRequested(object? sender, LegendShowRequest request)
    {
        ContentBecameAvailable?.Invoke(this, EventArgs.Empty);
        if (request.SectionId is { Length: > 0 } id)
            ShowSection(id, request.RowLabel);
    }

    private void OnTimeFormatChanged(TimeFormat format) => Rebuild();

    private void CollapseAll()
    {
        foreach (var section in Sections)
            section.IsExpanded = false;
        CollapseAllCommand.NotifyCanExecuteChanged();
    }

    private void Highlight(LegendRowViewModel? row)
    {
        if (ReferenceEquals(_highlighted, row))
            return;
        if (_highlighted is not null)
            _highlighted.IsHighlighted = false;
        _highlighted = row;
        if (row is not null)
            row.IsHighlighted = true;
    }

    private void Rebuild()
    {
        var tile = ChartBackground;
        var all = _service.Sections;
        var filter = _filter.Trim();
        var shown = new List<LegendSectionViewModel>();
        foreach (var section in all)
        {
            var detail = DetailOf(section);
            if (_byId.TryGetValue(section.Id, out var vm))
            {
                vm.Update(section, tile, detail);
            }
            else
            {
                vm = new LegendSectionViewModel(_service, section, tile, detail);
                vm.PropertyChanged += OnSectionPropertyChanged;
                _byId.Add(section.Id, vm);
            }

            if ((IsAllLoaded || section.IsInView) && vm.ApplyFilter(filter))
                shown.Add(vm);
        }

        foreach (var gone in _byId.Keys.Where(id => !all.Any(s => string.Equals(s.Id, id, StringComparison.Ordinal))).ToArray())
        {
            _byId[gone].PropertyChanged -= OnSectionPropertyChanged;
            _byId.Remove(gone);
        }
        if (_highlighted is not null && !_byId.Values.Any(s => s.Rows.Contains(_highlighted)))
            _highlighted = null;

        Sync(Sections, shown);
        NotInViewCount = all.Count(s => !s.IsInView);
        OnPropertyChanged(nameof(HasNotInView));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        OnPropertyChanged(nameof(ChartBackground));
        CollapseAllCommand.NotifyCanExecuteChanged();
    }

    private void OnSectionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LegendSectionViewModel.IsExpanded) or "" or null)
            CollapseAllCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The header's detail: "No data at 14:20", or the source and the time summary (D4, D5).</summary>
    private string DetailOf(LegendSection section)
    {
        if (section.HasNoDataAtViewTime)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.Legend_NoDataAtFormat,
                TimeFormatting.FormatClock(_viewTime.Current.UtcDateTime, _timeFormat.Current));
        }
        return section.TimeSummary is { Length: > 0 } time
            ? string.Format(CultureInfo.CurrentCulture, Strings.Legend_SourceAndTimeFormat, section.Source, time)
            : section.Source;
    }

    /// <summary>The tile a row's glyph sits on: the palette's chart background (D10).</summary>
    /// <param name="palette">The palette the map is drawn in.</param>
    internal static IBrush TileOf(PaletteType palette)
    {
        lock (Tiles)
        {
            if (!Tiles.TryGetValue(palette, out var brush))
            {
                var (r, g, b) = MeasureOverlayAppearance.ChartBackgroundFor(palette);
                brush = new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));
                Tiles.Add(palette, brush);
            }
            return brush;
        }
    }

    /// <summary>Makes <paramref name="target"/> hold <paramref name="items"/> in order, moving kept items rather than recreating them.</summary>
    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
        where T : class
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], items[i]))
                continue;
            var at = target.IndexOf(items[i]);
            if (at >= 0)
                target.Move(at, i);
            else
                target.Insert(i, items[i]);
        }
        while (target.Count > items.Count)
            target.RemoveAt(target.Count - 1);
    }
}

/// <summary>What <see cref="LegendViewModel.RevealRequested"/> asks the view to scroll to.</summary>
/// <param name="Section">The section.</param>
/// <param name="Row">The ringed row, or null.</param>
internal sealed record LegendRevealEventArgs(LegendSectionViewModel Section, LegendRowViewModel? Row);
