namespace EncDotNet.S100.Viewer.Routing;

// Passage values and per-leg overrides (#897). Each mutator sets one value,
// returns the value it replaced and raises Changed only when something
// changed, so an editor command can record (field, old, new) and revert it.
public sealed partial class Route
{
    private const double OverrideTolerance = 1e-9;

    /// <summary>
    /// Route-level passage values that legs inherit. Never <c>null</c>; empty
    /// for a route with only waypoints.
    /// </summary>
    public RoutePassage Passage { get; } = new();

    /// <summary>
    /// Sets one passage value. Legs that inherit the field follow it; legs
    /// with an override keep their override.
    /// </summary>
    /// <param name="field">The passage field to set.</param>
    /// <param name="value">The new value, or <c>null</c> to unset it.</param>
    /// <returns>The previous passage value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not finite.</exception>
    public double? SetPassageValue(RoutePassageField field, double? value)
    {
        ThrowIfNotFinite(value);
        var previous = Passage.Get(field);
        if (previous == value)
            return previous;
        Passage.Set(field, value);
        OnChanged();
        return previous;
    }

    /// <summary>Sets or clears the planned departure.</summary>
    /// <param name="departure">The departure, or <c>null</c> to use view time.</param>
    /// <returns>The previous departure.</returns>
    public DateTimeOffset? SetDeparture(DateTimeOffset? departure)
    {
        var previous = Passage.Departure;
        if (previous == departure)
            return previous;
        Passage.Departure = departure;
        OnChanged();
        return previous;
    }

    /// <summary>
    /// The value that applies to leg <paramref name="legIndex"/>: its override
    /// when set, otherwise the passage value.
    /// </summary>
    /// <param name="legIndex">Index of the leg in <c>[0, Legs.Count)</c>.</param>
    /// <param name="field">A leg-overridable passage field.</param>
    /// <returns>The effective value, or <c>null</c> when neither is set.</returns>
    public double? GetEffectiveLegValue(int legIndex, RoutePassageField field)
        => LegAt(legIndex).GetOverride(field) ?? Passage.Get(field);

    /// <summary>Whether leg <paramref name="legIndex"/> overrides <paramref name="field"/>.</summary>
    /// <param name="legIndex">Index of the leg in <c>[0, Legs.Count)</c>.</param>
    /// <param name="field">A leg-overridable passage field.</param>
    /// <returns><c>true</c> when the leg carries its own value.</returns>
    public bool IsLegOverridden(int legIndex, RoutePassageField field)
        => LegAt(legIndex).GetOverride(field).HasValue;

    /// <summary>
    /// Sets leg <paramref name="legIndex"/>'s override of
    /// <paramref name="field"/>. A value equal to the passage value stores no
    /// override, so the leg inherits again; <c>null</c> resets the leg to
    /// inherit.
    /// </summary>
    /// <param name="legIndex">Index of the leg in <c>[0, Legs.Count)</c>.</param>
    /// <param name="field">A leg-overridable passage field.</param>
    /// <param name="value">The override, or <c>null</c> to reset.</param>
    /// <returns>The previous override (<c>null</c> when the leg inherited).</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="legIndex"/> is not a valid leg index, or
    /// <paramref name="value"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="field"/> can't be overridden per leg.</exception>
    public double? SetLegOverride(int legIndex, RoutePassageField field, double? value)
    {
        ThrowIfNotFinite(value);
        var leg = LegAt(legIndex);
        var previous = leg.GetOverride(field);

        var passageValue = Passage.Get(field);
        if (value is { } v && passageValue is { } p && Math.Abs(v - p) <= OverrideTolerance)
            value = null;

        if (previous == value)
            return previous;
        leg.SetOverride(field, value);
        OnChanged();
        return previous;
    }

    /// <summary>Resets leg <paramref name="legIndex"/> to inherit <paramref name="field"/>.</summary>
    /// <param name="legIndex">Index of the leg in <c>[0, Legs.Count)</c>.</param>
    /// <param name="field">A leg-overridable passage field.</param>
    /// <returns>The override that was removed, or <c>null</c>.</returns>
    public double? ResetLegOverride(int legIndex, RoutePassageField field)
        => SetLegOverride(legIndex, field, null);

    private RouteLeg LegAt(int legIndex)
    {
        if (legIndex < 0 || legIndex >= _legs.Count)
            throw new ArgumentOutOfRangeException(nameof(legIndex));
        return _legs[legIndex];
    }

    private static void ThrowIfNotFinite(double? value)
    {
        if (value is { } v && !double.IsFinite(v))
            throw new ArgumentOutOfRangeException(nameof(value), v, "Passage values must be finite.");
    }
}
