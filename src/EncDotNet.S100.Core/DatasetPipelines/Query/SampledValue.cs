using System.ComponentModel;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

/// <summary>
/// Discriminated payload of a coverage sample, returned by an
/// <see cref="ICoverageSampler"/> and by the <c>sample_coverage</c> tool.
/// </summary>
/// <remarks>
/// The S-102, S-104 and S-111 shapes (<c>DepthSample</c>,
/// <c>WaterLevelSample</c> and the others, in <c>EncDotNet.S100.Datasets.Pipelines</c>)
/// are kept for their existing callers. A product without a shape of its own
/// returns a <see cref="CoverageFieldsSample"/>. Every variant describes its
/// values, with their quantities and units, through <see cref="GetFields"/>,
/// so a caller can read any product's sample without knowing its shape.
/// </remarks>
public abstract record SampledValue
{
    /// <summary>
    /// The sampled values with their quantities and units, in a fixed order
    /// per product. A method rather than a property so the typed variants'
    /// JSON is unchanged.
    /// </summary>
    public abstract IReadOnlyList<SampledField> GetFields();
}

/// <summary>One sampled value with its quantity and unit.</summary>
/// <param name="Name">
/// The product's name for the field — its S-100 attribute code where it has
/// one (<c>depth</c>, <c>waterLevelHeight</c>, <c>surfaceCurrentSpeed</c>).
/// Unique within a sample.
/// </param>
/// <param name="Quantity">
/// What the value measures, independent of the product, in snake case
/// (<c>depth</c>, <c>water_level_height</c>, <c>current_speed</c>,
/// <c>significant_wave_height</c>, <c>wind_speed</c>), so a caller can find,
/// say, a speed in any product.
/// </param>
/// <param name="Unit">
/// Unit symbol of <paramref name="Value"/>: <c>m</c>, <c>m/s</c>, <c>kn</c>,
/// <c>deg</c> (degrees from true north, clockwise) and so on; <c>null</c> for a
/// unitless or categorical value.
/// </param>
/// <param name="Value">The numeric value, or <c>null</c> for a categorical value or one the cell does not carry.</param>
/// <param name="Text">The value of a categorical field (e.g. an S-104 trend), or <c>null</c>.</param>
public sealed record SampledField(
    [property: Description("The product's name for the field (its S-100 attribute code where it has one, e.g. \"waterLevelHeight\"); unique within a sample.")] string Name,
    [property: Description("What the value measures, independent of the product, in snake case (e.g. \"depth\", \"current_speed\", \"significant_wave_height\").")] string Quantity,
    [property: Description("Unit symbol of the value (e.g. \"m\", \"m/s\", \"kn\", \"deg\" for degrees from true north); null for a unitless or categorical value.")] string? Unit,
    [property: Description("Numeric value; null for a categorical value or one the cell does not carry.")] double? Value,
    [property: Description("Value of a categorical field (e.g. an S-104 trend); null otherwise.")] string? Text = null);

/// <summary>
/// The generic sample shape: a list of <see cref="SampledField"/>s plus where
/// and when they were read. Returned by a product that has no typed shape of
/// its own, so its values reach MCP callers with no tool changes. Written with
/// the JSON <c>$kind</c> <c>"fields"</c>.
/// </summary>
/// <param name="Fields">The sampled values with their quantities and units.</param>
/// <param name="SampleTime">UTC instant of the time step read; <c>null</c> for a product with no time dimension.</param>
/// <param name="RequestedTime">The time the caller asked for, or <c>null</c> if unspecified.</param>
/// <param name="Row">Zero-based row of the grid cell read; <c>null</c> for a station.</param>
/// <param name="Column">Zero-based column of the grid cell read; <c>null</c> for a station.</param>
/// <param name="CellCentreLatitude">Latitude of the cell centre read; <c>null</c> for a station.</param>
/// <param name="CellCentreLongitude">Longitude of the cell centre read; <c>null</c> for a station.</param>
/// <param name="StationId">Identifier of the station read; <c>null</c> for a grid.</param>
/// <param name="StationDistanceMetres">Great-circle distance from the requested point to the station; <c>null</c> for a grid.</param>
/// <param name="StationLatitude">Latitude of the station; <c>null</c> for a grid.</param>
/// <param name="StationLongitude">Longitude of the station; <c>null</c> for a grid.</param>
public sealed record CoverageFieldsSample(
    [property: Description("The sampled values, each with its name, quantity, unit and value.")] IReadOnlyList<SampledField> Fields,
    [property: Description("UTC instant of the time step actually selected; null for a product with no time dimension.")] DateTime? SampleTime = null,
    [property: Description("UTC instant the caller asked for, or null if unspecified.")] DateTimeOffset? RequestedTime = null,
    [property: Description("Zero-based row index of the resolved cell in the source grid; null for a station.")] int? Row = null,
    [property: Description("Zero-based column index of the resolved cell in the source grid; null for a station.")] int? Column = null,
    [property: Description("Latitude of the resolved cell centre, decimal degrees, WGS-84; null for a station.")] double? CellCentreLatitude = null,
    [property: Description("Longitude of the resolved cell centre, decimal degrees, WGS-84; null for a station.")] double? CellCentreLongitude = null,
    [property: Description("Identifier of the station read; null for a grid.")] string? StationId = null,
    [property: Description("Great-circle distance from the requested point to the station, in metres; null for a grid.")] double? StationDistanceMetres = null,
    [property: Description("Latitude of the station, decimal degrees, WGS-84; null for a grid.")] double? StationLatitude = null,
    [property: Description("Longitude of the station, decimal degrees, WGS-84; null for a grid.")] double? StationLongitude = null) : SampledValue
{
    /// <inheritdoc />
    public override IReadOnlyList<SampledField> GetFields() => Fields;
}

/// <summary>
/// Where a requested instant fell relative to the sampled dataset's time
/// range, as reported by <see cref="CoverageSampling"/> and the
/// <c>sample_coverage</c> tool's <c>timeStatus</c>.
/// </summary>
public static class SampleTimeStatus
{
    /// <summary>The requested time is within the dataset's range (allowing one time-step interval at either end).</summary>
    public const string InRange = "in_range";

    /// <summary>The requested time precedes the dataset's first step; the first step was sampled.</summary>
    public const string BeforeStart = "before_start";

    /// <summary>The requested time follows the dataset's last step; the last step was sampled.</summary>
    public const string AfterEnd = "after_end";
}
