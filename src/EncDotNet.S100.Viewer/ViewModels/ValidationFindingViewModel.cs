using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Validation;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Display-ready wrapper around a single <see cref="ValidationFinding"/>
/// surfaced in the dataset properties panel's Validation tab. Immutable —
/// findings never mutate after a dataset finishes loading.
/// </summary>
internal sealed class ValidationFindingViewModel
{
    public ValidationFindingViewModel(ValidationFinding finding)
    {
        Finding = finding;
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

    /// <summary>Optional point location, plumbed for future click-to-zoom (not bound in v1).</summary>
    public GeoPosition? Point => Finding.Point;

    /// <summary>Optional bounding-box location, plumbed for future click-to-zoom (not bound in v1).</summary>
    public BoundingBox? BoundingBox => Finding.BoundingBox;
}
