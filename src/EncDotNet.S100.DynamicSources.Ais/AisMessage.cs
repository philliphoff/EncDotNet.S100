namespace EncDotNet.S100.DynamicSources.Ais;

/// <summary>
/// Base type for decoded AIS messages exposed by an
/// <see cref="IAisMessageSource"/>. Concrete subtypes
/// (<see cref="AisPositionReport"/>, <see cref="AisStaticVoyageData"/>)
/// carry the family-specific payload.
/// </summary>
/// <remarks>
/// The shape is intentionally "denormalised" — consumers do not see
/// the underlying AIS message-type number (1, 2, 3, 5, 18, 19, 24).
/// Drivers fold all position-bearing messages into
/// <see cref="AisPositionReport"/> and all static / voyage messages
/// into <see cref="AisStaticVoyageData"/>.
/// </remarks>
public abstract record AisMessage
{
    /// <summary>The reporting vessel's MMSI.</summary>
    public required uint Mmsi { get; init; }

    /// <summary>UTC timestamp at which the source received the message.</summary>
    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Decoded AIS position report (Class A messages 1/2/3, Class B
/// 18/19). Sentinel values from the wire (511 heading, 360 COG,
/// 102.3 SOG, -128 ROT) are collapsed to <see langword="null"/> at
/// the driver boundary so this record never carries them.
/// </summary>
public sealed record AisPositionReport : AisMessage
{
    /// <summary>WGS-84 latitude in degrees.</summary>
    public required double Latitude { get; init; }

    /// <summary>WGS-84 longitude in degrees.</summary>
    public required double Longitude { get; init; }

    /// <summary>Course over ground in degrees true (0–359.9), or
    /// <see langword="null"/> when the report carried the 360.0
    /// "not available" sentinel.</summary>
    public double? CourseOverGroundDeg { get; init; }

    /// <summary>True heading in degrees (0–359), or
    /// <see langword="null"/> when the report carried the 511
    /// "not available" sentinel.</summary>
    public double? HeadingDeg { get; init; }

    /// <summary>Speed over ground in knots, or
    /// <see langword="null"/> when the report carried the 102.3
    /// "not available" sentinel.</summary>
    public double? SpeedOverGroundKn { get; init; }

    /// <summary>Navigation status, or <see langword="null"/> when
    /// the message family does not carry one (Class B Type 18).</summary>
    public AisNavigationStatus? NavigationStatus { get; init; }

    /// <summary>Rate of turn in degrees per minute, or
    /// <see langword="null"/> when the report carried the -128 / 128
    /// "not available" sentinel or when the family does not carry
    /// ROT.</summary>
    public double? RateOfTurnDegPerMin { get; init; }
}

/// <summary>
/// Decoded AIS static and voyage-related data (Class A message 5,
/// Class B message 24 parts A and B combined when both have arrived).
/// </summary>
public sealed record AisStaticVoyageData : AisMessage
{
    /// <summary>IMO ship identification number, when reported.</summary>
    public uint? ImoNumber { get; init; }

    /// <summary>Call sign, when reported.</summary>
    public string? CallSign { get; init; }

    /// <summary>Vessel name, when reported.</summary>
    public string? VesselName { get; init; }

    /// <summary>Raw AIS shiptype code (Table 53). 0 = not available.</summary>
    public AisShipType ShipType { get; init; }

    /// <summary>Display-bucketed form of <see cref="ShipType"/>.</summary>
    public AisShipTypeClass ShipTypeClass { get; init; }

    /// <summary>Hull dimensions, when reported.</summary>
    public AisDimensions? Dimensions { get; init; }

    /// <summary>Maximum static draught in metres, when reported.</summary>
    public double? DraughtMetres { get; init; }

    /// <summary>Voyage destination, when reported.</summary>
    public string? Destination { get; init; }

    /// <summary>Voyage ETA, when reported.</summary>
    public DateTimeOffset? Eta { get; init; }
}

/// <summary>
/// Optional event raised by drivers that have an explicit
/// "vessel left coverage" signal. The aisstream.io driver does not
/// raise this — local-antenna and aggregator drivers may.
/// </summary>
public sealed record AisTargetLost
{
    /// <summary>The vanished vessel's MMSI.</summary>
    public required uint Mmsi { get; init; }

    /// <summary>UTC timestamp of the loss event.</summary>
    public required DateTimeOffset Timestamp { get; init; }
}
