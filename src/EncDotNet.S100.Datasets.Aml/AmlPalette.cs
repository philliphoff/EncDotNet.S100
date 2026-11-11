using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Datasets.Aml;

/// <summary>
/// The colour tokens of the AML portrayal: each family's ink
/// (<see cref="AmlFamily.ColorToken"/>) and the halo.
/// </summary>
public static class AmlPalette
{
    /// <summary>The name of the palette.</summary>
    public const string Name = "AML";

    /// <summary>
    /// The halo under every AML line, symbol frame and label, in the palette's
    /// background: white by Day, black at Dusk and Night (decision 15).
    /// </summary>
    public const string HaloToken = "AMLHALO";

    /// <summary>The halo's colours.</summary>
    public static PaletteColors HaloColors { get; } = PaletteColors.FromHex("#FFFFFF", "#000000", "#000000");

    /// <summary>Builds the palette of <paramref name="families"/> and the halo.</summary>
    /// <param name="families">The families.</param>
    public static ApplicationPalette Build(IEnumerable<AmlFamily> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        var tokens = new Dictionary<string, PaletteColors>(StringComparer.Ordinal) { [HaloToken] = HaloColors };
        foreach (var family in families)
        {
            if (tokens.TryGetValue(family.ColorToken, out var existing) && existing != family.Colors)
                throw new ArgumentException($"Two AML families use colour token '{family.ColorToken}' with different colours.", nameof(families));
            tokens[family.ColorToken] = family.Colors;
        }

        return new ApplicationPalette(Name, tokens);
    }

    /// <summary>The palette of the four families of the handoff (<see cref="AmlFamily.All"/>) and the halo.</summary>
    public static ApplicationPalette Default { get; } = Build(AmlFamily.All);
}
