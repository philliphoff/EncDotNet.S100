namespace EncDotNet.S100.Tides;

/// <summary>
/// The constituents NOAA CO-OPS publishes: the 37 standard ones, in NOAA's
/// order, and the 83 more it lists at some stations. Their arguments come
/// from SP 98 Table 2 (the elementary constituents) and §§ 172–186 (the
/// shallow-water constituents, as sums of the elementary ones).
/// </summary>
internal static class ConstituentTable
{
    /// <summary>The elementary constituents and the 37 standard ones, which the compounds are built from.</summary>
    private static readonly ConstituentDefinition[] Elementary =
    [
        Lunar2("M2", [2, -2, 2, 0, 0, 0]),
        Solar("S2", [2, 0, 0, 0, 0, 0]),
        Lunar2("N2", [2, -3, 2, 1, 0, 0]),
        Define("K1", [1, 0, 1, 0, 0, 0], -90, U(nuPrime: -1), F((NodeFactor.K1, 1))),
        Define("M4", [4, -4, 4, 0, 0, 0], 0, U(xi: 4, nu: -4), F((NodeFactor.M2, 2))),
        Lunar1("O1", [1, -2, 1, 0, 0, 0]),
        Define("M6", [6, -6, 6, 0, 0, 0], 0, U(xi: 6, nu: -6), F((NodeFactor.M2, 3))),
        Define("MK3", [3, -2, 3, 0, 0, 0], -90, U(xi: 2, nu: -2, nuPrime: -1), F((NodeFactor.M2, 1), (NodeFactor.K1, 1))),
        Solar("S4", [4, 0, 0, 0, 0, 0]),
        Define("MN4", [4, -5, 4, 1, 0, 0], 0, U(xi: 4, nu: -4), F((NodeFactor.M2, 2))),
        Lunar2("NU2", [2, -3, 4, -1, 0, 0]),
        Solar("S6", [6, 0, 0, 0, 0, 0]),
        Lunar2("MU2", [2, -4, 4, 0, 0, 0]),
        Lunar2("2N2", [2, -4, 2, 2, 0, 0]),
        Define("OO1", [1, 2, 1, 0, 0, 0], -90, U(xi: -2, nu: -1), F((NodeFactor.OO1, 1))),
        Lunar2("LAM2", [2, -1, 0, 1, 0, 0], 180, "LAMBDA2", "LDA2"),
        Solar("S1", [1, 0, 0, 0, 0, 0]),
        // M₁ (SP 98 §§ 121–126): the argument of formula (201), with p carried
        // in Q, and the speed of formula (194), which includes p.
        Define("M1", [1, -1, 1, 0, 0, 0], -90, U(xi: 1, nu: -1, q: 1), F((NodeFactor.O1, 1), (NodeFactor.InverseQa, 1)), speedArguments: [1, -1, 1, 1, 0, 0]),
        Define("J1", [1, 1, 1, -1, 0, 0], -90, U(nu: -1), F((NodeFactor.J1, 1))),
        Define("MM", [0, 1, 0, -1, 0, 0], 0, U(), F((NodeFactor.Mm, 1))),
        Solar("SSA", [0, 0, 2, 0, 0, 0]),
        Solar("SA", [0, 0, 1, 0, 0, 0]),
        // MSf as S₂ − M₂: u = 2ν − 2ξ, as NOAA's predictions have it.
        Define("MSF", [0, 2, -2, 0, 0, 0], 0, U(xi: -2, nu: 2), F((NodeFactor.M2, 1))),
        Define("MF", [0, 2, 0, 0, 0, 0], 0, U(xi: -2), F((NodeFactor.Mf, 1))),
        Lunar1("RHO", [1, -3, 3, -1, 0, 0], "RHO1"),
        Lunar1("Q1", [1, -3, 1, 1, 0, 0]),
        Solar("T2", [2, 0, -1, 0, 0, 1]),
        Solar("R2", [2, 0, 1, 0, 0, -1], 180),
        Lunar1("2Q1", [1, -4, 1, 2, 0, 0]),
        Solar("P1", [1, 0, -1, 0, 0, 0], 90),
        Define("2SM2", [2, 2, -2, 0, 0, 0], 0, U(xi: -2, nu: 2), F((NodeFactor.M2, 1))),
        Define("M3", [3, -3, 3, 0, 0, 0], 0, U(xi: 3, nu: -3), F((NodeFactor.M3, 1))),
        Define("L2", [2, -1, 2, -1, 0, 0], 180, U(xi: 2, nu: -2, r: -1), F((NodeFactor.M2, 1), (NodeFactor.InverseRa, 1))),
        Define("2MK3", [3, -4, 3, 0, 0, 0], 90, U(xi: 4, nu: -4, nuPrime: 1), F((NodeFactor.M2, 2), (NodeFactor.K1, 1))),
        Define("K2", [2, 0, 2, 0, 0, 0], 0, U(twoNuSecond: -1), F((NodeFactor.K2, 1))),
        Define("M8", [8, -8, 8, 0, 0, 0], 0, U(xi: 8, nu: -8), F((NodeFactor.M2, 4))),
        Define("MS4", [4, -2, 2, 0, 0, 0], 0, U(xi: 2, nu: -2), F((NodeFactor.M2, 1))),

        // Three elementary diurnal constituents NOAA publishes at some
        // stations (SP 98 Table 2). NOAA's predictions give σ₁ the nodal
        // terms of 2O₁ − P₁, which has the same V; χ₁ and θ₁ take J₁'s.
        Define("SIGMA1", [1, -4, 3, 0, 0, 0], 90, U(xi: 4, nu: -2), F((NodeFactor.O1, 2))),
        Define("CHI1", [1, -1, 3, -1, 0, 0], -90, U(nu: -1), F((NodeFactor.J1, 1))),
        Define("THETA1", [1, 1, -1, 1, 0, 0], -90, U(nu: -1), F((NodeFactor.J1, 1))),
    ];

