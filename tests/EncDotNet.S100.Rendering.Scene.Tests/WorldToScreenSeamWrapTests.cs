using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Rendering.Scene;

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
}
