namespace EncDotNet.S100.Quantities;

/// <summary>
/// A depth: a vertical <see cref="Length"/> measured from a vertical datum,
/// stored canonically in metres.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Depth"/> is a specialization of <see cref="Length"/> for the
/// core hydrographic concept of a sounding. It carries the same
/// unit-independent, value-semantics design as <see cref="Length"/> and adds
/// depth-specific conveniences (fathom-and-feet decomposition via
/// <see cref="TotalFathoms"/>/<see cref="TotalFeet"/>). Positive values are
/// below the datum; negative values (drying heights) are permitted and their
/// sign is preserved.
/// </para>
/// <para>
/// A <see cref="Depth"/> converts implicitly to its underlying
/// <see cref="Length"/> so it can participate in generic distance arithmetic,
/// while remaining a distinct type at API boundaries where the hydrographic
/// meaning matters (safety contour, safety depth, contour value).
/// </para>
/// </remarks>
public readonly record struct Depth : IComparable<Depth>
{
    private readonly Length _value;

    private Depth(Length value) => _value = value;

    /// <summary>A zero depth (at the datum). Equal to <c>default(Depth)</c>.</summary>
    public static Depth Zero => default;

    /// <summary>Creates a depth from a value in metres.</summary>
    public static Depth FromMetres(double metres) => new(Length.FromMetres(metres));

    /// <summary>Creates a depth from a value in international feet.</summary>
    public static Depth FromFeet(double feet) => new(Length.FromFeet(feet));

    /// <summary>Creates a depth from a value in fathoms.</summary>
    public static Depth FromFathoms(double fathoms) => new(Length.FromFathoms(fathoms));

    /// <summary>Creates a depth from an existing <see cref="Length"/>.</summary>
    public static Depth FromLength(Length length) => new(length);

    /// <summary>The underlying vertical <see cref="Length"/>.</summary>
    public Length AsLength() => _value;

    /// <summary>The depth expressed in metres (the canonical unit).</summary>
    public double TotalMetres => _value.TotalMetres;

    /// <summary>The depth expressed in international feet.</summary>
    public double TotalFeet => _value.TotalFeet;

    /// <summary>The depth expressed in fathoms.</summary>
    public double TotalFathoms => _value.TotalFathoms;

    /// <summary>Returns the absolute (non-negative) magnitude of this depth.</summary>
    public Depth Abs() => new(_value.Abs());

    /// <summary>Converts a depth to its underlying vertical <see cref="Length"/>.</summary>
    public static implicit operator Length(Depth depth) => depth._value;

    /// <summary>Indicates whether one depth is shallower than another.</summary>
    public static bool operator <(Depth a, Depth b) => a._value < b._value;

    /// <summary>Indicates whether one depth is deeper than another.</summary>
    public static bool operator >(Depth a, Depth b) => a._value > b._value;

    /// <summary>Indicates whether one depth is shallower than or equal to another.</summary>
    public static bool operator <=(Depth a, Depth b) => a._value <= b._value;

    /// <summary>Indicates whether one depth is deeper than or equal to another.</summary>
    public static bool operator >=(Depth a, Depth b) => a._value >= b._value;

    /// <inheritdoc/>
    public int CompareTo(Depth other) => _value.CompareTo(other._value);

    /// <summary>
    /// Returns an invariant-culture string of the form <c>"12.5 m"</c> for
    /// diagnostic purposes. User-facing presentation should go through a
    /// dedicated formatter.
    /// </summary>
    public override string ToString() => _value.ToString();
}
