using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Core.Tests.DynamicSources;

public class DynamicVesselGeometryTests
{
    [Fact]
    public void Record_RequiresAllFourDimensions()
    {
        var g = new DynamicVesselGeometry
        {
            LengthMetres = 1, BeamMetres = 1,
            BowOffsetMetres = 0, PortOffsetMetres = 0,
        };
        Assert.Equal(1, g.LengthMetres);
        Assert.Equal(1, g.BeamMetres);
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        var a = new DynamicVesselGeometry
        {
            LengthMetres = 50, BeamMetres = 10,
            BowOffsetMetres = 25, PortOffsetMetres = 5,
        };
        var b = new DynamicVesselGeometry
        {
            LengthMetres = 50, BeamMetres = 10,
            BowOffsetMetres = 25, PortOffsetMetres = 5,
        };
        Assert.Equal(a, b);
    }

    [Fact]
    public void DynamicFeature_CarriesOptionalVesselGeometrySidecar()
    {
        var g = new DynamicVesselGeometry
        {
            LengthMetres = 100, BeamMetres = 20,
            BowOffsetMetres = 50, PortOffsetMetres = 10,
        };
        var f = new DynamicFeature
        {
            Id = "ownship", GeometryType = GeometryType.Point,
            Coordinates = new[] { (0.0, 0.0) },
            VesselGeometry = g,
            LastUpdated = DateTimeOffset.UtcNow,
        };

        Assert.Same(g, f.VesselGeometry);
    }

    [Fact]
    public void DynamicFeature_VesselGeometry_DefaultsToNull()
    {
        var f = new DynamicFeature
        {
            Id = "x", GeometryType = GeometryType.Point,
            Coordinates = new[] { (0.0, 0.0) },
            LastUpdated = DateTimeOffset.UtcNow,
        };
        Assert.Null(f.VesselGeometry);
    }
}
