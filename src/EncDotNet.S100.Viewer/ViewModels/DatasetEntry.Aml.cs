using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// What the viewer knows about a loaded NATO Additional Military Layers (AML)
/// cell (#1371): its AML facts, whether its portrayal draws it, and the
/// coverage its map outline traces.
/// </summary>
/// <param name="Info">The cell's AML facts.</param>
/// <param name="Drawn">Whether the AML portrayal draws the cell; false while it's "recognised, not drawn".</param>
/// <param name="FeatureCount">How many features the cell holds.</param>
/// <param name="Coverage">The cell's coverage: its <c>M_COVR</c> areas, or its bounding box.</param>
internal sealed record AmlCellState(
    AmlDatasetInfo Info,
    bool Drawn,
    int FeatureCount,
    IReadOnlyList<CoverageArea> Coverage);

// AML rows in the Datasets panel (#1371, decisions 8 and 9): three lines (the
// cell, "AML · {code}, {product name}" and the state), a dashed ink frame for
// the row icon while the cell isn't drawn, and the eye toggling the coverage
// outline.
internal sealed partial class DatasetEntry
{
    private AmlCellState? _aml;
    private bool _isFirstAmlInSet;

    /// <summary>
    /// Whether the entry is an AML cell: registered as one, or opened as one
    /// (an S-57 cell the exchange set didn't mark).
    /// </summary>
    public bool IsAml => _aml is not null
        || string.Equals(ProductSpec, AmlDatasetInfo.SpecName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the spec tag shows: AML rows say what they are on their second line instead.</summary>
    public bool ShowsSpecTag => !IsAml;

    /// <summary>The loaded cell's AML state, or <see langword="null"/> before it loads or when it isn't AML.</summary>
    public AmlCellState? Aml => _aml;

    /// <summary>
    /// Records the loaded processor's AML state (<see langword="null"/> for any
    /// other product) and raises the AML row properties.
    /// </summary>
    /// <param name="state">The cell's AML state.</param>
    internal void SetAml(AmlCellState? state)
    {
        if (Equals(_aml, state))
            return;
        _aml = state;
        RaiseAmlChanged();
    }

    /// <summary>
    /// Whether this is the first AML row of its exchange set, which adds
    /// "Its coverage is outlined on the map." to its state line. Set by
    /// <see cref="ExchangeSetHeader"/>.
    /// </summary>
    internal bool IsFirstAmlInSet
    {
        get => _isFirstAmlInSet;
        set
        {
            if (_isFirstAmlInSet == value)
                return;
            _isFirstAmlInSet = value;
            OnPropertyChanged(nameof(AmlStateText));
        }
    }

    /// <summary>The tag text, "AML · CLB"; "AML" until the cell loads.</summary>
    public string? AmlTagText => _aml?.Info.TagText
        ?? (IsAml ? AmlCellName.TryParse(DisplayName, out var name) && name.Product is { } code
            ? string.Format(CultureInfo.CurrentCulture, Strings.Aml_TagFormat, code)
            : Strings.Aml_Chip
            : null);

    /// <summary>
    /// The second line of an AML row: "AML · CLB, Contour Line Bathymetry"
    /// (the tag and the product name); the tag alone until the cell loads.
    /// </summary>
    public string? AmlProductLine => _aml is { } aml
        ? string.Format(CultureInfo.CurrentCulture, Strings.Aml_Row_ProductFormat, aml.Info.TagText, aml.Info.ProductName)
        : AmlTagText;

    /// <summary>The tag tooltip: "Additional Military Layers · {product name} · {version}".</summary>
    public string? AmlTagToolTip => _aml?.Info.TagToolTip ?? (IsAml ? Strings.Aml_Heading : null);

    /// <summary>The cell's protective marking, upper case as given, or <see langword="null"/>.</summary>
    public string? AmlMarking => _aml?.Info.ProtectiveMarking;

    /// <summary>Whether the row shows its marking chip: only above UNCLASSIFIED (decision 4).</summary>
    public bool ShowsAmlMarking => _aml?.Info.IsMarkedAboveUnclassified == true;

    /// <summary>Whether the AML cell is loaded and drawn by the AML portrayal.</summary>
    public bool IsAmlDrawn => _aml?.Drawn == true;

    /// <summary>Whether the row shows the dashed "not drawn" frame for its icon.</summary>
    public bool ShowsAmlNotDrawnIcon => IsAml && !IsAmlDrawn;

    /// <summary>Whether the row shows the layers glyph of a drawn AML cell.</summary>
    public bool ShowsAmlDrawnIcon => IsAml && IsAmlDrawn;

    /// <summary>
    /// The third line of an AML row: "Recognised, not drawn" (plus "Its
    /// coverage is outlined on the map." on a set's first AML row) or, once
    /// the portrayal draws the cell, "Drawn · {n} features".
    /// </summary>
    public string? AmlStateText
    {
        get
        {
            if (!IsAml)
                return null;
            if (_aml is null)
                return IsLoading ? Strings.Aml_Row_Opening : Strings.Aml_State_RecognisedNotDrawn;
            if (_aml.Drawn)
                return string.Format(CultureInfo.CurrentCulture, Strings.Aml_Row_DrawnFormat, _aml.FeatureCount);
            return _isFirstAmlInSet
                ? string.Format(CultureInfo.CurrentCulture, Strings.Aml_Row_NotDrawnOutlinedFormat, Strings.Aml_State_RecognisedNotDrawn)
                : Strings.Aml_State_RecognisedNotDrawn;
        }
    }

    /// <summary>
    /// Whether the map outlines the cell's coverage (decision 9): the row is
    /// visible and the cell isn't drawn, or it's drawn but every one of its
    /// sub-layers is hidden. The row's eye turns it off, and so does the
    /// display panel hiding the cell (<see cref="IsHiddenByDisplay"/>).
    /// </summary>
    public bool ShowsAmlCoverageOutline =>
        _aml is { } aml
        && IsVisible
        && !IsHiddenByDisplay
        && (!aml.Drawn || (SubLayers.Count > 0 && SubLayers.All(s => !s.IsVisible)));

    private void RaiseAmlChanged()
    {
        OnPropertyChanged(nameof(Aml));
        OnPropertyChanged(nameof(IsAml));
        OnPropertyChanged(nameof(ShowsSpecTag));
        OnPropertyChanged(nameof(AmlTagText));
        OnPropertyChanged(nameof(AmlProductLine));
        OnPropertyChanged(nameof(AmlTagToolTip));
        OnPropertyChanged(nameof(AmlMarking));
        OnPropertyChanged(nameof(ShowsAmlMarking));
        OnPropertyChanged(nameof(IsAmlDrawn));
        OnPropertyChanged(nameof(ShowsAmlNotDrawnIcon));
        OnPropertyChanged(nameof(ShowsAmlDrawnIcon));
        OnPropertyChanged(nameof(AmlStateText));
        OnPropertyChanged(nameof(ShowsAmlCoverageOutline));
    }
}
