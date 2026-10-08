using Avalonia;
using Avalonia.Controls;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// #825: first-run sizing and restore of the remembered main-window
/// placement against the screens connected now.
/// </summary>
public sealed class WindowPlacementPolicyTests
{
    // 1920x1080 primary with a 40 px taskbar, 1:1.
    private static readonly PlacementScreen Desktop =
        new(new PixelRect(0, 0, 1920, 1040), 1.0, IsPrimary: true);

    // A 2x laptop panel (1512x944 DIPs of working area) to the right.
    private static readonly PlacementScreen Retina =
        new(new PixelRect(1920, 0, 3024, 1888), 2.0, IsPrimary: false);

    private static PlacementResult Resolve(WindowPlacement? saved, params PlacementScreen[] screens) =>
        WindowPlacementPolicy.Resolve(saved, screens) ?? throw new Xunit.Sdk.XunitException("expected a placement");

    [Fact]
    public void No_screens_keeps_the_xaml_defaults()
    {
        Assert.Null(WindowPlacementPolicy.Resolve(null, []));
    }

    [Fact]
    public void First_run_covers_80_percent_of_the_primary_working_area_centred()
    {
        var result = Resolve(null, Retina, Desktop);

        Assert.Equal(new Size(1536, 832), result.Size);
        Assert.Equal(new PixelPoint(192, 104), result.Position);
        Assert.Equal(WindowState.Normal, result.State);
    }

    [Fact]
    public void First_run_size_is_in_dips_on_a_scaled_screen()
    {
        var result = WindowPlacementPolicy.FirstRun(Retina);

        // 80% of 1512x944 DIPs, centred in physical pixels.
        Assert.Equal(new Size(1209, 755), result.Size);
        Assert.Equal(new PixelPoint(1920 + (3024 - 2418) / 2, (1888 - 1510) / 2), result.Position);
    }

    [Fact]
    public void First_run_never_goes_below_the_preferred_minimum_when_it_fits()
    {
        // 80% of 1400x850 is 1120x680 — height is lifted to 700.
        var screen = new PlacementScreen(new PixelRect(0, 0, 1400, 850), 1.0, true);

        var result = WindowPlacementPolicy.FirstRun(screen);

        Assert.Equal(new Size(1120, 700), result.Size);
        Assert.Equal(WindowState.Normal, result.State);
    }

    [Fact]
    public void Small_screens_open_maximized_with_normal_bounds_that_fit()
    {
        var screen = new PlacementScreen(new PixelRect(0, 0, 1280, 760), 1.0, true);

        var result = WindowPlacementPolicy.FirstRun(screen);

        Assert.Equal(WindowState.Maximized, result.State);
        Assert.Equal(new Size(1100, 700), result.Size);
        Assert.True(result.Position.X >= 0 && result.Position.Y >= 0);
    }

    [Fact]
    public void Saved_bounds_on_a_connected_screen_are_restored_exactly()
    {
        var saved = new WindowPlacement { X = 100, Y = 50, Width = 1400, Height = 900 };

        var result = Resolve(saved, Desktop, Retina);

        Assert.Equal(new PixelPoint(100, 50), result.Position);
        Assert.Equal(new Size(1400, 900), result.Size);
        Assert.Equal(WindowState.Normal, result.State);
    }

    [Fact]
    public void Saved_maximized_state_is_restored_with_its_normal_bounds()
    {
        var saved = new WindowPlacement { X = 2000, Y = 100, Width = 1000, Height = 700, IsMaximized = true };

        var result = Resolve(saved, Desktop, Retina);

        Assert.Equal(WindowState.Maximized, result.State);
        Assert.Equal(new PixelPoint(2000, 100), result.Position);
        Assert.Equal(new Size(1000, 700), result.Size);
    }

    [Fact]
    public void Window_saved_on_a_disconnected_screen_falls_back_to_first_run()
    {
        // Saved on the retina panel, which is no longer attached.
        var saved = new WindowPlacement { X = 2200, Y = 200, Width = 1200, Height = 800 };

        var result = Resolve(saved, Desktop);

        Assert.Equal(WindowPlacementPolicy.FirstRun(Desktop), result);
    }

