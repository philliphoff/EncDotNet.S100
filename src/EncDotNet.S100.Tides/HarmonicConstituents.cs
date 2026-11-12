namespace EncDotNet.S100.Tides;

/// <summary>The harmonic constituents the predictor knows.</summary>
public static class HarmonicConstituents
{
    /// <summary>
    /// The names of the constituents NOAA CO-OPS publishes: first the 37
    /// standard ones in NOAA's order (M2, S2, N2, K1, M4, O1, … , MS4), then
    /// the 83 shallow-water and minor constituents NOAA lists at some
    /// stations, such as Anchorage (SIGMA1, MP1, … , 2MS6, … , OO2). Names
    /// are matched without regard to case, and a few other spellings are
    /// accepted (RHO1 for RHO, LAMBDA2 for LAM2).
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = [.. ConstituentTable.All.Select(c => c.Name)];

    /// <summary>Whether the predictor knows a constituent.</summary>
    /// <param name="name">The constituent's name.</param>
    /// <returns><c>true</c> when the name, or another spelling of it, is known.</returns>
    public static bool Contains(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ConstituentTable.IndexOf(name) >= 0;
    }

    /// <summary>A constituent's speed, in degrees per mean solar hour.</summary>
    /// <param name="name">The constituent's name.</param>
    /// <returns>The speed, or <c>null</c> when the name is unknown.</returns>
    public static double? TryGetSpeed(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var index = ConstituentTable.IndexOf(name);
        return index < 0 ? null : ConstituentTable.All[index].Speed;
    }
}
