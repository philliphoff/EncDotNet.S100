using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class DatasetEntry : ViewModelBase
{
    public string FilePath { get; }
    public string DisplayName { get; }
    public string ProductSpec { get; }

    private bool _isLoaded;
    public bool IsLoaded
    {
        get => _isLoaded;
        set => SetProperty(ref _isLoaded, value);
    }

    // ── Per-dataset display state ─────────────────────────────────────
    //
    // These properties drive the underlying Mapsui ILayer.Enabled and
    // ILayer.Opacity values via DatasetLoaderService. They survive
    // re-renders (palette switches, time-step scrubs) because the
    // loader re-applies them inside ReplaceLayers.

    private bool _isVisible = true;
    /// <summary>
    /// Whether this dataset's layers are drawn on the map. Toggling
    /// this updates <see cref="Mapsui.Layers.ILayer.Enabled"/> on every
    /// layer the loader has produced for this entry.
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
                OnPropertyChanged(nameof(RowOpacity));
        }
    }

    private double _opacity = 1.0;
    /// <summary>
    /// Opacity factor applied to this dataset's layers, in the range
    /// 0..1. Updates <see cref="Mapsui.Layers.ILayer.Opacity"/>.
    /// </summary>
    public double Opacity
    {
        get => _opacity;
        set
        {
            var clamped = value < 0 ? 0 : value > 1 ? 1 : value;
            SetProperty(ref _opacity, clamped);
        }
    }

    /// <summary>
    /// UI helper: dims the row text when the dataset is hidden.
    /// </summary>
    public double RowOpacity => _isVisible ? 1.0 : 0.5;

    /// <summary>
    /// Flips <see cref="IsVisible"/>. Bound to the eye-icon button in
    /// the Datasets list so the action behaves as a transient command
    /// (icon swaps in response to state change) rather than a
    /// persistent toggle button (which would render as accent-checked).
    /// </summary>
    public ICommand ToggleVisibilityCommand { get; }

    // ── Sub-layers ────────────────────────────────────────────────────
    //
    // Products that emit more than one Mapsui ILayer (S-111 colour
    // band + arrows; future S-101 fan-out) expose them here so the
    // user can toggle each one independently. Single-layer products
    // leave this collection empty and the UI hides the disclosure
    // triangle. The collection is populated and reconciled by
    // DatasetLoaderService inside ReplaceLayers — never reset, only
    // mutated in place — so user toggles survive palette switches and
    // time-step scrubs.

    private readonly ObservableCollection<DatasetSubLayer> _subLayers = new();
    public ObservableCollection<DatasetSubLayer> SubLayers => _subLayers;

    /// <summary>
    /// True when this dataset has more than one sub-layer and the
    /// disclosure UI should be shown.
    /// </summary>
    public bool HasSubLayers => _subLayers.Count > 1;

    private string? _info;
    public string? Info
    {
        get => _info;
        set => SetProperty(ref _info, value);
    }

    // ── Time-aware participation ──────────────────────────────────────
    //
    // Time-step navigation is global: per-entry prev/next/ComboBox
    // controls were replaced by a single timeline panel beneath the
    // map (TimelineView). Each entry exposes only:
    //   • the time samples the loader discovered for this dataset, and
    //   • the timestamp it is currently rendered at (a read-only label).

    private IReadOnlyList<DateTime>? _availableTimes;
    public IReadOnlyList<DateTime>? AvailableTimes
    {
        get => _availableTimes;
        set
        {
            if (SetProperty(ref _availableTimes, value))
                OnPropertyChanged(nameof(HasTimeSteps));
        }
    }

    /// <summary>True when this dataset has at least one time sample.</summary>
    public bool HasTimeSteps => _availableTimes is { Count: > 0 };

    private DateTime? _currentTime;
    /// <summary>
    /// The timestamp this dataset is currently rendered at, or
    /// <c>null</c> when the dataset is not time-aware or has not yet
    /// been rendered. Set by <see cref="IDatasetLoaderService"/>.
    /// </summary>
    public DateTime? CurrentTime
    {
        get => _currentTime;
        set
        {
            if (SetProperty(ref _currentTime, value))
                OnPropertyChanged(nameof(CurrentTimeLabel));
        }
    }

    /// <summary>
    /// Display label for <see cref="CurrentTime"/>, or empty when no
    /// time has been assigned. Formatted via
    /// <see cref="Strings.DatasetEntry_CurrentTimeFormat"/>.
    /// </summary>
    public string CurrentTimeLabel =>
        _currentTime is { } t
            ? string.Format(CultureInfo.CurrentCulture, Strings.DatasetEntry_CurrentTimeFormat, t)
            : string.Empty;

    public DatasetEntry(string filePath, string productSpec)
    {
        FilePath = filePath;
        ProductSpec = productSpec;
        DisplayName = System.IO.Path.GetFileName(filePath);
        ToggleVisibilityCommand = new RelayCommand(() => IsVisible = !IsVisible);

        _subLayers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSubLayers));
    }
}

