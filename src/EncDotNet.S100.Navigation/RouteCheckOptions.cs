using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// Tuning for a <see cref="RouteChecker"/>: one set for every check, set
/// once rather than per run. What the user decides on each run is in
/// <see cref="RouteCheckSettings"/>.
/// </summary>
public sealed record RouteCheckOptions
{
    /// <summary>The default options.</summary>
    public static RouteCheckOptions Default { get; } = new();

    /// <summary>
    /// The corridor's half-width on a side whose cross-track limit isn't set:
    /// how far it reaches to port, or to starboard, of the track. The full
    /// corridor is twice this when neither side is set. Every check uses it.
    /// Default 50 m.
    /// </summary>
    public Length DefaultLimit { get; init; } = Length.FromMetres(50);

    /// <summary>Tuning for <see cref="ChartCheck"/>.</summary>
    public ChartCheckOptions Chart { get; init; } = ChartCheckOptions.Default;

    /// <summary>Tuning for <see cref="AreaCheck"/>.</summary>
    public RouteAreaOptions Areas { get; init; } = RouteAreaOptions.Default;

    /// <summary>Tuning for <see cref="WarningCheck"/>.</summary>
    public RouteWarningOptions Warnings { get; init; } = RouteWarningOptions.Default;

    /// <summary>Tuning for <see cref="ClearanceCheck"/>.</summary>
    public PassageCheckOptions Clearance { get; init; } = PassageCheckOptions.Default;

    /// <summary>
    /// Renders every finding's title and meta line. Hosts with their own
    /// resources (the viewer's <c>Strings.resx</c>) pass their own.
    /// </summary>
    public RouteFindingFormatter Formatter { get; init; } = RouteFindingFormatter.Default;
}
