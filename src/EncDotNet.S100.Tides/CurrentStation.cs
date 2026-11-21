namespace EncDotNet.S100.Tides;

/// <summary>
/// A harmonic tidal current station: its identifier, its constituents along
/// the major and minor axes, the axes' mean currents and directions, and
/// its flood and ebb directions.
/// </summary>
/// <remarks>
/// <para>
/// The velocity along the major axis is positive while the current floods
/// and negative while it ebbs. A <i>rectilinear</i> current, the usual kind
/// in a channel, flows along that axis: it sets toward
/// <see cref="FloodDirectionDegrees"/> while it floods and toward
/// <see cref="EbbDirectionDegrees"/> while it ebbs. A <i>rotary</i> current
/// turns round the compass through the tide; its direction comes from both
/// axes, the minor axis being 90° clockwise from the major.
/// </para>
/// <para>
/// Directions are degrees clockwise from true north, toward which the
/// current flows. Velocities are metres per second.
/// </para>
/// </remarks>
public sealed class CurrentStation
{
    /// <summary>Creates a station.</summary>
    /// <param name="id">The station identifier, such as NOAA's <c>PUG1701_33</c> (station and depth bin).</param>
    /// <param name="name">The station's name, such as <c>Deception Pass (Narrows)</c>.</param>
    /// <param name="constituents">
    /// The harmonic constituents, with Greenwich phases. Every name must be
    /// one of <see cref="HarmonicConstituents.Names"/>, and no name may
    /// appear twice.
    /// </param>
    /// <param name="majorAxisDirectionDegrees">The direction of the major axis's positive (flood) end, in degrees true.</param>
    /// <param name="majorMeanVelocity">The mean current along the major axis, in metres per second (NOAA's <c>majorMeanSpeed</c>).</param>
    /// <param name="minorMeanVelocity">The mean current along the minor axis, in metres per second (NOAA's <c>minorMeanSpeed</c>).</param>
    /// <param name="floodDirectionDegrees">The direction the current sets toward while it floods, in degrees true.</param>
    /// <param name="ebbDirectionDegrees">The direction the current sets toward while it ebbs, in degrees true.</param>
    /// <param name="isRotary">Whether the current is rotary, so its direction comes from both axes.</param>
    /// <exception cref="ArgumentException">
    /// A constituent's name is unknown or repeated, or a value isn't a
    /// finite number.
    /// </exception>
    public CurrentStation(
        string id,
        string name,
        IReadOnlyList<CurrentConstituent> constituents,
        double majorAxisDirectionDegrees,
        double majorMeanVelocity,
        double minorMeanVelocity,
        double floodDirectionDegrees,
        double ebbDirectionDegrees,
        bool isRotary)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(constituents);

        var seen = new HashSet<int>();
        foreach (var constituent in constituents)
        {
            ArgumentNullException.ThrowIfNull(constituent, nameof(constituents));
            var index = ConstituentTable.IndexOf(constituent.Name);
            if (index < 0)
            {
                throw new ArgumentException($"Unknown tidal constituent '{constituent.Name}'.", nameof(constituents));
            }

            if (!seen.Add(index))
            {
                throw new ArgumentException($"Tidal constituent '{constituent.Name}' appears more than once.", nameof(constituents));
            }

            if (!double.IsFinite(constituent.MajorAmplitudeMetersPerSecond) || !double.IsFinite(constituent.MajorPhaseDegrees)
                || !double.IsFinite(constituent.MinorAmplitudeMetersPerSecond) || !double.IsFinite(constituent.MinorPhaseDegrees))
            {
                throw new ArgumentException($"Tidal constituent '{constituent.Name}' has a non-finite amplitude or phase.", nameof(constituents));
            }
        }

        if (!double.IsFinite(majorAxisDirectionDegrees) || !double.IsFinite(majorMeanVelocity) || !double.IsFinite(minorMeanVelocity)
            || !double.IsFinite(floodDirectionDegrees) || !double.IsFinite(ebbDirectionDegrees))
        {
            throw new ArgumentException("The station's directions and mean currents must be finite numbers.");
        }

        Id = id;
        Name = name;
        Constituents = [.. constituents];
        MajorAxisDirectionDegrees = Angles.Normalize(majorAxisDirectionDegrees);
        MajorMeanVelocity = majorMeanVelocity;
        MinorMeanVelocity = minorMeanVelocity;
        FloodDirectionDegrees = Angles.Normalize(floodDirectionDegrees);
        EbbDirectionDegrees = Angles.Normalize(ebbDirectionDegrees);
        IsRotary = isRotary;
    }

    /// <summary>The station identifier.</summary>
    public string Id { get; }

    /// <summary>The station's name.</summary>
    public string Name { get; }

    /// <summary>The harmonic constituents, with Greenwich phases.</summary>
    public IReadOnlyList<CurrentConstituent> Constituents { get; }

    /// <summary>The direction of the major axis's positive (flood) end, in degrees true, from 0 up to 360.</summary>
    public double MajorAxisDirectionDegrees { get; }

    /// <summary>The mean current along the major axis, in metres per second.</summary>
    public double MajorMeanVelocity { get; }

    /// <summary>The mean current along the minor axis, in metres per second.</summary>
    public double MinorMeanVelocity { get; }

    /// <summary>The direction the current sets toward while it floods, in degrees true, from 0 up to 360.</summary>
    public double FloodDirectionDegrees { get; }

    /// <summary>The direction the current sets toward while it ebbs, in degrees true, from 0 up to 360.</summary>
    public double EbbDirectionDegrees { get; }

    /// <summary>Whether the current is rotary, so its direction and speed come from both axes.</summary>
    public bool IsRotary { get; }

    /// <summary>
    /// The ratio of the M2 constituent's minor-axis amplitude to its
    /// major-axis amplitude: near 0 for a rectilinear current, near 1 for a
    /// current that turns evenly round the compass. Zero when the station
    /// has no M2 or its major amplitude is zero.
    /// </summary>
    public double M2EllipseRatio
    {
        get
        {
            foreach (var constituent in Constituents)
            {
                if (string.Equals(constituent.Name, "M2", StringComparison.OrdinalIgnoreCase))
                {
                    return constituent.MajorAmplitudeMetersPerSecond == 0
                        ? 0
                        : Math.Abs(constituent.MinorAmplitudeMetersPerSecond / constituent.MajorAmplitudeMetersPerSecond);
                }
            }

            return 0;
        }
    }
}
