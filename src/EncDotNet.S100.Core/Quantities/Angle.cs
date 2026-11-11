using System.Globalization;

namespace EncDotNet.S100.Quantities;

/// <summary>
/// A planar angle, stored canonically in degrees.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Angle"/> models directions and rotations — most commonly
/// marine bearings measured clockwise from true north — without committing
/// callers to degrees or radians at the API boundary. Values are created
/// through <see cref="FromDegrees"/>/<see cref="FromRadians"/> and read back
/// through <see cref="TotalDegrees"/>/<see cref="TotalRadians"/>.
/// </para>
/// <para>
/// The type follows the same <c>readonly record struct</c> design as
/// <see cref="Length"/>: value semantics, arithmetic operators, and ordering.
/// Stored angles are not implicitly normalized; call <see cref="Normalized"/>
/// to fold a value into the <c>[0, 360)</c> degree range used for bearings.
/// </para>
/// <para>
/// Because the backing store is a <see cref="double"/>, equality (<c>==</c>)
/// is exact floating-point equality. Prefer <see cref="CompareTo(Angle)"/>
/// with a tolerance when comparing computed values.
/// </para>
/// </remarks>
public readonly record struct Angle : IComparable<Angle>
{
    /// <summary>Degrees per radian (180 / π).</summary>
    public const double DegreesPerRadian = 180.0 / Math.PI;

    /// <summary>Degrees in a full turn.</summary>
    public const double DegreesPerTurn = 360.0;

    private readonly double _degrees;

    private Angle(double degrees) => _degrees = degrees;

    /// <summary>A zero angle. Equal to <c>default(Angle)</c>.</summary>
    public static Angle Zero => default;

    /// <summary>Creates an angle from a value in degrees.</summary>
    public static Angle FromDegrees(double degrees) => new(degrees);

    /// <summary>Creates an angle from a value in radians.</summary>
    public static Angle FromRadians(double radians) => new(radians * DegreesPerRadian);

    /// <summary>The angle expressed in degrees (the canonical unit).</summary>
    public double TotalDegrees => _degrees;

    /// <summary>The angle expressed in radians.</summary>
    public double TotalRadians => _degrees / DegreesPerRadian;

    /// <summary>
    /// Returns an equivalent angle folded into the <c>[0, 360)</c> degree
    /// range (the convention for compass bearings).
    /// </summary>
    public Angle Normalized()
    {
        double d = _degrees % DegreesPerTurn;
        if (d < 0)
            d += DegreesPerTurn;
        return new(d);
    }

    /// <summary>Adds two angles.</summary>
    public static Angle operator +(Angle a, Angle b) => new(a._degrees + b._degrees);

    /// <summary>Subtracts one angle from another.</summary>
    public static Angle operator -(Angle a, Angle b) => new(a._degrees - b._degrees);

    /// <summary>Negates an angle.</summary>
    public static Angle operator -(Angle a) => new(-a._degrees);

    /// <summary>Scales an angle by a dimensionless factor.</summary>
    public static Angle operator *(Angle a, double factor) => new(a._degrees * factor);

    /// <summary>Scales an angle by a dimensionless factor.</summary>
    public static Angle operator *(double factor, Angle a) => new(a._degrees * factor);

    /// <summary>Divides an angle by a dimensionless divisor.</summary>
    public static Angle operator /(Angle a, double divisor) => new(a._degrees / divisor);

    /// <summary>Indicates whether one angle is less than another.</summary>
    public static bool operator <(Angle a, Angle b) => a._degrees < b._degrees;

    /// <summary>Indicates whether one angle is greater than another.</summary>
    public static bool operator >(Angle a, Angle b) => a._degrees > b._degrees;

    /// <summary>Indicates whether one angle is less than or equal to another.</summary>
    public static bool operator <=(Angle a, Angle b) => a._degrees <= b._degrees;

    /// <summary>Indicates whether one angle is greater than or equal to another.</summary>
    public static bool operator >=(Angle a, Angle b) => a._degrees >= b._degrees;

    /// <inheritdoc/>
    public int CompareTo(Angle other) => _degrees.CompareTo(other._degrees);

    /// <summary>
    /// Returns an invariant-culture string of the form <c>"90°"</c> for
    /// diagnostic purposes. User-facing presentation should go through a
    /// dedicated formatter.
    /// </summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{_degrees}°");
}
