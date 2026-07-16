using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Rendering.Scene.Tests;

/// <summary>
/// Tests for the antimeridian seam-wrap in <see cref="WorldToScreen"/> (issue
/// #413). A viewport expressed in a shifted longitude frame
/// (<c>MaxLongitude &gt; 180</c>) must wrap ops on the far side of the ±180°
/// seam into the visible window; a normal viewport must never wrap.
/// </summary>
public sealed class WorldToScreenSeamWrapTests
{
    private const double RadToDeg = 180.0 / Math.PI;

    private static Viewport ShiftedViewport() => new()
    {
        // Frame [175°, 225°] — a contiguous window straddling the seam, as the
        // seam-aware auto-fit produces for the Alaska NWS extent.
        MinLongitude = 175.0,
        MaxLongitude = 225.0,
        MinLatitude = 60.0,
        MaxLatitude = 72.0,
        WidthPixels = 1000,
        HeightPixels = 1000,
        ScaleDenominator = 50_000,
    };

    [Fact]
    public void Op_west_of_seam_wraps_into_the_shifted_window()
    {
        var transform = WorldToScreen.Create(ShiftedViewport());

        // A feature at −135° (= +225° in the shifted frame) sits at the right
        // edge; its raw world-X is negative and would fall far off-screen left
        // without the wrap.
        var world = WebMercator.FromLonLat(-135.0, 66.0);
        var (x, _) = transform.Project(world);

        Assert.InRange(x, 990f, 1010f); // right edge (~1000)
    }

    [Fact]
    public void Op_east_of_seam_projects_near_left_edge()
    {
        var transform = WorldToScreen.Create(ShiftedViewport());

        var world = WebMercator.FromLonLat(175.0, 66.0);
        var (x, _) = transform.Project(world);

        Assert.InRange(x, -5f, 5f); // left edge (~0)
    }

    [Fact]
    public void Normal_viewport_does_not_wrap_offscreen_features()
    {
        // A conventional viewport (within ±180°) must leave an op that lies west
        // of the viewport projecting to a negative pixel X — NOT teleported to
        // the right edge.
        var vp = new Viewport
        {
            MinLongitude = 2.0,
            MaxLongitude = 5.0,
            MinLatitude = 51.0,
            MaxLatitude = 53.0,
            WidthPixels = 1200,
            HeightPixels = 900,
            ScaleDenominator = 50_000,
        };
        var transform = WorldToScreen.Create(vp);

        var world = WebMercator.FromLonLat(1.0, 52.0); // 1° west of the viewport
        var (x, _) = transform.Project(world);

        Assert.True(x < 0f, "A feature west of a normal viewport must stay off the left edge.");
    }

    [Fact]
    public void Shifted_viewport_with_wrap_disabled_does_not_teleport_far_ops()
    {
        // The tiled renderer disables the wrap (allowSeamWrap: false) because it
        // draws already-continuous geometry from narrow per-tile viewports. Under
        // a shifted window an op west of the viewport must project to a negative
        // pixel X — NOT be wrapped +circumference to the right edge (which is what
        // smeared large polygons across eastern tiles before the fix).
        var transform = WorldToScreen.Create(ShiftedViewport(), allowSeamWrap: false);

        // A feature at −135° has a large negative raw world-X. With the wrap on it
        // would land at the right edge (see Op_west_of_seam_wraps_into_the_shifted_window);
        // with the wrap off it must stay far off-screen left.
        var world = WebMercator.FromLonLat(-135.0, 66.0);
        var (x, _) = transform.Project(world);

        Assert.True(x < 0f, "With wrap disabled, a far-west op must not be teleported into the window.");
    }

    [Fact]
    public void Wrap_disabled_matches_wrap_enabled_for_in_window_ops()
    {
        // Disabling the wrap must not disturb ops already inside the shifted
        // window (the common case for a per-tile viewport): both forms project
        // them identically.
        var vp = ShiftedViewport();
        var wrapOn = WorldToScreen.Create(vp, allowSeamWrap: true);
        var wrapOff = WorldToScreen.Create(vp, allowSeamWrap: false);

        var world = WebMercator.FromLonLat(200.0, 66.0); // continuous, inside [175,225]
        var (xOn, yOn) = wrapOn.Project(world);
        var (xOff, yOff) = wrapOff.Project(world);

        Assert.Equal(xOn, xOff, 3);
        Assert.Equal(yOn, yOff, 3);
    }
}
