namespace EncDotNet.S100.DynamicSources.Ais;

/// <summary>
/// Raw AIS shiptype code per ITU-R M.1371-5 Table 53. Carried
/// verbatim from the upstream message for callers that need the
/// original code; <see cref="AisShipTypeClass"/> exposes the
/// display-bucketed form used by renderers.
/// </summary>
/// <remarks>
/// Codes are nominally 0–99. The enum lists only the buckets used
/// by the Table 53 mapping; any unmapped numeric value is preserved
/// by casting an arbitrary <see cref="int"/> to the enum (the type
/// is intentionally non-strict).
/// </remarks>
public enum AisShipType
{
    /// <summary>Not available (default).</summary>
    NotAvailable = 0,

    /// <summary>Reserved (1–19) — should not be used.</summary>
    ReservedFutureUse = 1,

    /// <summary>Wing in ground (WIG) — generic.</summary>
    WingInGround = 20,

    /// <summary>Fishing.</summary>
    Fishing = 30,

    /// <summary>Towing.</summary>
    Towing = 31,

    /// <summary>Towing — length exceeds 200 m or breadth exceeds 25 m.</summary>
    TowingLargeOrWide = 32,

    /// <summary>Dredging or underwater operations.</summary>
    DredgingOrUnderwaterOps = 33,

    /// <summary>Diving operations.</summary>
    DivingOps = 34,

    /// <summary>Military operations.</summary>
    MilitaryOps = 35,

    /// <summary>Sailing.</summary>
    Sailing = 36,

    /// <summary>Pleasure craft.</summary>
    PleasureCraft = 37,

    /// <summary>High speed craft (HSC) — generic.</summary>
    HighSpeedCraft = 40,

    /// <summary>Pilot vessel.</summary>
    PilotVessel = 50,

    /// <summary>Search and rescue vessel.</summary>
    SearchAndRescue = 51,

    /// <summary>Tug.</summary>
    Tug = 52,

    /// <summary>Port tender.</summary>
    PortTender = 53,

    /// <summary>Anti-pollution equipment.</summary>
    AntiPollution = 54,

    /// <summary>Law enforcement.</summary>
    LawEnforcement = 55,

    /// <summary>Spare — local vessel (56, 57).</summary>
    SpareLocalVessel = 56,

    /// <summary>Medical transport.</summary>
    MedicalTransport = 58,

    /// <summary>Non-combatant ship per RR Resolution No. 18.</summary>
    NonCombatant = 59,

    /// <summary>Passenger — generic.</summary>
    Passenger = 60,

    /// <summary>Cargo — generic.</summary>
    Cargo = 70,

    /// <summary>Tanker — generic.</summary>
    Tanker = 80,

    /// <summary>Other type — generic.</summary>
    Other = 90,
}
