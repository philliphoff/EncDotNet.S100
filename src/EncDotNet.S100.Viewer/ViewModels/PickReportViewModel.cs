using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
/// As of milestone 1 of the pick roadmap, attribute rows carry FC-resolved
/// names and (where applicable) decoded enumeration labels via
/// <see cref="PickAttribute"/>. Multi-feature picks and xlink:href
/// navigation are deferred to follow-up milestones.
/// </remarks>
internal sealed class PickReportViewModel : ViewModelBase
{
    private string? _featureType;
    private string? _featureTypeName;
    private string? _featureRef;
    private string? _datasetFileName;
    private string? _productSpec;
    private bool _hasPick;

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
    /// Attribute rows for the picked feature, decoded against the dataset's
    /// Feature Catalogue when one is available. Complex attributes nest their
    /// sub-rows via <see cref="PickAttribute.Children"/>; the panel renders
    /// the collection through a TreeView.
    /// </summary>
    public ObservableCollection<PickAttribute> Attributes { get; } = new();

    /// <summary>True when the current pick has at least one displayable attribute.</summary>
    public bool HasAttributes => Attributes.Count > 0;

    /// <summary>Clears the panel.</summary>
    public ICommand ClearCommand { get; }

    /// <summary>
    /// Replaces the current pick with the supplied feature.
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

        FeatureType = featureType;
        FeatureTypeName = featureTypeName;
        FeatureRef = featureRef;
        DatasetFileName = datasetFileName;
        ProductSpec = productSpec;

        Attributes.Clear();
        foreach (var attr in attributes)
            Attributes.Add(attr);

        HasPick = true;
        OnPropertyChanged(nameof(HasAttributes));
    }

    /// <summary>Clears all pick state and sets <see cref="HasPick"/> to false.</summary>
    public void Clear()
    {
        FeatureType = null;
        FeatureTypeName = null;
        FeatureRef = null;
        DatasetFileName = null;
        ProductSpec = null;
        Attributes.Clear();
        HasPick = false;
        OnPropertyChanged(nameof(HasAttributes));
    }
}
