namespace EncDotNet.S100.Tides;

/// <summary>The base node factors, combined into each constituent's f.</summary>
internal enum NodeFactor
{
    M2,
    O1,
    K1,
    K2,
    J1,
    OO1,
    Mm,
    Mf,
    M3,

    /// <summary>1/Rₐ, the L₂ amplitude term (SP 98 § 214).</summary>
    InverseRa,

    /// <summary>1/Qₐ, the M₁ amplitude term (SP 98 § 197).</summary>
    InverseQa,
}

/// <summary>
/// The nodal terms at a time, in radians, and the base node factors
/// (SP 98 §§ 136–235).
/// </summary>
internal sealed class NodalTerms
{
    public const int FactorCount = 11;

    /// <summary>ξ, the longitude in the moon's orbit of the lunar intersection.</summary>
    public double Xi { get; init; }

    /// <summary>ν, the right ascension of the lunar intersection.</summary>
    public double Nu { get; init; }

    /// <summary>ν′, the K₁ term.</summary>
    public double NuPrime { get; init; }

    /// <summary>2ν″, the K₂ term.</summary>
    public double TwoNuSecond { get; init; }

    /// <summary>R, the L₂ term.</summary>
    public double R { get; init; }

    /// <summary>Q, the M₁ term.</summary>
    public double Q { get; init; }

    /// <summary>The base node factors, indexed by <see cref="NodeFactor"/>.</summary>
    public double[] F { get; } = new double[FactorCount];
}