    [Fact]
    public void Off_screen_fallback_keeps_the_maximized_state()
    {
        var saved = new WindowPlacement { X = -5000, Y = -5000, Width = 1200, Height = 800, IsMaximized = true };

        var result = Resolve(saved, Desktop);

        Assert.Equal(WindowState.Maximized, result.State);
        Assert.Equal(WindowPlacementPolicy.FirstRun(Desktop).Size, result.Size);
    }

    [Fact]
    public void A_sliver_left_on_screen_is_not_enough_to_trust_the_saved_bounds()
    {
        // Only 100 px of the window's width overlaps the desktop.
        var saved = new WindowPlacement { X = 1820, Y = 100, Width = 1000, Height = 700 };

        var result = Resolve(saved, Desktop);

        Assert.Equal(WindowPlacementPolicy.FirstRun(Desktop), result);
    }

    [Fact]
    public void Partly_off_screen_window_is_slid_fully_onto_its_screen()
    {
        var saved = new WindowPlacement { X = 1200, Y = -100, Width = 1000, Height = 700 };

        var result = Resolve(saved, Desktop);

        Assert.Equal(new PixelPoint(920, 0), result.Position);
        Assert.Equal(new Size(1000, 700), result.Size);
    }

    [Fact]
    public void Window_larger_than_a_lower_resolution_screen_is_shrunk_to_fit()
    {
        // Saved at 2400x1300 on a bigger display; now only 1920x1040 is available.
        var saved = new WindowPlacement { X = 0, Y = 0, Width = 2400, Height = 1300 };

        var result = Resolve(saved, Desktop);

        Assert.Equal(new Size(1920, 1040), result.Size);
        Assert.Equal(new PixelPoint(0, 0), result.Position);
    }

    [Fact]
    public void Saved_dip_size_is_measured_with_the_target_screens_scale()
    {
        // 1500x900 DIPs on the 2x panel is 3000x1800 px — inside its 3024x1888 working area.
        var saved = new WindowPlacement { X = 1930, Y = 40, Width = 1500, Height = 900 };

        var result = Resolve(saved, Desktop, Retina);

        Assert.Equal(new Size(1500, 900), result.Size);
        Assert.Equal(new PixelPoint(1930, 40), result.Position);
    }

    [Fact]
    public void Window_spanning_two_screens_is_placed_on_the_one_it_overlaps_most()
    {
        // Mostly on the desktop, its right edge running onto the retina panel.
        var saved = new WindowPlacement { X = 1000, Y = 100, Width = 1100, Height = 700 };

        var result = Resolve(saved, Desktop, Retina);

        Assert.Equal(new PixelPoint(820, 100), result.Position);
        Assert.Equal(new Size(1100, 700), result.Size);
    }

    [Theory]
    [InlineData(0, 700)]
    [InlineData(1100, -1)]
    [InlineData(double.NaN, 700)]
    public void Invalid_saved_sizes_are_ignored(double width, double height)
    {
        var saved = new WindowPlacement { X = 100, Y = 100, Width = width, Height = height };

        Assert.Equal(WindowPlacementPolicy.FirstRun(Desktop), Resolve(saved, Desktop));
    }

    [Fact]
    public void Zero_scaling_is_treated_as_one_to_one()
    {
        var screen = Desktop with { Scaling = 0 };

        Assert.Equal(WindowPlacementPolicy.FirstRun(Desktop), WindowPlacementPolicy.FirstRun(screen));
    }

    [Fact]
    public void Placement_round_trips_through_the_settings_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewer-placement-{Guid.NewGuid():N}.json");
        try
        {
            var settings = ViewerSettings.Load(path);
            Assert.Null(settings.MainWindowPlacement);

            settings.MainWindowPlacement = new WindowPlacement { X = -1200, Y = 30, Width = 1500, Height = 950, IsMaximized = true };
            settings.Save();

            var reloaded = ViewerSettings.Load(path).MainWindowPlacement;
            Assert.NotNull(reloaded);
            Assert.Equal((-1200, 30, 1500d, 950d, true),
                (reloaded.X, reloaded.Y, reloaded.Width, reloaded.Height, reloaded.IsMaximized));
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }
}