/// <summary>
/// Represents one of the Mapsui layers a dataset is rendered as,
/// surfaced with a per-layer visibility toggle and opacity slider.
/// The combined effective state is computed by
/// <see cref="Services.DatasetLoaderService"/>:
///   <c>layer.Enabled = parent.IsVisible &amp;&amp; sub.IsVisible</c> and
///   <c>layer.Opacity = parent.Opacity * sub.Opacity</c>.
/// </summary>
internal sealed class DatasetSubLayer : ViewModelBase
{
    /// <summary>
    /// Stable key supplied by the dataset processor (e.g.
    /// <c>"s111.arrows"</c>). Used to reconcile sub-layers across
    /// re-renders so a palette switch or time-scrub does not reset
    /// user-driven toggles.
    /// </summary>
    public string Key { get; }

    public string DisplayName { get; }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    private double _opacity = 1.0;
    public double Opacity
    {
        get => _opacity;
        set
        {
            var clamped = value < 0 ? 0 : value > 1 ? 1 : value;
            SetProperty(ref _opacity, clamped);
        }
    }

    /// <summary>
    /// Flips <see cref="IsVisible"/>. See the rationale on
    /// <see cref="DatasetEntry.ToggleVisibilityCommand"/>.
    /// </summary>
    public ICommand ToggleVisibilityCommand { get; }

    public DatasetSubLayer(string key, string displayName)
    {
        Key = key;
        DisplayName = displayName;
        ToggleVisibilityCommand = new RelayCommand(() => IsVisible = !IsVisible);
    }
}

internal sealed class DatasetsViewModel : ViewModelBase
{
    private readonly IDatasetLoaderService _loader;

    public ObservableCollection<DatasetEntry> Entries { get; } = new();

