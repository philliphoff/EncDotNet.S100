using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Tools;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The measure tool's shared state (#1130): the path, its geometry, the leg
/// the card shows and the distance unit. The map tool, the measure card and
/// the <c>set_measure</c> / <c>get_measure</c> MCP tools all work on it, and
/// whoever changes the path calls <see cref="NotifyChanged"/> so the others
/// follow.
/// </summary>
internal sealed class MeasureSession
{
    private readonly ViewerSettings? _settings;
    private DistanceUnit _unit = DistanceUnit.NauticalMiles;

    /// <param name="settings">Where the geometry persists (D3); <c>null</c>
    /// keeps it for the session only.</param>
    public MeasureSession(ViewerSettings? settings = null)
    {
        _settings = settings;
        State.SetGeometry(ParseGeometry(settings?.MeasureGeometry));
        if (settings is not null && Enum.TryParse<DistanceUnit>(settings.DistanceUnit, ignoreCase: true, out var unit))
            _unit = unit;
    }

    /// <summary>Raised after the path, its geometry, the selected leg or the unit changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when something outside the map (MCP) asks for the measure tool to be on.</summary>
    public event EventHandler? ActivationRequested;

    /// <summary>
    /// Raised when a typed position is committed, so the map can pan to it
    /// when it's off screen (D7).
    /// </summary>
    public event EventHandler<GeoPosition>? PositionCommitted;

    /// <summary>The path being measured.</summary>
    public MeasurePathState State { get; } = new();

    /// <summary>The geometry every leg is measured along.</summary>
    public LegGeometry Geometry => State.Geometry;

    /// <summary>True while the measure tool is on.</summary>
    public bool IsActive { get; set; }

    /// <summary>The primary distance unit: the Distance unit setting (D6).</summary>
    public DistanceUnit Unit
    {
        get => _unit;
        set
        {
            if (_unit == value)
                return;
            _unit = value;
            NotifyChanged();
        }
    }

    /// <summary>
    /// The earth model the card's footer names (D17):
    /// <c>WGS-84 ellipsoid</c>.
    /// </summary>
    public static string EarthModelText => Strings.Measure_Model;

    /// <summary>Sets the geometry and remembers it (D3).</summary>
    public void SetGeometry(LegGeometry geometry)
    {
        if (!State.SetGeometry(geometry))
            return;
        if (_settings is not null)
        {
            _settings.MeasureGeometry = geometry.ToString();
            _settings.Save();
        }

        NotifyChanged();
    }

    /// <summary>Switches between rhumb line and great circle (the <c>G</c> key).</summary>
    public void ToggleGeometry() =>
        SetGeometry(Geometry == LegGeometry.GreatCircle ? LegGeometry.Rhumb : LegGeometry.GreatCircle);

    /// <summary>Raises <see cref="Changed"/>.</summary>
    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Asks for the measure tool to be turned on.</summary>
    public void RequestActivation() => ActivationRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Reports a committed typed position.</summary>
    public void OnPositionCommitted(GeoPosition position) => PositionCommitted?.Invoke(this, position);

    /// <summary>Reads a stored geometry name; anything but GreatCircle is Rhumb.</summary>
    internal static LegGeometry ParseGeometry(string? text) =>
        Enum.TryParse<LegGeometry>(text, ignoreCase: true, out var geometry) ? geometry : LegGeometry.Rhumb;
}
