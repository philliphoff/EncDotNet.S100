namespace EncDotNet.S100.Pipelines;

/// <summary>
/// Display unit used by the viewer when presenting depth values to the
/// mariner. Canonical depth values inside the data model are always stored
/// in metres; this enum only affects formatting/parsing of user-facing
/// strings (S-100 Part 9 §4.2 mariner selections).
/// </summary>
public enum DepthUnit
{
    /// <summary>Metres (canonical unit, e.g. "30.5 m").</summary>
    Metres,

    /// <summary>Feet (e.g. "100 ft"). 1 m = 3.28084 ft.</summary>
    Feet,

    /// <summary>Combined fathoms and feet (e.g. "5fm 2ft"). 1 fathom = 6 ft.</summary>
    FathomsFeet,

    /// <summary>Fathoms (e.g. "5.0 fm"). 1 m = 0.546807 fathoms.</summary>
    Fathoms,
}