    private static readonly Dictionary<string, ConstituentDefinition> BaseConstituents =
        Elementary.ToDictionary(d => d.Name, StringComparer.Ordinal);

    /// <summary>
    /// The definitions: NOAA's 37 standard constituents in NOAA's order, then
    /// the further ones NOAA publishes at some stations.
    /// </summary>
    public static readonly ConstituentDefinition[] All =
    [
        .. Elementary,

        // The shallow-water and compound constituents NOAA publishes at some
        // stations, in NOAA's order. Each is the sum of multiples of the
        // constituents above: V and u add, f multiplies (SP 98 §§ 172–186).
        // The combinations follow the names and agree with NOAA's
        // published speeds.
        Compound("MP1", ("M2", 1), ("P1", -1)),
        Compound("2PO1", ("P1", 2), ("O1", -1)),
        Compound("SO1", ("S2", 1), ("O1", -1)),
        Compound("MSN2", ("M2", 1), ("S2", 1), ("N2", -1)),
        Compound("MNS2", ("M2", 1), ("N2", 1), ("S2", -1)),
        Compound("OP2", ("O1", 1), ("P1", 1)),
        Compound("MKS2", ("M2", 1), ("K2", 1), ("S2", -1)),
        Compound("2NS2", ("N2", 2), ("S2", -1)),
        Compound("MLN2S2", ("M2", 1), ("L2", 1), ("N2", 1), ("S2", -2)),
        Compound("2ML2S2", ("M2", 2), ("L2", 1), ("S2", -2)),
        Compound("SKM2", ("S2", 1), ("K2", 1), ("M2", -1)),
        Compound("2MS2K2", ("M2", 2), ("S2", 1), ("K2", -2)),
        Compound("MKL2S2", ("M2", 1), ("K2", 1), ("L2", 1), ("S2", -2)),
        Compound("M2KS2", ("M2", 1), ("K2", 2), ("S2", -2)),
        Compound("2SNMK2", ("S2", 2), ("N2", 1), ("M2", -1), ("K2", -1)),
        Compound("2KMSN2", ("K2", 2), ("M2", 1), ("S2", -1), ("N2", -1)),
        Compound("SO3", ("S2", 1), ("O1", 1)),
        Compound("SK3", ("S2", 1), ("K1", 1)),
        Compound("NO3", ("N2", 1), ("O1", 1)),
        Compound("MK4", ("M2", 1), ("K2", 1)),
        Compound("SN4", ("S2", 1), ("N2", 1)),
        Compound("2MLS4", ("M2", 2), ("L2", 1), ("S2", -1)),
        Compound("3MS4", ("M2", 3), ("S2", -1)),
        Compound("ML4", ("M2", 1), ("L2", 1)),
        Compound("N4", ("N2", 2)),
        Compound("SL4", ("S2", 1), ("L2", 1)),
        Compound("MNO5", ("M2", 1), ("N2", 1), ("O1", 1)),
        Compound("2MO5", ("M2", 2), ("O1", 1)),
        Compound("2MK5", ("M2", 2), ("K1", 1)),
        Compound("MSK5", ("M2", 1), ("S2", 1), ("K1", 1)),
        Compound("3KM5", ("K1", 1), ("K2", 1), ("M2", 1)),
        Compound("2MP5", ("M2", 2), ("P1", 1)),
        Compound("3MP5", ("M2", 3), ("P1", -1)),
        Compound("MNK5", ("M2", 1), ("N2", 1), ("K1", 1)),
        Compound("2SM6", ("S2", 2), ("M2", 1)),
        Compound("2MN6", ("M2", 2), ("N2", 1)),
        Compound("MSN6", ("M2", 1), ("S2", 1), ("N2", 1)),
        Compound("2MS6", ("M2", 2), ("S2", 1)),
        Compound("2NMLS6", ("N2", 2), ("M2", 1), ("L2", 1), ("S2", -1)),
        Compound("2NM6", ("N2", 2), ("M2", 1)),
        Compound("MSL6", ("M2", 1), ("S2", 1), ("L2", 1)),
        Compound("2ML6", ("M2", 2), ("L2", 1)),
        Compound("MSK6", ("M2", 1), ("S2", 1), ("K2", 1)),
        Compound("2MLNS6", ("M2", 2), ("L2", 1), ("N2", 1), ("S2", -1)),
        Compound("3MLS6", ("M2", 3), ("L2", 1), ("S2", -1)),
        Compound("2MK6", ("M2", 2), ("K2", 1)),
        Compound("2MNO7", ("M2", 2), ("N2", 1), ("O1", 1)),
        Compound("2NMK7", ("N2", 2), ("M2", 1), ("K1", 1)),
        Compound("2MSO7", ("M2", 2), ("S2", 1), ("O1", 1)),
        Compound("MSKO7", ("M2", 1), ("S2", 1), ("K2", 1), ("O1", 1)),
        Compound("2MSN8", ("M2", 2), ("S2", 1), ("N2", 1)),
        Compound("3MS8", ("M2", 3), ("S2", 1)),
        Compound("2MS8", ("M2", 2), ("S2", 2)),
        Compound("2MN8", ("M2", 2), ("N2", 2)),
        Compound("3MN8", ("M2", 3), ("N2", 1)),
        Compound("2MSL8", ("M2", 2), ("S2", 1), ("L2", 1)),
        Compound("4MLS8", ("M2", 4), ("L2", 1), ("S2", -1)),
        Compound("3ML8", ("M2", 3), ("L2", 1)),
        Compound("3MK8", ("M2", 3), ("K2", 1)),
        Compound("2MSK8", ("M2", 2), ("S2", 1), ("K2", 1)),
        Compound("2M2NK9", ("M2", 2), ("N2", 2), ("K1", 1)),
        Compound("3MNK9", ("M2", 3), ("N2", 1), ("K1", 1)),
        Compound("4MK9", ("M2", 4), ("K1", 1)),
        Compound("3MSK9", ("M2", 3), ("S2", 1), ("K1", 1)),
        Compound("4MN10", ("M2", 4), ("N2", 1)),
        Compound("M10", ("M2", 5)),
        Compound("3MNS10", ("M2", 3), ("N2", 1), ("S2", 1)),
        Compound("4MS10", ("M2", 4), ("S2", 1)),
        Compound("3MSL10", ("M2", 3), ("S2", 1), ("L2", 1)),
        Compound("3M2S10", ("M2", 3), ("S2", 2)),
        Compound("4MSK11", ("M2", 4), ("S2", 1), ("K1", 1)),
        Compound("4MNS12", ("M2", 4), ("N2", 1), ("S2", 1)),
        Compound("5MS12", ("M2", 5), ("S2", 1)),
        Compound("4MSL12", ("M2", 4), ("S2", 1), ("L2", 1)),
        Compound("4M2S12", ("M2", 4), ("S2", 2)),

        // T₂ − K₁, R₂ − P₁ and K₂ − P₁ are solar terms: NOAA's predictions
        // give them no nodal terms, and RP1 a constant of −90°.
        Solar("TK1", [1, 0, -2, 0, 0, 1], 90),
        Solar("RP1", [1, 0, 2, 0, 0, -1], -90),
        Solar("KP1", [1, 0, 3, 0, 0, 0], -90),
        Compound("KJ2", ("K1", 1), ("J1", 1)),
        Compound("OO2", ("O1", 1), ("Q1", 1)),
    ];

