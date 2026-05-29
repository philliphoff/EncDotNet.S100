using EncDotNet.S100.Viewer;
using EncDotNet.S100.Viewer.Services.DynamicSources.Ais;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

public class AisOverlayFactoryTests
{
    [Fact]
    public void Returns_disabled_when_settings_null()
    {
        var src = AisOverlayServiceCollectionExtensions.BuildSource(null, loggerFactory: null);
        Assert.IsType<DisabledAisFeatureSource>(src);
    }

    [Fact]
    public void Returns_disabled_when_overlay_disabled()
    {
        var src = AisOverlayServiceCollectionExtensions.BuildSource(
            new AisOverlaySettings { Enabled = false },
            loggerFactory: null);
        Assert.IsType<DisabledAisFeatureSource>(src);
    }

    [Fact]
    public void Returns_disabled_when_env_var_unset()
    {
        var envVarName = "AIS_OVERLAY_TEST_KEY_THAT_DOES_NOT_EXIST_" + Guid.NewGuid().ToString("N");
        var src = AisOverlayServiceCollectionExtensions.BuildSource(
            new AisOverlaySettings { Enabled = true, ApiKeyEnvironmentVariable = envVarName },
            loggerFactory: null);
        Assert.IsType<DisabledAisFeatureSource>(src);
    }

    [Fact]
    public void Returns_disabled_when_env_var_blank()
    {
        var envVarName = "AIS_OVERLAY_TEST_KEY_BLANK_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(envVarName, "   ");
        try
        {
            var src = AisOverlayServiceCollectionExtensions.BuildSource(
                new AisOverlaySettings { Enabled = true, ApiKeyEnvironmentVariable = envVarName },
                loggerFactory: null);
            Assert.IsType<DisabledAisFeatureSource>(src);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public async Task DisabledSource_emits_no_features_and_disposes_cleanly()
    {
        var src = new DisabledAisFeatureSource();
        Assert.Empty(src.CurrentFeatures);
        Assert.Equal("vessel.ais", src.Metadata.RendererKey);
        await src.DisposeAsync();
    }
}
