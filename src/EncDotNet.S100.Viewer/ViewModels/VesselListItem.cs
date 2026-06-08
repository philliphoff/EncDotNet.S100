using EncDotNet.S100.DynamicSources.Ais;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Display projection of a single AIS target in the Vessels panel.
/// Mutable (an <see cref="ViewModelBase"/>/<c>ObservableObject</c>) so a
/// 1 Hz refresh can update range/bearing/state in place without
/// replacing the row — which preserves both the user's selection and a
/// smooth, flicker-free list.
/// </summary>
/// <remarks>
/// The panel uses a master/detail layout: the compact list row binds to
/// <see cref="Name"/>, <see cref="ShipTypeClass"/>, <see cref="StateText"/>
/// and <see cref="RangeBearingText"/>, while the properties sub-pane binds
/// to the richer identity/motion/voyage/dimension fields below. Every
/// detail field has a matching <c>Has*</c> flag so the view can hide rows
/// for which no data has been received yet.
/// </remarks>
internal sealed class VesselListItem : ViewModelBase
{
    /// <summary>
    /// Stable source feature id (e.g. <c>"ais:&lt;mmsi&gt;"</c>). Used as
    /// the dictionary key for in-place updates across refreshes.
    /// </summary>
    public required string Id { get; init; }

    private string _name = string.Empty;
    /// <summary>Display name (vessel name, else MMSI, else a placeholder).</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private AisShipTypeClass _shipTypeClass = AisShipTypeClass.Unknown;
    /// <summary>Ship-type bucket driving the row pictogram tint.</summary>
    public AisShipTypeClass ShipTypeClass
    {
        get => _shipTypeClass;
        set => SetProperty(ref _shipTypeClass, value);
    }

    private string _shipTypeText = string.Empty;
    /// <summary>Localised ship-type label (e.g. "Cargo").</summary>
    public string ShipTypeText
    {
        get => _shipTypeText;
        set => SetProperty(ref _shipTypeText, value);
    }

    private string _headerSubtitle = string.Empty;
    /// <summary>
    /// Combined "type · status" line shown under the vessel name in the
    /// detail header.
    /// </summary>
    public string HeaderSubtitle
    {
        get => _headerSubtitle;
        set => SetProperty(ref _headerSubtitle, value);
    }

    private string _stateText = string.Empty;
    /// <summary>Localised navigation/state label (e.g. "At anchor").</summary>
    public string StateText
    {
        get => _stateText;
        set => SetProperty(ref _stateText, value);
    }

    // --- Identity -------------------------------------------------------

    private string _mmsiText = string.Empty;
    /// <summary>Formatted MMSI (always known for a received target).</summary>
    public string MmsiText
    {
        get => _mmsiText;
        set => SetProperty(ref _mmsiText, value);
    }

    private string? _callSign;
    /// <summary>Radio call sign, or <see langword="null"/> when not received.</summary>
    public string? CallSign
    {
        get => _callSign;
        set => SetProperty(ref _callSign, value);
    }

    private bool _hasCallSign;
    /// <summary>Whether <see cref="CallSign"/> should be shown.</summary>
    public bool HasCallSign
    {
        get => _hasCallSign;
        set => SetProperty(ref _hasCallSign, value);
    }

    private string? _imoText;
    /// <summary>IMO number text, or <see langword="null"/> when not received.</summary>
    public string? ImoText
    {
        get => _imoText;
        set => SetProperty(ref _imoText, value);
    }

    private bool _hasImo;
    /// <summary>Whether <see cref="ImoText"/> should be shown.</summary>
    public bool HasImo
    {
        get => _hasImo;
        set => SetProperty(ref _hasImo, value);
    }

    // --- Motion ---------------------------------------------------------

    private bool _hasMotion;
    /// <summary>
    /// Whether any motion field (heading, course, or speed) is available.
    /// Gates the "Motion" detail section as a whole.
    /// </summary>
    public bool HasMotion
    {
        get => _hasMotion;
        set => SetProperty(ref _hasMotion, value);
    }

    private string? _headingText;
    /// <summary>Formatted true heading, or <see langword="null"/>.</summary>
    public string? HeadingText
    {
        get => _headingText;
        set => SetProperty(ref _headingText, value);
    }

    private bool _hasHeading;
    /// <summary>Whether <see cref="HeadingText"/> should be shown.</summary>
    public bool HasHeading
    {
        get => _hasHeading;
        set => SetProperty(ref _hasHeading, value);
    }

    private string? _courseText;
    /// <summary>Formatted course over ground, or <see langword="null"/>.</summary>
    public string? CourseText
    {
        get => _courseText;
        set => SetProperty(ref _courseText, value);
    }

    private bool _hasCourse;
    /// <summary>Whether <see cref="CourseText"/> should be shown.</summary>
    public bool HasCourse
    {
        get => _hasCourse;
        set => SetProperty(ref _hasCourse, value);
    }

    private string? _speedText;
    /// <summary>Formatted speed over ground, or <see langword="null"/>.</summary>
    public string? SpeedText
    {
        get => _speedText;
        set => SetProperty(ref _speedText, value);
    }

    private bool _hasSpeed;
    /// <summary>Whether <see cref="SpeedText"/> should be shown.</summary>
    public bool HasSpeed
    {
        get => _hasSpeed;
        set => SetProperty(ref _hasSpeed, value);
    }