    private DatasetEntry? _selectedEntry;
    /// <summary>
    /// The dataset row currently highlighted in the panel. Drives the
    /// Properties sub-panel (opacity slider + sub-layer list).
    /// </summary>
    public DatasetEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!ReferenceEquals(_selectedEntry, value))
            {
                _selectedEntry = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    /// <summary>True when a dataset row is selected; controls visibility
    /// of the Properties sub-panel.</summary>
    public bool HasSelection => _selectedEntry is not null;

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }

    /// <summary>Moves the supplied entry one slot up in the panel (toward index 0).</summary>
    public ICommand MoveUpCommand { get; }
    /// <summary>Moves the supplied entry one slot down in the panel.</summary>
    public ICommand MoveDownCommand { get; }
    /// <summary>
    /// Moves the supplied entry to the top of the list, which makes its
    /// layers paint last (above every other dataset).
    /// </summary>
    public ICommand BringToFrontCommand { get; }
    /// <summary>
    /// Moves the supplied entry to the bottom of the list, which makes
    /// its layers paint first (below every other dataset).
    /// </summary>
    public ICommand SendToBackCommand { get; }

    // ── Bulk actions ─────────────────────────────────────────────────
    /// <summary>Sets <see cref="DatasetEntry.IsVisible"/> to true on every entry.</summary>
    public ICommand ShowAllCommand { get; }
    /// <summary>Sets <see cref="DatasetEntry.IsVisible"/> to false on every entry.</summary>
    public ICommand HideAllCommand { get; }
    /// <summary>
    /// Hides every dataset except the supplied one. Surfaced from the
    /// per-row context menu so no list-selection state is required.
    /// </summary>
    public ICommand IsolateCommand { get; }
    /// <summary>
    /// Resets <see cref="DatasetEntry.Opacity"/> to 1.0 on every entry
    /// and on every <see cref="DatasetEntry.SubLayers"/> child.
    /// </summary>
    public ICommand ResetOpacityCommand { get; }

    /// <summary>
    /// Raised when <see cref="LoadFromPathAsync"/> rejects a file because
    /// no S-100 product specification recognised its extension. The window
    /// surfaces this as a status-bar message.
    /// </summary>
    public event Action<string>? UnrecognizedFileEncountered;

    public DatasetsViewModel(IDatasetLoaderService loader, GlobalTimeService? globalTime = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;

        AddCommand = new RelayCommand<string?>(_ => { });
        RemoveCommand = new RelayCommand<DatasetEntry>(Remove);
        MoveUpCommand = new RelayCommand<DatasetEntry>(MoveUp);
        MoveDownCommand = new RelayCommand<DatasetEntry>(MoveDown);
        BringToFrontCommand = new RelayCommand<DatasetEntry>(BringToFront);
        SendToBackCommand = new RelayCommand<DatasetEntry>(SendToBack);
        ShowAllCommand = new RelayCommand(ShowAll);
        HideAllCommand = new RelayCommand(HideAll);
        IsolateCommand = new RelayCommand<DatasetEntry>(Isolate);
        ResetOpacityCommand = new RelayCommand(ResetOpacity);

        // Re-apply paint order whenever the entries collection is
        // mutated by reorder commands. Add/remove side-effects flow
        // through the loader's Load/RemoveEntry path; we only need to
        // push order changes here. Coalescing per-event keeps Mapsui
        // mutations cheap (a single removeAll + insertAll round-trip
        // per move). See <see cref="DatasetLoaderService.SetEntryOrder"/>
        // for the host-side reorder logic.
        Entries.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
                _loader.SetEntryOrder(Entries.ToArray());
        };

        // Auto-unregister entries from the global time service when they
        // are removed from the collection.
        globalTime?.AttachTo(this);
    }

    public DatasetEntry Add(string filePath, string productSpec)
    {
        var entry = new DatasetEntry(filePath, productSpec);
        // Photoshop/QGIS convention: list index 0 is the top of the
        // paint stack (drawn last, on top of every other dataset). New
        // datasets are inserted at the top so they overlay existing
        // ones by default.
        Entries.Insert(0, entry);
        return entry;
    }

    /// <summary>
    /// Loads the supplied entry through the dataset loader. Fire-and-forget;
    /// errors are surfaced via <see cref="IDatasetLoaderService.StatusChanged"/>.
    /// </summary>
    public void RequestLoad(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _ = _loader.LoadAsync(entry);
    }

    /// <summary>
    /// Detects the product spec for <paramref name="path"/>, adds an entry,
    /// and asks the loader to render it. If the file extension is not
    /// recognised, raises <see cref="UnrecognizedFileEncountered"/> with the
    /// extension and returns <c>null</c>.
    /// </summary>
    public async Task<DatasetEntry?> LoadFromPathAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var spec = Datasets.Pipelines.DatasetPipelineFactory.DetectProductSpec(path);
        if (spec is null)
        {
            UnrecognizedFileEncountered?.Invoke(System.IO.Path.GetExtension(path));
            return null;
        }

        var entry = Add(path, spec);
        await _loader.LoadAsync(entry);
        return entry;
    }

    private void Remove(DatasetEntry? entry)
    {
        if (entry is null) return;
        Entries.Remove(entry);
    }

    private void MoveUp(DatasetEntry? entry)
    {
        if (entry is null) return;
        var i = Entries.IndexOf(entry);
        if (i > 0) Entries.Move(i, i - 1);
    }

    private void MoveDown(DatasetEntry? entry)
    {
        if (entry is null) return;
        var i = Entries.IndexOf(entry);
        if (i >= 0 && i < Entries.Count - 1) Entries.Move(i, i + 1);
    }

    private void BringToFront(DatasetEntry? entry)
    {
        if (entry is null) return;
        var i = Entries.IndexOf(entry);
        if (i >= 0 && i != 0) Entries.Move(i, 0);
    }

    private void SendToBack(DatasetEntry? entry)
    {
        if (entry is null) return;
        var i = Entries.IndexOf(entry);
        var last = Entries.Count - 1;
        if (i >= 0 && i != last) Entries.Move(i, last);
    }

    private void ShowAll()
    {
        foreach (var e in Entries) e.IsVisible = true;
    }

    private void HideAll()
    {
        foreach (var e in Entries) e.IsVisible = false;
    }

    private void Isolate(DatasetEntry? entry)
    {
        if (entry is null) return;
        foreach (var e in Entries) e.IsVisible = ReferenceEquals(e, entry);
    }

    private void ResetOpacity()
    {
        foreach (var e in Entries)
        {
            e.Opacity = 1.0;
            foreach (var sub in e.SubLayers) sub.Opacity = 1.0;
        }
    }
}
