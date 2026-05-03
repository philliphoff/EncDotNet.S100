using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model that backs the Pick Report (Object Information) side panel.
/// Holds the most recently picked feature's identity, originating dataset,
/// and attribute list.
/// </summary>
/// <remarks>
/// This is a deliberately minimal ECDIS-style "pick report": it shows raw
/// attribute codes and values with no Feature-Catalogue-driven label or
/// enumerant resolution, and it tracks a single (nearest) feature at a
/// time. Multi-feature picks and FC-resolved labels are deferred to
/// follow-up iterations.
/// </remarks>
internal sealed class PickReportViewModel : ViewModelBase
{
    private string? _featureType;
    private string? _featureRef;
    private string? _datasetFileName;
    private string? _productSpec;
    private bool _hasPick;

    public PickReportViewModel()
    {
        ClearCommand = new RelayCommand(Clear);
    }

    /// <summary>The picked feature's class/type name (e.g. "DepthArea", "LateralBuoy").</summary>
    public string? FeatureType
    {
        get => _featureType;
        private set => SetProperty(ref _featureType, value);
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
    /// Attribute key/value pairs for the picked feature, with null/whitespace
    /// values filtered out. Order matches the source dictionary's enumeration.
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
        string featureRef,
        string? datasetFileName,
        string? productSpec,
        IReadOnlyDictionary<string, string?> attributes)
    {
        ArgumentNullException.ThrowIfNull(featureType);
        ArgumentNullException.ThrowIfNull(featureRef);
        ArgumentNullException.ThrowIfNull(attributes);

        FeatureType = featureType;
        FeatureRef = featureRef;
        DatasetFileName = datasetFileName;
        ProductSpec = productSpec;

        Attributes.Clear();
        foreach (var (key, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            Attributes.Add(new PickAttribute(key, value!));
        }

        HasPick = true;
        OnPropertyChanged(nameof(HasAttributes));
    }

    /// <summary>Clears all pick state and sets <see cref="HasPick"/> to false.</summary>
    public void Clear()
    {
        FeatureType = null;
        FeatureRef = null;
        DatasetFileName = null;
        ProductSpec = null;
        Attributes.Clear();
        HasPick = false;
        OnPropertyChanged(nameof(HasAttributes));
    }
}

/// <summary>A single attribute row displayed in the pick report.</summary>
internal sealed record PickAttribute(string Name, string Value);
