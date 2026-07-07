using System;
using System.Globalization;

namespace EncDotNet.S100.Quantities;

/// <summary>
/// A linear speed, stored canonically in metres per second.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Speed"/> models how fast something moves without committing
/// callers to a unit at the API boundary. It is particularly useful where a
/// single concept arrives in different units from different feeds — AIS
/// reports speed over ground in knots, while own-ship simulators often work
/// in metres per second. Both become a <see cref="Speed"/> and are read back
/// in whichever unit the consumer needs.
/// </para>
/// <para>
/// The type follows the same <c>readonly record struct</c> design as
/// <see cref="Length"/>: value semantics, arithmetic operators, and ordering.
/// </para>
/// <para>
/// Because the backing store is a <see cref="double"/>, equality (<c>==</c>)
/// is exact floating-point equality. Prefer <see cref="CompareTo(Speed)"/>
/// with a tolerance when comparing computed values.
/// </para>
/// </remarks>
public readonly record struct Speed : IComparable<Speed>
{
    /// <summary>Metres per second in one knot (1 NM/h = 1852 m / 3600 s).</summary>
    public const double MetresPerSecondPerKnot = Length.MetresPerNauticalMile / 3600.0;

    /// <summary>Metres per second in one kilometre per hour.</summary>
    public const double MetresPerSecondPerKilometrePerHour = 1000.0 / 3600.0;

    private readonly double _metresPerSecond;

    private Speed(double metresPerSecond) => _metresPerSecond = metresPerSecond;

    /// <summary>A zero speed. Equal to <c>default(Speed)</c>.</summary>
    public static Speed Zero => default;

    /// <summary>Creates a speed from a value in metres per second.</summary>
    public static Speed FromMetresPerSecond(double metresPerSecond) => new(metresPerSecond);

    /// <summary>Creates a speed from a value in knots.</summary>
    public static Speed FromKnots(double knots) => new(knots * MetresPerSecondPerKnot);

    /// <summary>Creates a speed from a value in kilometres per hour.</summary>
    public static Speed FromKilometresPerHour(double kilometresPerHour) =>
        new(kilometresPerHour * MetresPerSecondPerKilometrePerHour);

    /// <summary>The speed expressed in metres per second (the canonical unit).</summary>
    public double TotalMetresPerSecond => _metresPerSecond;

    /// <summary>The speed expressed in knots.</summary>
    public double TotalKnots => _metresPerSecond / MetresPerSecondPerKnot;

    /// <summary>The speed expressed in kilometres per hour.</summary>
    public double TotalKilometresPerHour => _metresPerSecond / MetresPerSecondPerKilometrePerHour;

    /// <summary>Returns the absolute (non-negative) magnitude of this speed.</summary>
    public Speed Abs() => new(Math.Abs(_metresPerSecond));

    /// <summary>The distance travelled at this speed over the supplied duration.</summary>
    public Length DistanceOver(TimeSpan duration) =>
        Length.FromMetres(_metresPerSecond * duration.TotalSeconds);

    /// <summary>Adds two speeds.</summary>
    public static Speed operator +(Speed a, Speed b) => new(a._metresPerSecond + b._metresPerSecond);

    /// <summary>Subtracts one speed from another.</summary>
    public static Speed operator -(Speed a, Speed b) => new(a._metresPerSecond - b._metresPerSecond);

    /// <summary>Negates a speed.</summary>
    public static Speed operator -(Speed a) => new(-a._metresPerSecond);

    /// <summary>Scales a speed by a dimensionless factor.</summary>
    public static Speed operator *(Speed a, double factor) => new(a._metresPerSecond * factor);

    /// <summary>Scales a speed by a dimensionless factor.</summary>
    public static Speed operator *(double factor, Speed a) => new(a._metresPerSecond * factor);

    /// <summary>Divides a speed by a dimensionless divisor.</summary>
    public static Speed operator /(Speed a, double divisor) => new(a._metresPerSecond / divisor);

    /// <summary>Divides two speeds, yielding their dimensionless ratio.</summary>
    public static double operator /(Speed a, Speed b) => a._metresPerSecond / b._metresPerSecond;

    /// <summary>Indicates whether one speed is slower than another.</summary>
    public static bool operator <(Speed a, Speed b) => a._metresPerSecond < b._metresPerSecond;

    /// <summary>Indicates whether one speed is faster than another.</summary>
    public static bool operator >(Speed a, Speed b) => a._metresPerSecond > b._metresPerSecond;

    /// <summary>Indicates whether one speed is slower than or equal to another.</summary>
    public static bool operator <=(Speed a, Speed b) => a._metresPerSecond <= b._metresPerSecond;

    /// <summary>Indicates whether one speed is faster than or equal to another.</summary>
    public static bool operator >=(Speed a, Speed b) => a._metresPerSecond >= b._metresPerSecond;

    /// <inheritdoc/>
    public int CompareTo(Speed other) => _metresPerSecond.CompareTo(other._metresPerSecond);

    /// <summary>
    /// Returns an invariant-culture string of the form <c>"5 m/s"</c> for
    /// diagnostic purposes. User-facing presentation should go through a
    /// dedicated formatter.
    /// </summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{_metresPerSecond} m/s");
}
