using EncDotNet.S100.Testing.Scenarios.World;

namespace EncDotNet.S100.Testing.Scenarios.Quirks;

/// <summary>
/// A way real producers' data departs from what the specification says, or
/// from what readers expect, applied to one dataset of a scenario
/// (<see cref="ScenarioDefinition.Quirks"/>).
/// </summary>
/// <remarks>
/// Quirks are data; <see cref="DatasetQuirks"/> interprets them for the
/// writers. A writer that doesn't support a quirk ignores it, so a quirk
/// listed for the wrong product changes nothing. Each quirk names the
/// issue where we met it.
/// </remarks>
public abstract record ProducerQuirk;

/// <summary>
/// Latitude and longitude written in each other's place (#650; the S-411
/// lon-first case is #760). HDF5 grids: the grid origin, spacing and node
/// counts are exchanged; the declared bounding box stays right. HDF5
/// stations: each station's position is exchanged.
/// </summary>
public sealed record SwappedAxisOrder : ProducerQuirk;

/// <summary>
/// The vertical datum an HDF5 file declares (#939): <c>null</c> leaves
/// <c>verticalDatum</c> out, as some producers do; a code declares that
/// datum instead of the world's.
/// </summary>
/// <param name="Code">The S-100 vertical datum code, or <c>null</c> to omit it.</param>
public sealed record DeclaredVerticalDatum(int? Code) : ProducerQuirk;

/// <summary>
/// An HDF5 file declares <paramref name="Epsg"/> as its <c>horizontalCRS</c>
/// while its coordinates stay WGS 84 degrees: a wrong or unusual CRS
/// declaration.
/// </summary>
/// <param name="Epsg">The EPSG code declared.</param>
public sealed record DeclaredHorizontalCrs(int Epsg) : ProducerQuirk;

/// <summary>
/// Longitudes written in the 0…360° frame instead of −180…180° (#773):
/// Synthetic Sound's 135° W becomes 225°. HDF5 grids, stations and bounding
/// boxes, and the catalogue's bounding box. (S-101 can't carry it: at the
/// writer's coordinate factor of 10⁷, 225° overflows a 32-bit coordinate.)
/// </summary>
public sealed record LongitudeFrame0To360 : ProducerQuirk;

/// <summary>
/// The S-101 <c>DataCoverage</c> display scales as written: inverted when
/// <paramref name="Minimum"/> is the finer denominator, or missing when
/// <c>null</c> (<c>DisplayScaleRange.FromDeclared</c>).
/// </summary>
/// <param name="Minimum">The <c>minimumDisplayScale</c> written, or <c>null</c> to omit it.</param>
/// <param name="Maximum">The <c>maximumDisplayScale</c> written, or <c>null</c> to omit it.</param>
public sealed record DeclaredDisplayScales(int? Minimum, int? Maximum) : ProducerQuirk;

/// <summary>
/// Holes of fill value (no data) in an S-102 grid: every node inside one of
/// the rectangles is written as 1 000 000.
/// </summary>
/// <param name="Holes">The rectangles, in metres.</param>
public sealed record NoDataHoles(IReadOnlyList<LocalRect> Holes) : ProducerQuirk;

/// <summary>
/// A gap in S-104 records from <paramref name="From"/> to
/// <paramref name="To"/> inclusive: a grid leaves those steps out (its
/// remaining steps are no longer evenly spaced); a station series, whose
/// times follow from its interval, writes the fill value there.
/// </summary>
/// <param name="From">The first missing record time (UTC).</param>
/// <param name="To">The last missing record time (UTC).</param>
public sealed record RecordGap(DateTime From, DateTime To) : ProducerQuirk;

/// <summary>
/// An S-104 forecast run that ends at <paramref name="LastRecord"/>: no
/// records after it, so later times are past the run (an expired run).
/// </summary>
/// <param name="LastRecord">The last record time (UTC).</param>
public sealed record ForecastRunEnds(DateTime LastRecord) : ProducerQuirk;

/// <summary>
/// An S-101 cell that charts every depth <paramref name="Metres"/> shoaler
/// than the world, as a cell compiled from an older or different survey.
/// Two cells of the same band that disagree this way overlap with
/// conflicting values (#947).
/// </summary>
/// <param name="Metres">How much shoaler, in metres.</param>
public sealed record ShoalBias(double Metres) : ProducerQuirk;

/// <summary>
/// An extra S-101 area of <paramref name="FeatureType"/> (one with an area
/// fill pattern, such as <c>Sandwave</c>) whose exterior ring has a
/// zero-width spike of <paramref name="SpikeLength"/> metres out of the
/// middle of its north edge. Overlay operations snap such a ring to a
/// polygon plus a line: mixed-dimension geometry (#891).
/// </summary>
/// <param name="FeatureType">The S-101 feature type.</param>
/// <param name="Area">The area, in metres.</param>
/// <param name="SpikeLength">The spike's length, in metres.</param>
public sealed record SpikedArea(string FeatureType, LocalRect Area, double SpikeLength) : ProducerQuirk;