    private static readonly Dictionary<string, int> NameIndex = BuildIndex();

    /// <summary>The number of constituents NOAA publishes at most stations, the first entries of <see cref="All"/>.</summary>
    public const int StandardCount = 37;

    /// <summary>The index of a constituent in <see cref="All"/>, or −1 when the name is unknown.</summary>
    public static int IndexOf(string name) => NameIndex.TryGetValue(name, out var index) ? index : -1;

    private static Dictionary<string, int> BuildIndex()
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < All.Length; i++)
        {
            index.Add(All[i].Name, i);
            foreach (var alias in All[i].Aliases)
            {
                index.Add(alias, i);
            }
        }

        return index;
    }

    /// <summary>A semidiurnal lunar constituent with M₂'s nodal terms (u = 2ξ − 2ν, f = f(M₂)).</summary>
    private static ConstituentDefinition Lunar2(string name, int[] arguments, double phase = 0, params string[] aliases) =>
        Define(name, arguments, phase, U(xi: 2, nu: -2), F((NodeFactor.M2, 1)), aliases);

    /// <summary>A diurnal lunar constituent with O₁'s nodal terms (u = 2ξ − ν, f = f(O₁)), V's constant +90°.</summary>
    private static ConstituentDefinition Lunar1(string name, int[] arguments, params string[] aliases) =>
        Define(name, arguments, 90, U(xi: 2, nu: -1), F((NodeFactor.O1, 1)), aliases);

    /// <summary>A solar constituent: no nodal terms.</summary>
    private static ConstituentDefinition Solar(string name, int[] arguments, double phase = 0) =>
        Define(name, arguments, phase, U(), F());

    /// <summary>
    /// A compound constituent: the sum of multiples of constituents defined
    /// earlier in <see cref="All"/>. V, u and the speed add; f multiplies,
    /// a component's factor raised to its multiple's absolute value.
    /// </summary>
    private static ConstituentDefinition Compound(string name, params (string Component, int Multiple)[] components)
    {
        var arguments = new int[6];
        var corrections = new int[6];
        var factorPowers = new int[NodalTerms.FactorCount];
        var speedArguments = new int[6];
        var phase = 0.0;
        foreach (var (componentName, multiple) in components)
        {
            var component = BaseConstituents[componentName];
            for (var i = 0; i < 6; i++)
            {
                arguments[i] += multiple * component.Arguments[i];
                speedArguments[i] += multiple * component.SpeedArguments[i];
                corrections[i] += multiple * component.Corrections[i];
            }

            for (var i = 0; i < factorPowers.Length; i++)
            {
                factorPowers[i] += Math.Abs(multiple) * component.FactorPowers[i];
            }

            phase += multiple * component.PhaseConstant;
        }

        return new ConstituentDefinition
        {
            Name = name,
            Arguments = arguments,
            SpeedArguments = speedArguments,
            PhaseConstant = Angles.Normalize(phase),
            Corrections = corrections,
            FactorPowers = factorPowers,
        };
    }

    private static ConstituentDefinition Define(
        string name, int[] arguments, double phase, int[] corrections, int[] factorPowers, string[]? aliases = null, int[]? speedArguments = null) =>
        new()
        {
            Name = name,
            Aliases = aliases ?? [],
            Arguments = arguments,
            SpeedArguments = speedArguments ?? arguments,
            PhaseConstant = phase,
            Corrections = corrections,
            FactorPowers = factorPowers,
        };

    private static int[] U(int xi = 0, int nu = 0, int nuPrime = 0, int twoNuSecond = 0, int r = 0, int q = 0) =>
        [xi, nu, nuPrime, twoNuSecond, r, q];

    private static int[] F(params (NodeFactor Factor, int Power)[] powers)
    {
        var result = new int[NodalTerms.FactorCount];
        foreach (var (factor, power) in powers)
        {
            result[(int)factor] = power;
        }

        return result;
    }
}
