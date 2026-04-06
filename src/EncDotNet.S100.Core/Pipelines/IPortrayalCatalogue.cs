namespace EncDotNet.S100.Pipelines;

public interface IPortrayalCatalogue
{
string ProductSpec { get; }
    string Edition { get; }
    ColorPalette ActivePalette { get; }
    void SwitchPalette(PaletteType type);
}
 