using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Renderers.Mapsui.Avalonia.Tests;

public class S100MapControlTests
{
    private static S100MapsuiOptions IdentityOptions() =>
        new() { CrsTransformFactory = new IdentityCrsTransformFactory() };

    [Fact]
    public void Accessors_throw_before_configure()
    {
        HeadlessTest.Run(() =>
        {
            using var control = new S100MapControl();

            Assert.False(control.IsConfigured);
            Assert.Throws<InvalidOperationException>(() => control.Session);
            Assert.Throws<InvalidOperationException>(() => control.Adapter);
        });
    }

    [Fact]
    public void Configure_attaches_session_and_adapter_over_a_default_map()
    {
        HeadlessTest.Run(() =>
        {
            using var control = new S100MapControl();

            var session = control.Configure(IdentityOptions());

            Assert.True(control.IsConfigured);
            Assert.Same(session, control.Session);
            Assert.NotNull(control.Adapter);
            // Configure created the EPSG:3857 map when the control had none.
            Assert.NotNull(control.Map);
            Assert.Equal("EPSG:3857", control.Map!.CRS);
        });
    }

    [Fact]
    public void Configure_keeps_a_host_supplied_map()
    {
        HeadlessTest.Run(() =>
        {
            var hostMap = new global::Mapsui.Map { CRS = "EPSG:3857" };
            using var control = new S100MapControl { Map = hostMap };

            control.Configure(IdentityOptions());

            Assert.Same(hostMap, control.Map);
        });
    }

    [Fact]
    public void Configure_rejects_a_non_web_mercator_map()
    {
        HeadlessTest.Run(() =>
        {
            using var control = new S100MapControl
            {
                Map = new global::Mapsui.Map { CRS = "EPSG:4326" },
            };

            // The renderer and pick/coordinate adapters only work in Web Mercator,
            // so a conflicting CRS fails fast instead of attaching a broken session.
            Assert.Throws<InvalidOperationException>(() => control.Configure(IdentityOptions()));
            Assert.False(control.IsConfigured);
        });
    }

    [Fact]
    public void Configure_normalizes_an_unset_map_crs()
    {
        HeadlessTest.Run(() =>
        {
            using var control = new S100MapControl { Map = new global::Mapsui.Map() };

            control.Configure(IdentityOptions());

            Assert.Equal("EPSG:3857", control.Map!.CRS);
        });
    }

    [Fact]
    public void Configure_twice_throws()
    {
        HeadlessTest.Run(() =>
        {
            using var control = new S100MapControl();
            control.Configure(IdentityOptions());

            Assert.Throws<InvalidOperationException>(
                () => control.Configure(IdentityOptions()));
        });
    }

    [Fact]
    public void Configure_rejects_null_options()
    {
        HeadlessTest.Run(() =>
        {
            using var control = new S100MapControl();
            Assert.Throws<ArgumentNullException>(() => control.Configure(null!));
        });
    }

    [Fact]
    public void Dispose_before_configure_is_safe()
    {
        HeadlessTest.Run(() =>
        {
            var control = new S100MapControl();
            control.Dispose();
            control.Dispose();
        });
    }

    [Fact]
    public void Dispose_releases_the_attached_session()
    {
        HeadlessTest.Run(() =>
        {
            var control = new S100MapControl();
            var session = control.Configure(IdentityOptions());

            control.Dispose();

            // The control owns the session, so disposing it disposes the session.
            Assert.Throws<ObjectDisposedException>(() => session.GetDatasets());
            // The control presents a self-contained disposed contract: accessors
            // throw and IsConfigured reads false rather than handing back a
            // disposed session/adapter.
            Assert.False(control.IsConfigured);
            Assert.Throws<ObjectDisposedException>(() => control.Session);
            Assert.Throws<ObjectDisposedException>(() => control.Adapter);
        });
    }

    private sealed class IdentityCrsTransformFactory : ICrsTransformFactory
    {
        public ICrsTransform Create(string sourceCrs, string targetCrs) =>
            IdentityCrsTransform.Instance;
    }
}
