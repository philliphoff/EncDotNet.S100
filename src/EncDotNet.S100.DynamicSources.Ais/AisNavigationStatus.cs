namespace EncDotNet.S100.DynamicSources.Ais;

/// <summary>
/// AIS navigation-status codes per ITU-R M.1371-5 §3.3.7.2.1.
/// </summary>
public enum AisNavigationStatus
{
    /// <summary>0 — under way using engine.</summary>
    UnderWayUsingEngine = 0,

    /// <summary>1 — at anchor.</summary>
    AtAnchor = 1,

    /// <summary>2 — not under command.</summary>
    NotUnderCommand = 2,

    /// <summary>3 — restricted manoeuvrability.</summary>
    RestrictedManoeuvrability = 3,

    /// <summary>4 — constrained by her draught.</summary>
    ConstrainedByDraught = 4,

    /// <summary>5 — moored.</summary>
    Moored = 5,

    /// <summary>6 — aground.</summary>
    Aground = 6,

    /// <summary>7 — engaged in fishing.</summary>
    EngagedInFishing = 7,

    /// <summary>8 — under way sailing.</summary>
    UnderWaySailing = 8,

    /// <summary>9 — reserved for future amendment of navigational status for ships carrying DG, HS, MP, or IMO HSC.</summary>
    Reserved9 = 9,

    /// <summary>10 — reserved for future amendment of navigational status for ships carrying DG, HS, MP.</summary>
    Reserved10 = 10,

    /// <summary>11 — power-driven vessel towing astern (regional use).</summary>
    PowerDrivenTowingAstern = 11,

    /// <summary>12 — power-driven vessel pushing ahead or towing alongside (regional use).</summary>
    PowerDrivenPushingAhead = 12,

    /// <summary>13 — reserved for future use.</summary>
    Reserved13 = 13,

    /// <summary>14 — AIS-SART (active), MOB-AIS, EPIRB-AIS.</summary>
    AisSart = 14,

    /// <summary>15 — undefined / default.</summary>
    NotDefined = 15,
}
