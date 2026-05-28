using System;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Validation;
using EncDotNet.S100.Viewer.Resources;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Display-ready wrapper around a single <see cref="ValidationFinding"/>
/// surfaced in the dataset properties panel's Validation tab. Immutable —
/// findings never mutate after a dataset finishes loading.
/// </summary>
internal sealed class ValidationFindingViewModel
{
    /// <summary>
    /// Half-side, in metres (EPSG:3857), of the padded square used to
    /// zoom to a point-only finding. ~500 m gives a comfortable
    /// feature-level view at roughly 1:50,000.
    /// </summary>
    internal const double PointZoomHalfMetres = 500.0;

    private readonly Action<MRect>? _zoomDispatcher;

    public ValidationFindingViewModel(ValidationFinding finding)
        : this(finding, zoomDispatcher: null)
    {
    }

    /// <summary>
    /// Creates a finding view-model that routes
    /// <see cref="ZoomToFindingCommand"/> through
    /// <paramref name="zoomDispatcher"/>. Pass <c>null</c> in tests or
    /// non-map surfaces; the command will then no-op while
    /// <see cref="HasSpatialLocation"/> still reflects the truth.
    /// </summary>
    public ValidationFindingViewModel(ValidationFinding finding, Action<MRect>? zoomDispatcher)
    {
        Finding = finding;
        _zoomDispatcher = zoomDispatcher;
        SeverityClass = finding.Severity switch
        {
            ValidationSeverity.Error => "Error",
            ValidationSeverity.Warning => "Warning",
            _ => "Info",
        };
        SeverityIcon = finding.Severity switch
        {
            ValidationSeverity.Error => "ErrorCircle",
            ValidationSeverity.Warning => "Warning",
            _ => "Info",
        };
        SeverityLabel = finding.Severity switch
        {
            ValidationSeverity.Error => Strings.Pane_Validation_Severity_Error,
            ValidationSeverity.Warning => Strings.Pane_Validation_Severity_Warning,
            _ => Strings.Pane_Validation_Severity_Info,
        };
        HasRelatedFeature = !string.IsNullOrEmpty(finding.RelatedFeatureId);
        RelatedFeatureLine = HasRelatedFeature
            ? string.Format(CultureInfo.CurrentCulture, Strings.Pane_Validation_RelatedFeatureFormat,
                finding.RelatedFeatureId)
            : string.Empty;
        ZoomToFindingCommand = new RelayCommand(ExecuteZoomToFinding, () => HasSpatialLocation);
    }

    public ValidationFinding Finding { get; }

    /// <summary>Rule identifier (e.g. <c>S125-R-1.1</c>).</summary>
    public string RuleId => Finding.RuleId;

    /// <summary>Human-readable message describing the finding.</summary>
    public string Message => Finding.Message;

    /// <summary>Severity for icon / class binding.</summary>
    public ValidationSeverity Severity => Finding.Severity;

    /// <summary><c>true</c> when this finding is an Error — bound to <c>Classes.Error</c>.</summary>
    public bool IsError => Finding.Severity == ValidationSeverity.Error;

    /// <summary><c>true</c> when this finding is a Warning — bound to <c>Classes.Warning</c>.</summary>
    public bool IsWarning => Finding.Severity == ValidationSeverity.Warning;

    /// <summary><c>true</c> when this finding is Info — bound to <c>Classes.Info</c>.</summary>
    public bool IsInfo => Finding.Severity == ValidationSeverity.Info;

    /// <summary>
    /// Localised severity label (e.g. "Error") used in tooltips and
    /// accessibility surfaces.
    /// </summary>
    public string SeverityLabel { get; }

    /// <summary>
    /// XAML class name (<c>Error</c> / <c>Warning</c> / <c>Info</c>)
    /// applied to the severity icon for colour theming.
    /// </summary>
    public string SeverityClass { get; }

    /// <summary>
    /// Name of the FluentIcon glyph rendered for this severity
    /// (<c>ErrorCircle</c> / <c>Warning</c> / <c>Info</c>).
    /// </summary>
    public string SeverityIcon { get; }

    /// <summary><c>true</c> when <see cref="ValidationFinding.RelatedFeatureId"/> is set.</summary>
    public bool HasRelatedFeature { get; }

    /// <summary>
    /// Localised line of text describing the related feature, e.g.
    /// "Feature: BUOYAGE.1". Empty when no related feature is set.
    /// </summary>
    public string RelatedFeatureLine { get; }

    /// <summary>Optional point location for click-to-zoom and map overlay.</summary>
    public GeoPosition? Point => Finding.Point;

    /// <summary>Optional bounding-box location for click-to-zoom and map overlay.</summary>
    public BoundingBox? BoundingBox => Finding.BoundingBox;

    /// <summary>
    /// <c>true</c> when the finding carries any spatial information
    /// (either <see cref="Point"/> or <see cref="BoundingBox"/>) that
    /// the click-to-zoom command and the map overlay can use.
    /// </summary>
    public bool HasSpatialLocation => Finding.Point is not null || Finding.BoundingBox is not null;

    /// <summary>
    /// Localised tooltip for the clickable finding row: the zoom-to
    /// hint when a spatial location is present, otherwise the
    /// no-location explanation.
    /// </summary>
    public string ZoomTooltip => HasSpatialLocation
        ? Strings.Tooltip_FindingZoomTo
        : Strings.Tooltip_FindingNoSpatialLocation;

    /// <summary>
    /// Command that zooms the map to this finding's spatial location.
    /// Disabled when <see cref="HasSpatialLocation"/> is <c>false</c>
    /// or no zoom dispatcher has been supplied. When both
    /// <see cref="Point"/> and <see cref="BoundingBox"/> are present
    /// the bounding box wins (richer extent). A point-only finding
    /// resolves to a small padded square (~500 m on a side) around
    /// the point so the zoom lands at a sensible feature-level scale.
    /// </summary>
    public ICommand ZoomToFindingCommand { get; }

    /// <summary>
    /// Resolves the EPSG:3857 extent this finding should zoom to, or
    /// <c>null</c> when the finding has no spatial information.
    /// Exposed internally for tests; the command uses the same logic.
    /// </summary>
    internal MRect? ResolveZoomExtent()
    {
        if (Finding.BoundingBox is { } bbox)
        {
            var (minX, minY) = SphericalMercator.FromLonLat(bbox.WestLongitude, bbox.SouthLatitude);
            var (maxX, maxY) = SphericalMercator.FromLonLat(bbox.EastLongitude, bbox.NorthLatitude);
            return new MRect(minX, minY, maxX, maxY);
        }
        if (Finding.Point is { } p)
        {
            var (cx, cy) = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
            return new MRect(
                cx - PointZoomHalfMetres,
                cy - PointZoomHalfMetres,
                cx + PointZoomHalfMetres,
                cy + PointZoomHalfMetres);
        }
        return null;
    }

    private void ExecuteZoomToFinding()
    {
        if (_zoomDispatcher is null) return;
        if (ResolveZoomExtent() is { } extent)
        {
            _zoomDispatcher(extent);
        }
    }
}
