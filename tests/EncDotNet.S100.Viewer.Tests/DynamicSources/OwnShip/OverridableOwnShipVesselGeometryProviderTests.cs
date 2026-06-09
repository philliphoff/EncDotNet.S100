using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources.OwnShip;

public sealed class OverridableOwnShipVesselGeometryProviderTests
{
    private sealed class FakeInner : IOwnShipVesselGeometryProvider
    {
        private DynamicVesselGeometry? _current;

        public FakeInner(DynamicVesselGeometry? current) => _current = current;

        public DynamicVesselGeometry? Current => _current;
        public event EventHandler? Changed;

        public void Set(DynamicVesselGeometry? value)
        {
            _current = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static DynamicVesselGeometry Geom(double length, double beam)
        => new() { LengthMetres = length, BeamMetres = beam, BowOffsetMetres = length / 2, PortOffsetMetres = beam / 2 };

    [Fact]
    public void Current_WithoutOverride_DelegatesToInner()
    {
        var inner = new FakeInner(Geom(100, 20));
        using var provider = new OverridableOwnShipVesselGeometryProvider(inner);

        Assert.Equal(100, provider.Current!.LengthMetres);
    }

    [Fact]
    public void SetOverride_MasksInnerAndRaisesChanged()
    {
        var inner = new FakeInner(Geom(100, 20));
        using var provider = new OverridableOwnShipVesselGeometryProvider(inner);

        var raised = 0;
        provider.Changed += (_, _) => raised++;

        provider.SetOverride(Geom(300, 45));

        Assert.Equal(300, provider.Current!.LengthMetres);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetOverride_Null_YieldsNull_NotSettingsGeometry()
    {
        // A target of unknown size must override to "no geometry"
        // (pictogram fallback), not fall back to the configured size.
        var inner = new FakeInner(Geom(100, 20));
        using var provider = new OverridableOwnShipVesselGeometryProvider(inner);

        provider.SetOverride(null);

        Assert.Null(provider.Current);
    }

    [Fact]
    public void ClearOverride_RestoresInnerAndRaisesChanged()
    {
        var inner = new FakeInner(Geom(100, 20));
        using var provider = new OverridableOwnShipVesselGeometryProvider(inner);
        provider.SetOverride(Geom(300, 45));

        var raised = 0;
        provider.Changed += (_, _) => raised++;

        provider.ClearOverride();

        Assert.Equal(100, provider.Current!.LengthMetres);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ClearOverride_WhenNoneActive_IsNoOp()
    {
        var inner = new FakeInner(Geom(100, 20));
        using var provider = new OverridableOwnShipVesselGeometryProvider(inner);

        var raised = 0;
        provider.Changed += (_, _) => raised++;

        provider.ClearOverride();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void InnerChanged_ForwardedOnlyWhenNoOverride()
    {
        var inner = new FakeInner(Geom(100, 20));
        using var provider = new OverridableOwnShipVesselGeometryProvider(inner);

        var raised = 0;
        provider.Changed += (_, _) => raised++;

        // No override: inner change is forwarded.
        inner.Set(Geom(120, 22));
        Assert.Equal(1, raised);

        // Override active: inner change is masked.
        provider.SetOverride(Geom(300, 45)); // raised -> 2
        inner.Set(Geom(130, 23));            // masked
        Assert.Equal(2, raised);
    }
}
