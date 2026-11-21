namespace EncDotNet.S100.Tides;

/// <summary>
/// One harmonic constituent of a tidal current station: its NOAA name and
/// its amplitude and Greenwich phase lag along the station's major and
/// minor axes.
/// </summary>
/// <remarks>
/// NOAA analyses a current into two components: along the major axis, the
/// direction the current mostly flows in (positive toward the flood), and
/// along the minor axis, 90° clockwise from it. A rectilinear current is
/// predicted from the major axis alone; a rotary current needs both.
/// </remarks>
/// <param name="Name">
/// The constituent's NOAA name, such as <c>M2</c>. See
/// <see cref="HarmonicConstituents.Names"/> for the names the predictor knows.
/// </param>
/// <param name="MajorAmplitudeMetersPerSecond">The amplitude along the major axis, in metres per second.</param>
/// <param name="MajorPhaseDegrees">The phase lag along the major axis, in degrees, referred to Greenwich (NOAA's <c>majorPhaseGMT</c>).</param>
/// <param name="MinorAmplitudeMetersPerSecond">The amplitude along the minor axis, in metres per second; zero when not known.</param>
/// <param name="MinorPhaseDegrees">The phase lag along the minor axis, in degrees, referred to Greenwich (NOAA's <c>minorPhaseGMT</c>).</param>
public sealed record CurrentConstituent(
    string Name,
    double MajorAmplitudeMetersPerSecond,
    double MajorPhaseDegrees,
    double MinorAmplitudeMetersPerSecond = 0,
    double MinorPhaseDegrees = 0);
