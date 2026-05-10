using EncDotNet.S100.Core;

namespace EncDotNet.S100.Pipelines;

public interface IPortrayalCatalogue
{
    /// <summary>The product specification (name + edition) this catalogue targets.</summary>
    SpecRef Spec { get; }

    /// <summary>The edition of the underlying portrayal catalogue (matches <see cref="PortrayalCatalogue.Version"/>).</summary>
    string Edition { get; }
    ColorPalette ActivePalette { get; }
    void SwitchPalette(PaletteType type);
}
 