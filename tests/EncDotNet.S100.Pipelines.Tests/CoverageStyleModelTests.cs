using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for the coverage style model changes weather grids need (#1062,
/// design note <c>docs/design/weather-sources.md</c> §8.5 and §8.6): a
/// code-defined <see cref="ICoverageStyle"/>, contours by value, symbol choice
/// by latitude, the rotation offset, and computed fields.
/// </summary>
public sealed class CoverageStyleModelTests
{
    private static readonly SymbolBand NorthernBarb = new() { MinValue = 0f, MaxValue = 100f, SymbolRef = "BARB_N" };
    private static readonly SymbolBand SouthernBarb = new() { MinValue = 0f, MaxValue = 100f, SymbolRef = "BARB_S" };

    [Fact]
    public async Task A_code_defined_style_needs_no_spec_or_edition()
    {
        var style = new CodeDefinedStyle
        {
            Contours =
            [
                new ContourStyle { Value = 1012.0, Unit = "hPa", LineWidth = 1f, Color = "#333333", ShowLabel = true },
            ],
        };
        var source = new UniformSource(rows: 2, cols: 2, ("pressure", 1012f));

        var layer = await new CoveragePipeline().ProcessAsync(
            source, style, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(style.Switched, "The pipeline pre-warms a style whose palette is still empty.");
        Assert.Null(layer.ColorScheme);
        Assert.Equal(1012f, layer.Coverage.GetValues("pressure")[3]);
        var contour = Assert.Single(style.Contours);
        Assert.Equal(1012.0, contour.Value);
        Assert.Equal("hPa", contour.Unit);
    }

    [Theory]
    [InlineData(45.0, "BARB_N")]
    [InlineData(0.0, "BARB_N")]
    [InlineData(-0.5, "BARB_S")]
    [InlineData(-60.0, "BARB_S")]
    public void Southern_hemisphere_bands_are_chosen_by_latitude(double latitude, string expected)
    {
        var scheme = BarbScheme(rotationOffset: 0);

        Assert.Equal(expected, scheme.Resolve(10f, latitude)?.SymbolRef);
    }

    [Theory]
    [InlineData(45.0)]
    [InlineData(-45.0)]
    public void Latitude_is_ignored_without_southern_bands(double latitude)
    {
        var scheme = new CoverageSymbolScheme
        {
            ValueFieldName = "speed",
            RotationFieldName = "direction",
            Bands = [NorthernBarb],
        };

        Assert.Same(NorthernBarb, scheme.Resolve(10f, latitude));
        Assert.Null(scheme.Resolve(150f, latitude));
    }

    [Theory]
    [InlineData(0.0, 90f, 90f)]
    [InlineData(0.0, 359.5f, 359.5f)]
    [InlineData(180.0, 0f, 180f)]
    [InlineData(180.0, 90f, 270f)]
    [InlineData(180.0, 270f, 90f)]
    [InlineData(180.0, 180f, 0f)]
    [InlineData(-90.0, 45f, 315f)]
    public void Rotation_offset_is_added_and_wrapped(double offset, float direction, float expected)
    {
        var scheme = BarbScheme(rotationOffset: offset);

        Assert.Equal(expected, scheme.RotationFor(direction), precision: 4);
    }

    [Fact]
    public void Symbol_field_applies_the_rotation_offset_and_the_hemisphere()
    {
        // Two rows of one cell: 10° N and 10° S, both with waves from 90°.
        var metadata = new GridMetadata
        {
            NumRows = 2,
            NumColumns = 1,
            OriginLatitude = 10.0,
            OriginLongitude = 0.0,
            SpacingLatitudinal = -20.0,
            SpacingLongitudinal = 1.0,
        };
        var layer = new StyledCoverageLayer
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = metadata,
                Values = new Dictionary<string, float[]>
                {
                    ["speed"] = [10f, 10f],
                    ["direction"] = [90f, 90f],
                },
            },
            Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
            SymbolScheme = BarbScheme(rotationOffset: 180),
            NoDataValue = float.NaN,
        };

        var field = CoverageSymbolField.Build(layer, IdentityCrsTransform.Instance);

        Assert.Equal("BARB_N", field.Bands[0]?.SymbolRef);
        Assert.Equal("BARB_S", field.Bands[1]?.SymbolRef);
        Assert.Equal(270f, field.Rotation[0]);
        Assert.Equal(270f, field.Rotation[1]);
    }

    [Fact]
    public void Symbol_field_has_no_position_or_symbol_beyond_the_Mercator_limit()
    {
        // A global grid's pole rows: 90° N, the equator and 90° S.
        var metadata = new GridMetadata
        {
            NumRows = 3,
            NumColumns = 1,
            OriginLatitude = 90.0,
            OriginLongitude = 0.0,
            SpacingLatitudinal = -90.0,
            SpacingLongitudinal = 1.0,
        };
        var layer = new StyledCoverageLayer
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = metadata,
                Values = new Dictionary<string, float[]>
                {
                    ["speed"] = [10f, 10f, 10f],
                    ["direction"] = [90f, 90f, 90f],
                },
            },
            Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
            SymbolScheme = BarbScheme(rotationOffset: 0),
            NoDataValue = float.NaN,
        };

        var field = CoverageSymbolField.Build(layer, IdentityCrsTransform.Instance);

        foreach (int pole in new[] { 0, 2 })
        {
            Assert.True(double.IsNaN(field.X[pole]) && double.IsNaN(field.Y[pole]), $"row {pole}");
            Assert.True(float.IsNaN(field.Scale[pole]));
            Assert.Null(field.Bands[pole]);
        }

        Assert.Equal(0.0, field.Y[1], 6);
        Assert.NotNull(field.Bands[1]);
    }

    [Fact]
    public void Computed_field_is_derived_from_stored_fields_on_first_read()
    {
        int calls = 0;
        var sampled = UvSample(
            u: [3f, 0f, -9999f],
            v: [4f, 2f, 1f],
            new ComputedCoverageField
            {
                Name = "speed",
                Inputs = ["u", "v"],
                Compute = cell =>
                {
                    calls++;
                    return MathF.Sqrt((cell[0] * cell[0]) + (cell[1] * cell[1]));
                },
                NoDataValue = -9999f,
            });

        Assert.Equal(0, calls);
        Assert.True(sampled.HasField("speed"));
        Assert.Equal(["u", "v", "speed"], sampled.FieldNames);

        var speed = sampled.GetValues("speed");

        Assert.Equal([5f, 2f, -9999f], speed);
        Assert.Equal(2, calls);
        Assert.Same(speed, sampled.GetValues("speed"));
        Assert.Equal(2f, sampled.GetField("speed")[0, 1]);
        Assert.Equal(2, calls);
        Assert.False(sampled.Values.ContainsKey("speed"));
    }

    [Fact]
    public void Computed_field_without_a_no_data_value_computes_every_cell()
    {
        var sampled = UvSample(
            u: [float.NaN, 1f, 2f],
            v: [0f, 0f, 0f],
            new ComputedCoverageField
            {
                Name = "isNaN",
                Inputs = ["u"],
                Compute = cell => float.IsNaN(cell[0]) ? 1f : 0f,
            });

        Assert.Equal([1f, 0f, 0f], sampled.GetValues("isNaN"));
    }

    [Fact]
    public void Computed_field_with_a_NaN_no_data_value_skips_NaN_inputs()
    {
        var sampled = UvSample(
            u: [float.NaN, 1f, 2f],
            v: [0f, 0f, 0f],
            new ComputedCoverageField
            {
                Name = "double",
                Inputs = ["u"],
                Compute = cell => cell[0] * 2f,
                NoDataValue = float.NaN,
            });

        var values = sampled.GetValues("double");

        Assert.True(float.IsNaN(values[0]));
        Assert.Equal([2f, 4f], values[1..]);
    }

    [Fact]
    public void Unknown_fields_and_inputs_throw()
    {
        var sampled = UvSample(
            u: [1f, 1f, 1f],
            v: [1f, 1f, 1f],
            new ComputedCoverageField { Name = "broken", Inputs = ["w"], Compute = cell => cell[0] });

        Assert.False(sampled.HasField("nope"));
        Assert.Throws<KeyNotFoundException>(() => sampled.GetValues("nope"));
        var ex = Assert.Throws<KeyNotFoundException>(() => sampled.GetValues("broken"));
        Assert.Contains("'w'", ex.Message);
    }

    private static CoverageSymbolScheme BarbScheme(double rotationOffset) => new()
    {
        ValueFieldName = "speed",
        RotationFieldName = "direction",
        Bands = [NorthernBarb],
        SouthernHemisphereBands = [SouthernBarb],
        RotationOffsetDegrees = rotationOffset,
    };

    private static SampledCoverage UvSample(float[] u, float[] v, ComputedCoverageField computed) => new()
    {
        Region = GridRegion.Full,
        Metadata = new GridMetadata
        {
            NumRows = 1,
            NumColumns = u.Length,
            OriginLatitude = 0,
            OriginLongitude = 0,
            SpacingLatitudinal = 1,
            SpacingLongitudinal = 1,
        },
        Values = new Dictionary<string, float[]> { ["u"] = u, ["v"] = v },
        ComputedFields = [computed],
    };

    private sealed class CodeDefinedStyle : ICoverageStyle
    {
        public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

        public bool Switched { get; private set; }

        public required IReadOnlyList<ContourStyle> Contours { get; init; }

        public ValueTask SwitchPaletteAsync(PaletteType type, CancellationToken cancellationToken = default)
        {
            Switched = true;
            ActivePalette = ColorPalette.FromType(type);
            return ValueTask.CompletedTask;
        }

        public CoverageColorScheme? ResolveColorScheme(MarinerSettings settings) => null;
    }

    private sealed class UniformSource(int rows, int cols, (string Name, float Value) value) : ICoverageSource
    {
        private readonly GridMetadata _grid = new()
        {
            NumRows = rows,
            NumColumns = cols,
            OriginLatitude = 0,
            OriginLongitude = 0,
            SpacingLatitudinal = 1,
            SpacingLongitudinal = 1,
        };

        public CoverageMetadata Metadata => new()
        {
            // Non-S-100 product names arrive with #1061; the style itself carries no spec.
            Spec = new SpecRef("S-102", default),
            NativeExtent = new BoundingBox(0, 0, rows, cols),
            GridMetadata = _grid,
            HorizontalCRS = "EPSG:4326",
            VerticalDatum = "MSL",
            NoDataValue = float.NaN,
            ValueFields =
            [
                new CoverageValueField { Name = value.Name, Type = CoverageValueType.Float, Units = "hPa", FillValue = float.NaN },
            ],
        };

        public IReadOnlyList<DateTime> AvailableTimes => [];

        public void SelectTime(DateTime time) { }

        public SampledCoverage Sample(GridRegion region, CancellationToken cancellationToken = default) => new()
        {
            Region = region,
            Metadata = _grid,
            Values = new Dictionary<string, float[]>
            {
                [value.Name] = Enumerable.Repeat(value.Value, rows * cols).ToArray(),
            },
        };
    }
}
