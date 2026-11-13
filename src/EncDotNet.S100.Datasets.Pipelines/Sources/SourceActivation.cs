using EncDotNet.S100.Datasets.Pipelines.Portrayal;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// When a host runs a source: always, or only while the view is zoomed in
/// enough that the source's data is useful and affordable, such as an AIS
/// stream that would otherwise subscribe to an ocean's worth of targets.
/// </summary>
/// <remarks>
/// Outside its range a host pauses the source
/// (<see cref="IHostedDatasetProcessor.IsActive"/>) and keeps the features it
/// already has.
/// </remarks>
public sealed record SourceActivation
{
    private SourceActivation(double? maxViewportSpanDegrees) =>
        MaxViewportSpanDegrees = maxViewportSpanDegrees;

    /// <summary>The source runs whatever the view.</summary>
    public static SourceActivation Always { get; } = new((double?)null);

    /// <summary>
    /// The widest view, in degrees of latitude or longitude, in which the
    /// source runs; <see langword="null"/> for <see cref="Always"/>.
    /// </summary>
    public double? MaxViewportSpanDegrees { get; }

    /// <summary>The source runs while the view spans at most <paramref name="degrees"/> of latitude and of longitude.</summary>
    /// <param name="degrees">The widest span, greater than zero.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="degrees"/> is not a positive, finite number.</exception>
    public static SourceActivation BelowViewportSpan(double degrees)
    {
        if (!double.IsFinite(degrees) || degrees <= 0)
            throw new ArgumentOutOfRangeException(nameof(degrees), degrees, "The span must be a positive number of degrees.");
        return new SourceActivation(degrees);
    }

    /// <summary>
    /// Whether the source runs for <paramref name="viewport"/>. With no view
    /// yet, only an <see cref="Always"/> source runs.
    /// </summary>
    /// <param name="viewport">The area in view, or <see langword="null"/> when there's no view yet.</param>
    public bool IsActiveFor(GeographicBounds? viewport)
    {
        if (MaxViewportSpanDegrees is not { } max)
            return true;
        if (viewport is not { } v)
            return false;
        var longitudeSpan = v.MaxLongitude - v.MinLongitude;
        if (longitudeSpan < 0)
            longitudeSpan += 360; // The view crosses the antimeridian.
        return v.MaxLatitude - v.MinLatitude <= max && longitudeSpan <= max;
    }
}
