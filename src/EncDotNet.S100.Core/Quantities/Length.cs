using System;
using System.Globalization;

namespace EncDotNet.S100.Quantities;

/// <summary>
/// A unit-independent linear distance, stored canonically in metres.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Length"/> models a real-world physical distance without
/// committing callers to a particular unit at the API boundary. Values are
/// created through explicit factory methods (<see cref="FromMetres"/>,
/// <see cref="FromFeet"/>, …) and read back through explicit conversion
/// properties (<see cref="TotalMetres"/>, <see cref="TotalFeet"/>, …), so the
/// unit is always visible in the calling code rather than buried in a comment
/// or parameter-name suffix.
/// </para>
/// <para>
/// The design mirrors <see cref="TimeSpan"/>: a lightweight
/// <c>readonly record struct</c> with value semantics, arithmetic operators,
/// and ordering. Conversions use the exact international foot
/// (1 ft = 0.3048 m) and fathom (1 fm = 1.8288 m).
/// </para>
/// <para>
/// Because the backing store is a <see cref="double"/>, equality
/// (<c>==</c>) is exact floating-point equality and is therefore subject to
/// the usual rounding caveats. Prefer <see cref="CompareTo(Length)"/> with a
/// tolerance when comparing computed values.
/// </para>
/// </remarks>
public readonly record struct Length : IComparable<Length>
{
    /// <summary>Metres per international foot (exact).</summary>
    public const double MetresPerFoot = 0.3048;

    /// <summary>Metres per fathom (exact; 1 fathom = 6 international feet).</summary>
    public const double MetresPerFathom = 1.8288;

    /// <summary>Metres per nautical mile (exact).</summary>
    public const double MetresPerNauticalMile = 1852.0;

    /// <summary>Metres per kilometre.</summary>
    public const double MetresPerKilometre = 1000.0;

    private readonly double _metres;

    private Length(double metres) => _metres = metres;

    /// <summary>A zero-length value. Equal to <c>default(Length)</c>.</summary>
    public static Length Zero => default;

    /// <summary>Creates a length from a value in metres.</summary>
    public static Length FromMetres(double metres) => new(metres);

    /// <summary>Creates a length from a value in international feet.</summary>
    public static Length FromFeet(double feet) => new(feet * MetresPerFoot);

    /// <summary>Creates a length from a value in fathoms.</summary>
    public static Length FromFathoms(double fathoms) => new(fathoms * MetresPerFathom);

    /// <summary>Creates a length from a value in nautical miles.</summary>
    public static Length FromNauticalMiles(double nauticalMiles) => new(nauticalMiles * MetresPerNauticalMile);

    /// <summary>Creates a length from a value in kilometres.</summary>
    public static Length FromKilometres(double kilometres) => new(kilometres * MetresPerKilometre);

    /// <summary>The length expressed in metres (the canonical unit).</summary>
    public double TotalMetres => _metres;

    /// <summary>The length expressed in international feet.</summary>
    public double TotalFeet => _metres / MetresPerFoot;

    /// <summary>The length expressed in fathoms.</summary>
    public double TotalFathoms => _metres / MetresPerFathom;

    /// <summary>The length expressed in nautical miles.</summary>
    public double TotalNauticalMiles => _metres / MetresPerNauticalMile;

    /// <summary>The length expressed in kilometres.</summary>
    public double TotalKilometres => _metres / MetresPerKilometre;

    /// <summary>Returns the absolute (non-negative) magnitude of this length.</summary>
    public Length Abs() => new(Math.Abs(_metres));

    /// <summary>Adds two lengths.</summary>
    public static Length operator +(Length a, Length b) => new(a._metres + b._metres);

    /// <summary>Subtracts one length from another.</summary>
    public static Length operator -(Length a, Length b) => new(a._metres - b._metres);

    /// <summary>Negates a length.</summary>
    public static Length operator -(Length a) => new(-a._metres);

    /// <summary>Scales a length by a dimensionless factor.</summary>
    public static Length operator *(Length a, double factor) => new(a._metres * factor);

    /// <summary>Scales a length by a dimensionless factor.</summary>
    public static Length operator *(double factor, Length a) => new(a._metres * factor);

    /// <summary>Divides a length by a dimensionless divisor.</summary>
    public static Length operator /(Length a, double divisor) => new(a._metres / divisor);

    /// <summary>Divides two lengths, yielding their dimensionless ratio.</summary>
    public static double operator /(Length a, Length b) => a._metres / b._metres;

    /// <summary>Indicates whether one length is shorter than another.</summary>
    public static bool operator <(Length a, Length b) => a._metres < b._metres;

    /// <summary>Indicates whether one length is longer than another.</summary>
    public static bool operator >(Length a, Length b) => a._metres > b._metres;

    /// <summary>Indicates whether one length is shorter than or equal to another.</summary>
    public static bool operator <=(Length a, Length b) => a._metres <= b._metres;

    /// <summary>Indicates whether one length is longer than or equal to another.</summary>
    public static bool operator >=(Length a, Length b) => a._metres >= b._metres;

    /// <inheritdoc/>
    public int CompareTo(Length other) => _metres.CompareTo(other._metres);

    /// <summary>
    /// Returns an invariant-culture string of the form <c>"12.5 m"</c> for
    /// diagnostic purposes. User-facing presentation should go through a
    /// dedicated formatter.
    /// </summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{_metres} m");
}
