namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// Mutable route-level metadata, mirroring the S-421 <c>RouteInfo</c>
/// information type
/// (<see cref="EncDotNet.S100.Datasets.S421.DataModel.S421RouteInfo"/>).
/// Every <see cref="Route"/> owns exactly one instance; all fields are
/// optional.
/// </summary>
public sealed class RouteInfo
{
    /// <summary>Route name shown to the user (S-421 <c>routeInfoName</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Route author / originator (S-421 <c>routeInfoAuthor</c>).</summary>
    public string? Author { get; set; }

    /// <summary>Free-text description (S-421 <c>routeInfoDescription</c>).</summary>
    public string? Description { get; set; }

    /// <summary>Departure port identifier (S-421 <c>routeInfoDeparturePortID1</c>).</summary>
    public string? DeparturePortId { get; set; }

    /// <summary>Arrival port identifier (S-421 <c>routeInfoArrivalPortID1</c>).</summary>
    public string? ArrivalPortId { get; set; }

    /// <summary>Planned start of validity, UTC (S-421 <c>routeInfoValidityStart</c>).</summary>
    public DateTimeOffset? ValidityStart { get; set; }

    /// <summary>Planned end of validity, UTC (S-421 <c>routeInfoValidityEnd</c>).</summary>
    public DateTimeOffset? ValidityEnd { get; set; }

    /// <summary>Vessel the route is planned for; absent until populated.</summary>
    public RouteVesselInfo? Vessel { get; set; }
}

/// <summary>
/// Mutable vessel metadata carried by a <see cref="RouteInfo"/>, mirroring
/// the S-421 <c>routeInfoVessel*</c> attributes
/// (<see cref="EncDotNet.S100.Datasets.S421.DataModel.S421VesselInfo"/>).
/// </summary>
public sealed class RouteVesselInfo
{
    /// <summary>Vessel name (S-421 <c>routeInfoVesselName</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Maritime Mobile Service Identity (S-421 <c>routeInfoVesselMMSI</c>).</summary>
    public string? Mmsi { get; set; }

    /// <summary>IMO number (S-421 <c>routeInfoVesselIMO</c>).</summary>
    public string? Imo { get; set; }

    /// <summary>Call sign (S-421 <c>routeInfoVesselCallsign</c>).</summary>
    public string? Callsign { get; set; }

    /// <summary>Overall length, in metres (S-421 <c>routeInfoVesselLength</c>).</summary>
    public double? LengthMeters { get; set; }

    /// <summary>Beam, in metres (S-421 <c>routeInfoVesselBeam</c>).</summary>
    public double? BeamMeters { get; set; }
}
