using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.S102;

/// <summary>
/// S-102 portrayal catalogue implementing the IHO depth shading rules
/// from the official S-102 Portrayal Catalogue (BathymetryCoverage.lua).
/// Supports both two-shade and four-shade depth colour schemes.
/// </summary>
public class S102PortrayalCatalogue : ICoveragePortrayalCatalogue
{
    public string ProductSpec => "S-102";
    public string Edition => "3.0.0";
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    /// <summary>Whether to use four depth shading bands (true) or two (false).</summary>
    public bool FourShades { get; set; } = true;

    public void SwitchPalette(PaletteType type)
    {
        ActivePalette = ColorPalette.FromType(type);
    }

    // IHO S-52 Day palette sRGB colours for depth tokens
    // From: S-102-Portrayal-Catalogue/PortrayalCatalog/ColorProfiles/colorProfile.xml
    public static string DEPIT => "#58AF9C";  // Intertidal: rgb(88,175,156)
    public static string DEPVS => "#61B7FF";  // Very shallow: rgb(97,183,255)
    public static string DEPMS => "#82CAFF";  // Medium shallow: rgb(130,202,255)
    public static string DEPMD => "#A7D9FB";  // Medium deep: rgb(167,217,251)
    public static string DEPDW => "#C9EDFF";  // Deep water: rgb(201,237,255)

    public CoverageColorScheme ResolveColorScheme(NavigationContext context)
    {
        // BathymetryCoverage.lua lookup logic:
        //   Intertidal:     depth < 0
        //   Four-shade mode:
        //     DEPVS: [0, ShallowContour)
        //     DEPMS: [ShallowContour, SafetyContour)
        //     DEPMD: [SafetyContour, DeepContour)
        //     DEPDW: [DeepContour, +inf)
        //   Two-shade mode:
        //     DEPVS: [0, SafetyContour)
        //     DEPDW: [SafetyContour, +inf)

        var bands = new List<ColorBand>
        {
            new() { MinValue = float.MinValue, MaxValue = 0f, Color = DEPIT, Label = "Intertidal" },
        };

        if (FourShades)
        {
            bands.Add(new() { MinValue = 0f, MaxValue = (float)context.ShallowContour, Color = DEPVS, Label = "Shallow Water" });
            bands.Add(new() { MinValue = (float)context.ShallowContour, MaxValue = (float)context.SafetyContour, Color = DEPMS, Label = "Medium-Shallow Water" });
            bands.Add(new() { MinValue = (float)context.SafetyContour, MaxValue = (float)context.DeepContour, Color = DEPMD, Label = "Medium-Deep Water" });
            bands.Add(new() { MinValue = (float)context.DeepContour, MaxValue = float.MaxValue, Color = DEPDW, Label = "Deep Water" });
        }
        else
        {
            bands.Add(new() { MinValue = 0f, MaxValue = (float)context.SafetyContour, Color = DEPVS, Label = "Shallow Water" });
            bands.Add(new() { MinValue = (float)context.SafetyContour, MaxValue = float.MaxValue, Color = DEPDW, Label = "Deep Water" });
        }

        return new CoverageColorScheme { FieldName = "depth", Bands = bands };
    }

    public IReadOnlyList<ContourStyle> Contours => [];
}