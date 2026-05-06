using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model that backs the Pick Report (Object Information) side panel.
/// Holds the most recently picked feature's identity, originating dataset,
/// and attribute list.
/// </summary>
/// <remarks>
/// Milestone 1 introduced FC-resolved attribute decoding; milestone 2 adds
/// multi-feature picks: <see cref="Hits"/> carries every overlapping feature
/// at the click point, <see cref="SelectedHit"/> drives the detail view, and
/// <see cref="HasMultipleHits"/> gates the hit-list UI. Picks with a single
/// hit behave exactly like before (hit-list hidden).
/// </remarks>
internal sealed class PickReportViewModel : ViewModelBase
{
    private string? _featureType;
    private string? _featureTypeName;
    private string? _featureRef;
    private string? _datasetFileName;
    private string? _productSpec;
    private bool _hasPick;
    private PickHit? _selectedHit;

    public PickReportViewModel()
    {
        ClearCommand = new RelayCommand(Clear);
    }

    /// <summary>The picked feature's class/type code (e.g. "DepthArea", "LateralBuoy").</summary>
    public string? FeatureType
    {
        get => _featureType;
        private set => SetProperty(ref _featureType, value);
    }

    /// <summary>FC-resolved human-readable name of the feature type, when available.</summary>
    public string? FeatureTypeName
    {
        get => _featureTypeName;
        private set => SetProperty(ref _featureTypeName, value);
    }

    /// <summary>The picked feature's dataset-specific reference identifier.</summary>
    public string? FeatureRef
    {
        get => _featureRef;
        private set => SetProperty(ref _featureRef, value);
    }

    /// <summary>File name (no path) of the dataset the picked feature came from.</summary>
    public string? DatasetFileName
    {
        get => _datasetFileName;
        private set => SetProperty(ref _datasetFileName, value);
    }

    /// <summary>Product specification of the source dataset (e.g. "S-101").</summary>
    public string? ProductSpec
    {
        get => _productSpec;
        private set => SetProperty(ref _productSpec, value);
    }

    /// <summary>True when a feature is currently displayed in the panel.</summary>
    public bool HasPick
    {
        get => _hasPick;
        private set => SetProperty(ref _hasPick, value);
    }

    /// <summary>
    /// Ordered list of every feature the most recent pick gesture hit. The
    /// first entry is selected by default (matching the legacy single-hit
    /// behaviour); when <see cref="HasMultipleHits"/> is true the panel
    /// renders a selectable list above the detail view.
    /// </summary>
    public ObservableCollection<PickHit> Hits { get; } = new();

    /// <summary>
    /// The hit currently shown in the detail view. Two-way bound to the
    /// hit-list selection in the panel. Setting this property updates
    /// <see cref="FeatureType"/>, <see cref="FeatureRef"/>,
    /// <see cref="Attributes"/>, etc., to match the new selection.
    /// </summary>
    public PickHit? SelectedHit
    {
        get => _selectedHit;
        set
        {
            if (ReferenceEquals(_selectedHit, value))
                return;
            _selectedHit = value;
            OnPropertyChanged();
            ApplyHitToDetailFields(value);
        }
    }

    /// <summary>True when more than one feature was hit at the pick location.</summary>
    public bool HasMultipleHits => Hits.Count > 1;

    /// <summary>
    /// Attribute rows for the picked feature, decoded against the dataset's
    /// Feature Catalogue when one is available. Complex attributes nest their
    /// sub-rows via <see cref="PickAttribute.Children"/>; the panel renders
    /// the collection through a TreeView.
    /// </summary>
    public ObservableCollection<PickAttribute> Attributes { get; } = new();

    /// <summary>True when the current pick has at least one displayable attribute.</summary>
    public bool HasAttributes => Attributes.Count > 0;

    /// <summary>
    /// xlink-style references the currently selected hit points to.
    /// Surfaced in the panel above the attributes table; clicking a row
    /// invokes <see cref="NavigateCommand"/> to re-open the panel on the
    /// referenced feature.
    /// </summary>
    public ObservableCollection<FeatureReference> References { get; } = new();

    /// <summary>True when the selected hit has at least one outbound reference.</summary>
    public bool HasReferences => References.Count > 0;

