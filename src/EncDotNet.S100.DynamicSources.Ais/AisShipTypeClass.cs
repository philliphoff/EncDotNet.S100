namespace EncDotNet.S100.DynamicSources.Ais;

/// <summary>
/// Display-bucketed form of the AIS shiptype code, used by renderers
/// for palette dispatch and by the dynamic-source layer to compose
/// <see cref="DynamicFeature.Kind"/> as <c>"vessel.ais.{class}"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Mapping table (per ITU-R M.1371-5 Table 53):
/// </para>
/// <list type="bullet">
///   <item><description>0 → <see cref="Unknown"/></description></item>
///   <item><description>1–19, 56–57 → <see cref="Other"/></description></item>
///   <item><description>20–29 → <see cref="HighSpeedCraft"/> (WIG behaves like HSC)</description></item>
///   <item><description>30 → <see cref="Fishing"/></description></item>
///   <item><description>31, 32 → <see cref="Tug"/></description></item>
///   <item><description>33–34 → <see cref="Other"/></description></item>
///   <item><description>35 → <see cref="Military"/></description></item>
///   <item><description>36 → <see cref="Sailing"/></description></item>
///   <item><description>37 → <see cref="Pleasure"/></description></item>
///   <item><description>40–49 → <see cref="HighSpeedCraft"/></description></item>
///   <item><description>50 → <see cref="PilotVessel"/></description></item>
///   <item><description>51 → <see cref="SearchAndRescue"/></description></item>
///   <item><description>52, 53 → <see cref="Tug"/></description></item>
///   <item><description>55 → <see cref="LawEnforcement"/></description></item>
///   <item><description>54, 58, 59 → <see cref="Other"/></description></item>
///   <item><description>60–69 → <see cref="Passenger"/></description></item>
///   <item><description>70–79 → <see cref="Cargo"/></description></item>
///   <item><description>80–89 → <see cref="Tanker"/></description></item>
///   <item><description>90–99, anything else → <see cref="Other"/></description></item>
/// </list>
/// </remarks>
public enum AisShipTypeClass
{
    /// <summary>No shiptype data received yet (no Type-5 / Type-24 part-A).</summary>
    Unknown = 0,

    /// <summary>Cargo vessels (codes 70-79).</summary>
    Cargo,

    /// <summary>Tankers (codes 80-89).</summary>
    Tanker,

    /// <summary>Passenger vessels (codes 60-69).</summary>
    Passenger,

    /// <summary>High speed craft and WIG (codes 20-29, 40-49).</summary>
    HighSpeedCraft,

    /// <summary>Pleasure craft (code 37).</summary>
    Pleasure,

    /// <summary>Fishing (code 30).</summary>
    Fishing,

    /// <summary>Tugs and towing vessels (codes 31, 32, 52, 53).</summary>
    Tug,

    /// <summary>Search and rescue (code 51).</summary>
    SearchAndRescue,

    /// <summary>Law enforcement (code 55).</summary>
    LawEnforcement,

    /// <summary>Military operations (code 35).</summary>
    Military,

    /// <summary>Sailing vessels (code 36).</summary>
    Sailing,

    /// <summary>Pilot vessels (code 50).</summary>
    PilotVessel,

    /// <summary>Any other / unmapped code.</summary>
    Other,
}

/// <summary>
/// Maps raw AIS shiptype codes to display buckets.
/// </summary>
public static class AisShipTypeClassExtensions
{
    /// <summary>
    /// Bucket the supplied raw AIS shiptype code per ITU-R M.1371-5
    /// Table 53. Unknown / out-of-range codes map to
    /// <see cref="AisShipTypeClass.Other"/>.
    /// </summary>
    public static AisShipTypeClass ToClass(this AisShipType type) => ToClass((int)type);

    /// <summary>
    /// Bucket a numeric AIS shiptype code per ITU-R M.1371-5
    /// Table 53.
    /// </summary>
    public static AisShipTypeClass ToClass(int code) => code switch
    {
        0 => AisShipTypeClass.Unknown,
        >= 1 and <= 19 => AisShipTypeClass.Other,
        >= 20 and <= 29 => AisShipTypeClass.HighSpeedCraft,
        30 => AisShipTypeClass.Fishing,
        31 or 32 => AisShipTypeClass.Tug,
        33 or 34 => AisShipTypeClass.Other,
        35 => AisShipTypeClass.Military,
        36 => AisShipTypeClass.Sailing,
        37 => AisShipTypeClass.Pleasure,
        38 or 39 => AisShipTypeClass.Other,
        >= 40 and <= 49 => AisShipTypeClass.HighSpeedCraft,
        50 => AisShipTypeClass.PilotVessel,
        51 => AisShipTypeClass.SearchAndRescue,
        52 or 53 => AisShipTypeClass.Tug,
        54 => AisShipTypeClass.Other,
        55 => AisShipTypeClass.LawEnforcement,
        56 or 57 => AisShipTypeClass.Other,
        58 or 59 => AisShipTypeClass.Other,
        >= 60 and <= 69 => AisShipTypeClass.Passenger,
        >= 70 and <= 79 => AisShipTypeClass.Cargo,
        >= 80 and <= 89 => AisShipTypeClass.Tanker,
        _ => AisShipTypeClass.Other,
    };

    /// <summary>
    /// Lower-case, hyphen-free token used as the suffix in
    /// <see cref="DynamicFeature.Kind"/> (e.g. <c>"cargo"</c>,
    /// <c>"highspeedcraft"</c>).
    /// </summary>
    public static string ToKindToken(this AisShipTypeClass cls) => cls switch
    {
        AisShipTypeClass.Cargo => "cargo",
        AisShipTypeClass.Tanker => "tanker",
        AisShipTypeClass.Passenger => "passenger",
        AisShipTypeClass.HighSpeedCraft => "highspeedcraft",
        AisShipTypeClass.Pleasure => "pleasure",
        AisShipTypeClass.Fishing => "fishing",
        AisShipTypeClass.Tug => "tug",
        AisShipTypeClass.SearchAndRescue => "sar",
        AisShipTypeClass.LawEnforcement => "lawenforcement",
        AisShipTypeClass.Military => "military",
        AisShipTypeClass.Sailing => "sailing",
        AisShipTypeClass.PilotVessel => "pilot",
        AisShipTypeClass.Other => "other",
        _ => "unknown",
    };
}
