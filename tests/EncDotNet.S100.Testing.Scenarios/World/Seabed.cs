namespace EncDotNet.S100.Testing.Scenarios.World;

/// <summary>
/// An analytic seabed: depth below chart datum in metres (positive down,
/// negative where the seabed dries) at any local point.
/// </summary>
/// <remarks>
/// The depth is a regional shore profile plus a list of features. Every term
/// is a closed-form function of the local coordinates, so tests can compute
/// the true depth at any point exactly. Land is where the depth is shallower
/// than minus the high-water height (see <see cref="SyntheticWorld.HighWaterHeight"/>).
/// </remarks>
/// <param name="Shore">The regional shore profile.</param>
/// <param name="Features">Channels, spits, banks and shoals on top of it.</param>
/// <param name="Relief">Small seeded undulations, or <c>null</c> for none.</param>
public sealed record Seabed(ShoreProfile Shore, IReadOnlyList<SeabedFeature> Features, MicroRelief? Relief = null)
{
    /// <summary>The depth below chart datum at a local point.</summary>
    /// <param name="x">Metres east of the origin.</param>
    /// <param name="y">Metres north of the origin.</param>
    /// <returns>Metres below chart datum (negative for drying heights and land).</returns>
    public double DepthAt(double x, double y)
    {
        var regional = Shore.DepthAt(y);
        var depth = regional;
        foreach (var feature in Features)
        {
            depth += feature.Contribution(x, y, regional);
        }

        return depth + (Relief?.At(x, y) ?? 0);
    }
}

/// <summary>
/// A straight east–west shore with land to the south: the depth rises from
/// <c>Offset − Amplitude</c> far south to <c>Offset + Amplitude</c> far north,
/// following a hyperbolic tangent centred on <see cref="CentreY"/>.
/// </summary>
/// <param name="CentreY">Where the profile is at <see cref="Offset"/>, in metres north.</param>
/// <param name="Width">The profile's horizontal scale in metres.</param>
/// <param name="Amplitude">Half the depth change across the profile, in metres.</param>
/// <param name="Offset">The depth at <see cref="CentreY"/>, in metres.</param>
public sealed record ShoreProfile(double CentreY, double Width, double Amplitude, double Offset)
{
    /// <summary>The regional depth at a northing.</summary>
    /// <param name="y">Metres north of the origin.</param>
    /// <returns>The depth in metres.</returns>
    public double DepthAt(double y) => Amplitude * Math.Tanh((y - CentreY) / Width) + Offset;
}

/// <summary>One term of the seabed.</summary>
public abstract record SeabedFeature
{
    /// <summary>The term's change to the depth at a point.</summary>
    /// <param name="x">Metres east of the origin.</param>
    /// <param name="y">Metres north of the origin.</param>
    /// <param name="regional">The regional depth there, from the shore profile.</param>
    /// <returns>Metres added to the depth (negative shoals).</returns>
    public abstract double Contribution(double x, double y, double regional);

    /// <summary>
    /// Raises the seabed towards <paramref name="crest"/> by
    /// <paramref name="weight"/> (0 to 1), never deepening it.
    /// </summary>
    private protected static double ShoalTowards(double crest, double regional, double weight) =>
        Math.Min(0, crest - regional) * weight;

    private protected static double Gaussian(double offset, double width)
    {
        var u = offset / width;
        return Math.Exp(-u * u);
    }
}

/// <summary>An east–west channel: a Gaussian trough deepening the seabed.</summary>
/// <param name="CentreY">The channel's axis, in metres north.</param>
/// <param name="Width">The trough's Gaussian width in metres.</param>
/// <param name="Deepening">The extra depth on the axis, in metres.</param>
public sealed record Channel(double CentreY, double Width, double Deepening) : SeabedFeature
{
    /// <inheritdoc />
    public override double Contribution(double x, double y, double regional) =>
        Deepening * Gaussian(y - CentreY, Width);
}

/// <summary>
/// A spit running north from the shore along <see cref="AxisX"/> to
/// <see cref="TipY"/>: a ridge whose crest sits at <see cref="CrestDepth"/>
/// (negative dries) and which fades out over <see cref="TipTaper"/> metres
/// beyond the tip.
/// </summary>
/// <param name="AxisX">The ridge's axis, in metres east.</param>
/// <param name="TipY">Where the ridge starts to fade, in metres north.</param>
/// <param name="Width">The ridge's Gaussian half-width in metres.</param>
/// <param name="CrestDepth">The crest's depth in metres (negative for a drying height).</param>
/// <param name="TipTaper">How far the tip fades out, in metres.</param>
public sealed record Spit(double AxisX, double TipY, double Width, double CrestDepth, double TipTaper) : SeabedFeature
{
    /// <inheritdoc />
    public override double Contribution(double x, double y, double regional)
    {
        var along = 0.5 * (1 - Math.Tanh((y - TipY) / TipTaper));
        return ShoalTowards(CrestDepth, regional, along * Gaussian(x - AxisX, Width));
    }
}

/// <summary>
/// An elliptical mound: a bank when <see cref="CrestDepth"/> is negative
/// (it dries), a shoal otherwise.
/// </summary>
/// <param name="CentreX">Centre, in metres east.</param>
/// <param name="CentreY">Centre, in metres north.</param>
/// <param name="RadiusX">Gaussian radius east–west, in metres.</param>
/// <param name="RadiusY">Gaussian radius north–south, in metres.</param>
/// <param name="CrestDepth">The crest's depth in metres (negative for a drying height).</param>
public sealed record Mound(double CentreX, double CentreY, double RadiusX, double RadiusY, double CrestDepth) : SeabedFeature
{
    /// <inheritdoc />
    public override double Contribution(double x, double y, double regional) =>
        ShoalTowards(CrestDepth, regional, Gaussian(x - CentreX, RadiusX) * Gaussian(y - CentreY, RadiusY));
}

/// <summary>
/// Small seeded undulations so that soundings and grids aren't perfectly
/// smooth: a sum of plane waves with seeded directions and phases.
/// </summary>
/// <param name="Waves">The waves: amplitude (m), wavelength (m), direction (radians) and phase (radians).</param>
public sealed record MicroRelief(IReadOnlyList<(double Amplitude, double Wavelength, double Direction, double Phase)> Waves)
{
    /// <summary>Draws <paramref name="count"/> waves of at most <paramref name="amplitude"/> metres each.</summary>
    /// <param name="random">The seeded sequence.</param>
    /// <param name="count">How many waves.</param>
    /// <param name="amplitude">The largest amplitude of one wave, in metres.</param>
    /// <returns>The relief.</returns>
    public static MicroRelief Draw(ScenarioRandom random, int count, double amplitude)
    {
        ArgumentNullException.ThrowIfNull(random);
        var waves = new List<(double, double, double, double)>(count);
        for (var i = 0; i < count; i++)
        {
            waves.Add((
                random.NextDouble(0.5, 1.0) * amplitude,
                random.NextDouble(400, 1500),
                random.NextDouble(0, Math.PI),
                random.NextDouble(0, 2 * Math.PI)));
        }

        return new MicroRelief(waves);
    }

    /// <summary>The relief at a point, in metres.</summary>
    /// <param name="x">Metres east.</param>
    /// <param name="y">Metres north.</param>
    /// <returns>The height change in metres.</returns>
    public double At(double x, double y)
    {
        double sum = 0;
        foreach (var (amplitude, wavelength, direction, phase) in Waves)
        {
            var along = x * Math.Cos(direction) + y * Math.Sin(direction);
            sum += amplitude * Math.Sin(2 * Math.PI * along / wavelength + phase);
        }

        return sum;
    }
}