    /// <summary>
    /// Invoked from the References list. Parameter is the
    /// <see cref="FeatureReference"/> to follow; the actual lookup is
    /// delegated to the handler supplied by
    /// <see cref="SetNavigateHandler"/>. The view-model owns no service
    /// references directly so unit tests can drive it without a
    /// pick-service double.
    /// </summary>
    public ICommand NavigateCommand { get; private set; } = new RelayCommand<FeatureReference>(_ => { }, _ => false);

    /// <summary>Clears the panel.</summary>
    public ICommand ClearCommand { get; }

    /// <summary>
    /// Wires a navigation handler. <see cref="PickService"/> calls this
    /// once at construction to bridge <see cref="NavigateCommand"/> back
    /// into the service. The handler is invoked synchronously on the UI
    /// thread for each click.
    /// </summary>
    public void SetNavigateHandler(Action<FeatureReference> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        NavigateCommand = new RelayCommand<FeatureReference>(
            r => { if (r is not null) handler(r); },
            r => r is not null);
        OnPropertyChanged(nameof(NavigateCommand));
    }

    /// <summary>
    /// Replaces the current pick with the supplied list of hits. The first
    /// hit is selected by default. An empty list is equivalent to
    /// <see cref="Clear"/>.
    /// </summary>
    public void SetPicks(IReadOnlyList<PickHit> hits)
    {
        ArgumentNullException.ThrowIfNull(hits);

        Hits.Clear();
        foreach (var hit in hits)
            Hits.Add(hit);

        if (hits.Count == 0)
        {
            Clear();
            return;
        }

        // Setting SelectedHit propagates the values into the detail fields.
        // Order matters: HasPick and HasMultipleHits must be observable
        // before consumers react to SelectedHit changes.
        HasPick = true;
        OnPropertyChanged(nameof(HasMultipleHits));
        SelectedHit = hits[0];
    }

    /// <summary>
    /// Convenience overload that wraps a single-feature pick into the
    /// multi-hit shape. Preserved for callers that haven't migrated to
    /// <see cref="SetPicks"/>.
    /// </summary>
    public void SetPick(
        string featureType,
        string? featureTypeName,
        string featureRef,
        string? datasetFileName,
        string? productSpec,
        IReadOnlyList<PickAttribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(featureType);
        ArgumentNullException.ThrowIfNull(featureRef);
        ArgumentNullException.ThrowIfNull(attributes);

        SetPicks(new[]
        {
            new PickHit
            {
                FeatureType = featureType,
                FeatureTypeName = featureTypeName,
                FeatureRef = featureRef,
                DatasetFileName = datasetFileName,
                ProductSpec = productSpec,
                Attributes = attributes,
            },
        });
    }

    /// <summary>Clears all pick state and sets <see cref="HasPick"/> to false.</summary>
    public void Clear()
    {
        Hits.Clear();
        // SelectedHit setter rejects identical references; clear the backing
        // field directly so we always raise PropertyChanged when something
        // was selected.
        if (_selectedHit is not null)
        {
            _selectedHit = null;
            OnPropertyChanged(nameof(SelectedHit));
        }

        FeatureType = null;
        FeatureTypeName = null;
        FeatureRef = null;
        DatasetFileName = null;
        ProductSpec = null;
        Attributes.Clear();
        References.Clear();
        HasPick = false;
        OnPropertyChanged(nameof(HasAttributes));
        OnPropertyChanged(nameof(HasReferences));
        OnPropertyChanged(nameof(HasMultipleHits));
    }

    private void ApplyHitToDetailFields(PickHit? hit)
    {
        if (hit is null)
        {
            FeatureType = null;
            FeatureTypeName = null;
            FeatureRef = null;
            DatasetFileName = null;
            ProductSpec = null;
            Attributes.Clear();
            References.Clear();
            OnPropertyChanged(nameof(HasAttributes));
            OnPropertyChanged(nameof(HasReferences));
            return;
        }

        FeatureType = hit.FeatureType;
        FeatureTypeName = hit.FeatureTypeName;
        FeatureRef = hit.FeatureRef;
        DatasetFileName = hit.DatasetFileName;
        ProductSpec = hit.ProductSpec;

        Attributes.Clear();
        foreach (var attr in hit.Attributes)
            Attributes.Add(attr);
        OnPropertyChanged(nameof(HasAttributes));

        References.Clear();
        foreach (var reference in hit.References)
            References.Add(reference);
        OnPropertyChanged(nameof(HasReferences));
    }
}