    private string? _rateOfTurnText;
    /// <summary>Formatted rate of turn, or <see langword="null"/>.</summary>
    public string? RateOfTurnText
    {
        get => _rateOfTurnText;
        set => SetProperty(ref _rateOfTurnText, value);
    }

    private bool _hasRateOfTurn;
    /// <summary>Whether <see cref="RateOfTurnText"/> should be shown.</summary>
    public bool HasRateOfTurn
    {
        get => _hasRateOfTurn;
        set => SetProperty(ref _hasRateOfTurn, value);
    }

    // --- Range / bearing (own ship) ------------------------------------

    private double? _distanceMetres;
    /// <summary>
    /// Range from own ship in metres, or <see langword="null"/> when the
    /// own-ship position is unknown. Primary sort key (nearest first).
    /// </summary>
    public double? DistanceMetres
    {
        get => _distanceMetres;
        set => SetProperty(ref _distanceMetres, value);
    }

    private string _distanceText = string.Empty;
    /// <summary>Formatted range (e.g. "12.3 NM") or empty when unknown.</summary>
    public string DistanceText
    {
        get => _distanceText;
        set => SetProperty(ref _distanceText, value);
    }

    private string _bearingText = string.Empty;
    /// <summary>Formatted bearing true (e.g. "045°") or empty when unknown.</summary>
    public string BearingText
    {
        get => _bearingText;
        set => SetProperty(ref _bearingText, value);
    }

    private string _rangeBearingText = string.Empty;
    /// <summary>Combined "distance · bearing" line for the compact row.</summary>
    public string RangeBearingText
    {
        get => _rangeBearingText;
        set => SetProperty(ref _rangeBearingText, value);
    }

    private bool _hasRangeBearing;
    /// <summary>
    /// Whether range and bearing should be shown. They are relative to the
    /// own ship, so they are only meaningful — and only displayed — when
    /// the own-ship overlay is enabled and publishing a position.
    /// </summary>
    public bool HasRangeBearing
    {
        get => _hasRangeBearing;
        set => SetProperty(ref _hasRangeBearing, value);
    }

    // --- Voyage ---------------------------------------------------------

    private bool _hasVoyage;
    /// <summary>
    /// Whether any voyage field (destination, ETA, or draught) is
    /// available. Gates the "Voyage" detail section as a whole.
    /// </summary>
    public bool HasVoyage
    {
        get => _hasVoyage;
        set => SetProperty(ref _hasVoyage, value);
    }

    private string? _destinationText;
    /// <summary>Reported voyage destination, or <see langword="null"/>.</summary>
    public string? DestinationText
    {
        get => _destinationText;
        set => SetProperty(ref _destinationText, value);
    }

    private bool _hasDestination;
    /// <summary>Whether <see cref="DestinationText"/> should be shown.</summary>
    public bool HasDestination
    {
        get => _hasDestination;
        set => SetProperty(ref _hasDestination, value);
    }

    private string? _etaText;
    /// <summary>Formatted estimated time of arrival, or <see langword="null"/>.</summary>
    public string? EtaText
    {
        get => _etaText;
        set => SetProperty(ref _etaText, value);
    }

    private bool _hasEta;
    /// <summary>Whether <see cref="EtaText"/> should be shown.</summary>
    public bool HasEta
    {
        get => _hasEta;
        set => SetProperty(ref _hasEta, value);
    }

    private string? _draughtText;
    /// <summary>Formatted static draught, or <see langword="null"/>.</summary>
    public string? DraughtText
    {
        get => _draughtText;
        set => SetProperty(ref _draughtText, value);
    }

    private bool _hasDraught;
    /// <summary>Whether <see cref="DraughtText"/> should be shown.</summary>
    public bool HasDraught
    {
        get => _hasDraught;
        set => SetProperty(ref _hasDraught, value);
    }

    // --- Dimensions -----------------------------------------------------

    private bool _hasDimensionsSection;
    /// <summary>
    /// Whether any dimension-related field (hull size or draught) is
    /// available. Gates the "Dimensions" detail section as a whole.
    /// </summary>
    public bool HasDimensionsSection
    {
        get => _hasDimensionsSection;
        set => SetProperty(ref _hasDimensionsSection, value);
    }

    private string? _dimensionsText;
    /// <summary>Formatted length × beam, or <see langword="null"/>.</summary>
    public string? DimensionsText
    {
        get => _dimensionsText;
        set => SetProperty(ref _dimensionsText, value);
    }

    private bool _hasDimensions;
    /// <summary>Whether <see cref="DimensionsText"/> should be shown.</summary>
    public bool HasDimensions
    {
        get => _hasDimensions;
        set => SetProperty(ref _hasDimensions, value);
    }

    // --- Position -------------------------------------------------------

    private double _latitude;
    /// <summary>Latest reported latitude (WGS-84) — the centring target.</summary>
    public double Latitude
    {
        get => _latitude;
        set => SetProperty(ref _latitude, value);
    }

    private double _longitude;
    /// <summary>Latest reported longitude (WGS-84) — the centring target.</summary>
    public double Longitude
    {
        get => _longitude;
        set => SetProperty(ref _longitude, value);
    }
}
