using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tier-1 spec-compliance tests for <see cref="S102PortrayalCatalogue"/>
/// using the bundled S-102 portrayal catalogue and colour profile from
/// <c>EncDotNet.S100.Specifications</c>. These exercise palette loading,
/// the four context parameters (FourShades / Shallow / Safety / Deep),
/// boundary semantics of <c>geLtInterval</c>, NoData → NODTA propagation,
/// and the invariant clamp.
/// </summary>
public sealed class S102PortrayalCatalogueTests : IDisposable
{
    // S-102 Day-palette sRGB values from
    // src/EncDotNet.S100.Specifications/content/S102/pc/ColorProfiles/colorProfile.xml.
    private const string DayDEPVS = "#61B7FF";
    private const string DayDEPMS = "#82CAFF";
    private const string DayDEPMD = "#A7D9FB";
    private const string DayDEPDW = "#C9EDFF";
    private const string DayDEPIT = "#58AF9C";

    // Night-palette sRGB (red=7,green=23,blue=39).
    private const string NightDEPVS = "#071727";

    private readonly MoonSharpLuaEngine _engine = new();
    private readonly IAssetSource _source;
    private readonly PortrayalCatalogueProvider _provider;

    public S102PortrayalCatalogueTests()
    {
        _source = Specification.CreatePortrayalCatalogueSource("S-102");
        _provider = PortrayalCatalogueProvider.OpenAsync(_source).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _source.Dispose();
    }

    private S102PortrayalCatalogue CreateCatalogue() => new(_engine, _provider);

    [Fact]
    public void DayPalette_LoadsAllSixDepthTokens()
    {
        var catalogue = CreateCatalogue();
        catalogue.SwitchPalette(PaletteType.Day);

        foreach (var token in new[] { "DEPDW", "DEPMD", "DEPMS", "DEPVS", "DEPIT", "NODTA" })
        {
            Assert.True(
                catalogue.ActivePalette.TryResolve(token, out var hex) && !string.IsNullOrEmpty(hex),
                $"Day palette must resolve {token}");
        }

        Assert.True(catalogue.ActivePalette.TryResolve("DEPVS", out var depvs));
        Assert.Equal(DayDEPVS, depvs, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuskAndNightPalettes_Load_AndDifferFromDay()
    {
        var catalogue = CreateCatalogue();
        catalogue.SwitchPalette(PaletteType.Day);
        Assert.True(catalogue.ActivePalette.TryResolve("DEPVS", out var dayHex));

        catalogue.SwitchPalette(PaletteType.Dusk);
        Assert.True(catalogue.ActivePalette.TryResolve("DEPVS", out var duskHex));
        Assert.NotEqual(dayHex, duskHex);

        catalogue.SwitchPalette(PaletteType.Night);
        Assert.True(catalogue.ActivePalette.TryResolve("DEPVS", out var nightHex));
        Assert.Equal(NightDEPVS, nightHex, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TwoShade_ProducesThreeBands_AndUsesDayPalette()
    {
        var catalogue = CreateCatalogue();
        var mariner = new MarinerSettings { FourShades = false, SafetyContour = 30.0 };

        var scheme = catalogue.ResolveColorScheme(mariner);

        Assert.Equal("depth", scheme.FieldName);
        Assert.Equal(3, scheme.Bands.Count);

        // Drying / very shallow / deep, all using the bundled Day palette.
        Assert.Equal(DayDEPIT, scheme.Resolve(-1f), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(DayDEPVS, scheme.Resolve(5f), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(DayDEPDW, scheme.Resolve(40f), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FourShade_ProducesFiveBands_AndUsesDayPalette()
    {
        var catalogue = CreateCatalogue();
        var mariner = new MarinerSettings
        {
            FourShades = true,
            ShallowContour = 2.0,
            SafetyContour = 30.0,
            DeepContour = 50.0,
        };

        var scheme = catalogue.ResolveColorScheme(mariner);

        Assert.Equal(5, scheme.Bands.Count);
        Assert.Equal(DayDEPIT, scheme.Resolve(-1f), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(DayDEPVS, scheme.Resolve(1f), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(DayDEPMS, scheme.Resolve(10f), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(DayDEPMD, scheme.Resolve(40f), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(DayDEPDW, scheme.Resolve(100f), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Boundary_DepthEqualSafetyContour_FallsInDeepBand()
    {
        // S-102 bands use geLtInterval (≥ min, < max); a depth that
        // equals the SafetyContour belongs to the deep band, never the
        // shallow band.
        var catalogue = CreateCatalogue();
        var mariner = new MarinerSettings { FourShades = false, SafetyContour = 30.0 };

        var scheme = catalogue.ResolveColorScheme(mariner);

        Assert.Equal(DayDEPDW, scheme.Resolve(30f), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NightPalette_SwitchAppliesToResolvedScheme()
    {
        var catalogue = CreateCatalogue();
        catalogue.SwitchPalette(PaletteType.Night);

        var scheme = catalogue.ResolveColorScheme(new MarinerSettings { FourShades = false, SafetyContour = 30.0 });

        Assert.Equal(NightDEPVS, scheme.Resolve(5f), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoDataColor_IsPopulatedFromActivePaletteNODTA()
    {
        var catalogue = CreateCatalogue();
        catalogue.SwitchPalette(PaletteType.Day);

        var scheme = catalogue.ResolveColorScheme(new MarinerSettings { FourShades = false, SafetyContour = 30.0 });

        Assert.False(string.IsNullOrEmpty(scheme.NoDataColor));
        Assert.True(catalogue.ActivePalette.TryResolve("NODTA", out var nodta));
        Assert.Equal(nodta, scheme.NoDataColor, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvariantClamp_ShallowGreaterThanSafety_DoesNotThrow_AndProducesMonotonicBands()
    {
        var catalogue = CreateCatalogue();

        // Shallow > Safety and Deep < Safety — both clamped.
        var mariner = new MarinerSettings
        {
            FourShades = true,
            ShallowContour = 50.0,
            SafetyContour = 30.0,
            DeepContour = 20.0,
        };

        var scheme = catalogue.ResolveColorScheme(mariner);

        // Band lower bounds (ignoring the intertidal band that is
        // open-ended on the low side) must be non-decreasing.
        var lowerBounds = scheme.Bands
            .Where(b => !float.IsNegativeInfinity(b.MinValue))
            .Select(b => b.MinValue)
            .ToList();

        for (int i = 1; i < lowerBounds.Count; i++)
        {
            Assert.True(lowerBounds[i] >= lowerBounds[i - 1],
                $"Band lower bounds must be monotonic; got {string.Join(",", lowerBounds)}");
        }
    }

    [Fact]
    public void FourShades_IsSourcedFromMarinerSettings_NotCatalogueState()
    {
        // Same catalogue instance, two consecutive renders with
        // different FourShades values — the result must follow the
        // settings, not any persisted catalogue state.
        var catalogue = CreateCatalogue();

        var twoShade = catalogue.ResolveColorScheme(new MarinerSettings
        {
            FourShades = false,
            SafetyContour = 30.0,
        });

        var fourShade = catalogue.ResolveColorScheme(new MarinerSettings
        {
            FourShades = true,
            ShallowContour = 2.0,
            SafetyContour = 30.0,
            DeepContour = 50.0,
        });

        Assert.Equal(3, twoShade.Bands.Count);
        Assert.Equal(5, fourShade.Bands.Count);
    }
}
