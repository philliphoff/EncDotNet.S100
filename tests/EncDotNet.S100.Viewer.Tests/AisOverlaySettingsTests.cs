using System.IO;
using EncDotNet.S100.Viewer;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// PR-AIS-zoom-gated: round-trip the
/// <see cref="AisOverlaySettings.ActivationViewportSpanDegrees"/>
/// property through <see cref="ViewerSettings"/> JSON persistence.
/// </summary>
public class AisOverlaySettingsTests
{
    [Fact]
    public void Default_ActivationViewportSpanDegrees_is_50()
    {
        var settings = new AisOverlaySettings();
        Assert.Equal(50.0, settings.ActivationViewportSpanDegrees);
    }

    [Fact]
    public void Default_can_be_cleared_to_null()
    {
        var settings = new AisOverlaySettings { ActivationViewportSpanDegrees = null };
        Assert.Null(settings.ActivationViewportSpanDegrees);
    }

    [Fact]
    public void RoundTrips_through_settings_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewer-settings-{Path.GetRandomFileName()}.json");
        try
        {
            var s = new ViewerSettings
            {
                SettingsFilePath = path,
                AisOverlay = new AisOverlaySettings
                {
                    Enabled = true,
                    ActivationViewportSpanDegrees = 12.5,
                },
            };
            s.Save();

            var loaded = ViewerSettings.Load(path);
            Assert.NotNull(loaded.AisOverlay);
            Assert.Equal(12.5, loaded.AisOverlay!.ActivationViewportSpanDegrees);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Null_round_trips_as_null()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewer-settings-{Path.GetRandomFileName()}.json");
        try
        {
            var s = new ViewerSettings
            {
                SettingsFilePath = path,
                AisOverlay = new AisOverlaySettings
                {
                    Enabled = true,
                    ActivationViewportSpanDegrees = null,
                },
            };
            s.Save();

            var loaded = ViewerSettings.Load(path);
            Assert.Null(loaded.AisOverlay!.ActivationViewportSpanDegrees);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
