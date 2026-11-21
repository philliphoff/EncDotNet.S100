namespace EncDotNet.S100.Tides;

/// <summary>
/// How a constituent's equilibrium argument, nodal correction and node
/// factor are formed from the astronomical elements (SP 98 Table 2 and
/// §§ 172–186 for the shallow-water constituents).
/// </summary>
internal sealed class ConstituentDefinition
{
    /// <summary>The NOAA name.</summary>
    public required string Name { get; init; }

    /// <summary>Other spellings of the name.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>
    /// The multiples of T, s, h, p, N and p₁ in V, the equilibrium argument.
    /// </summary>
    public required int[] Arguments { get; init; }

    /// <summary>
    /// The multiples of the elements' rates in the speed. The same as
    /// <see cref="Arguments"/> except for M₁, whose argument carries the
    /// perigee in its nodal correction (SP 98 § 124).
    /// </summary>
    public required int[] SpeedArguments { get; init; }

    /// <summary>The constant part of V, in degrees (a multiple of 90°).</summary>
    public double PhaseConstant { get; init; }

    /// <summary>The multiples of ξ, ν, ν′, 2ν″, R and Q in u, the nodal correction.</summary>
    public int[] Corrections { get; init; } = new int[6];

    /// <summary>The powers of the base node factors (<see cref="NodeFactor"/>) in f.</summary>
    public int[] FactorPowers { get; init; } = new int[NodalTerms.FactorCount];

    /// <summary>The speed in degrees per mean solar hour.</summary>
    public double Speed
    {
        get
        {
            var speed = 0.0;
            for (var i = 0; i < SpeedArguments.Length; i++)
            {
                speed += SpeedArguments[i] * Astronomy.Rates[i];
            }

            return speed;
        }
    }

    /// <summary>V, the equilibrium argument, in degrees, from the elements.</summary>
    public double EquilibriumArgument(ReadOnlySpan<double> elements)
    {
        var v = PhaseConstant;
        for (var i = 0; i < Arguments.Length; i++)
        {
            v += Arguments[i] * elements[i];
        }

        return v;
    }

    /// <summary>u, the nodal correction, in degrees.</summary>
    public double NodalCorrection(NodalTerms terms)
    {
        var u = (Corrections[0] * terms.Xi)
            + (Corrections[1] * terms.Nu)
            + (Corrections[2] * terms.NuPrime)
            + (Corrections[3] * terms.TwoNuSecond)
            + (Corrections[4] * terms.R)
            + (Corrections[5] * terms.Q);
        return u / Angles.DegreesToRadians;
    }

    /// <summary>f, the node factor.</summary>
    public double NodeFactorValue(NodalTerms terms)
    {
        var f = 1.0;
        for (var i = 0; i < FactorPowers.Length; i++)
        {
            for (var k = 0; k < FactorPowers[i]; k++)
            {
                f *= terms.F[i];
            }
        }

        return f;
    }
}
