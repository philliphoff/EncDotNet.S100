using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Datasets.Aml;

/// <summary>
/// One of the AML colour families (AML design handoff decision 16): the ink
/// every line, tick, symbol and label of its classes is drawn in, in the Day,
/// Dusk and Night palettes, and the sub-layer its classes draw on.
/// </summary>
/// <remarks>
/// <para>
/// The four families of the handoff are <see cref="Air"/>,
/// <see cref="Operational"/>, <see cref="Seabed"/> and
/// <see cref="Environmental"/>. A family isn't tied to an AML product: RAL
/// draws in air and operational, CLB and LBO both in seabed.
/// </para>
/// <para>
/// Operational leans brick red and is never solid, so it stays apart from the
/// chart's danger magenta and light red. Night keeps each hue and drops its
/// luminance to about the chart's Night text level.
/// </para>
/// </remarks>
public sealed record AmlFamily
{
    /// <summary>Creates a family.</summary>
    /// <param name="key">The family's key, lower case, such as <c>"air"</c>: the end of its sub-layer key and part of its symbol names.</param>
    /// <param name="name">The family's name for people, such as "Air".</param>
    /// <param name="colorToken">The palette token of its ink, such as <c>AMLAIR</c>.</param>
    /// <param name="colors">The ink in Day, Dusk and Night.</param>
    /// <param name="order">
    /// Its order on the Additional military layers plane: higher draws on top.
    /// Also its sub-layer's <see cref="EncDotNet.S100.Datasets.Pipelines.Portrayal.VectorSubLayer.WithinPlanePriority"/>.
    /// </param>
    /// <param name="viewingGroup">The viewing group of its instructions.</param>
    public AmlFamily(string key, string name, string colorToken, PaletteColors colors, int order, int viewingGroup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorToken);
        if (key.Any(c => !char.IsAsciiLetterLower(c)))
            throw new ArgumentException("A family key is lower-case ASCII letters.", nameof(key));
        Key = key;
        Name = name;
        ColorToken = colorToken;
        Colors = colors;
        Order = order;
        ViewingGroup = viewingGroup;
    }

    /// <summary>The family's key, such as <c>"air"</c>.</summary>
    public string Key { get; }

    /// <summary>The family's name for people, such as "Air".</summary>
    public string Name { get; }

    /// <summary>The palette token of the family's ink, such as <c>AMLAIR</c>.</summary>
    public string ColorToken { get; }

    /// <summary>The family's ink in Day, Dusk and Night.</summary>
    public PaletteColors Colors { get; }

    /// <summary>The family's order on the plane: higher draws on top.</summary>
    public int Order { get; }

    /// <summary>The viewing group of the family's instructions.</summary>
    public int ViewingGroup { get; }

    /// <summary>The key of the family's sub-layer: <c>"aml."</c> and <see cref="Key"/>, such as <c>"aml.air"</c>.</summary>
    public string LayerKey => "aml." + Key;

    /// <summary>
    /// The upper-case stem of the family's symbol and line-style names:
    /// <c>AML_</c> and the key, such as <c>AML_AIR</c>.
    /// </summary>
    public string SymbolStem => "AML_" + Key.ToUpperInvariant();

    /// <summary>Air: ATS routes, waypoints, NAVAIDs and airspace. Blue.</summary>
    public static AmlFamily Air { get; } = new(
        "air", "Air", "AMLAIR", PaletteColors.FromHex("#2350B5", "#3E66C2", "#33508F"), order: 40, viewingGroup: 45040);

    /// <summary>Operational: practice and patrol areas, Q-routes and turning points, maritime zones. Brick red.</summary>
    public static AmlFamily Operational { get; } = new(
        "operational", "Operational", "AMLOPS", PaletteColors.FromHex("#B2381B", "#B44A2C", "#8A3B27"), order: 30, viewingGroup: 45030);

    /// <summary>Seabed: contacts, mines, wrecks and bathymetry (SBO, LBO, CLB). Brown.</summary>
    public static AmlFamily Seabed { get; } = new(
        "seabed", "Seabed", "AMLSEA", PaletteColors.FromHex("#7A4B1F", "#8A5A2E", "#6B4A2B"), order: 10, viewingGroup: 45010);

    /// <summary>Environmental: sediment, bottom features, zones (ESB). Green.</summary>
    public static AmlFamily Environmental { get; } = new(
        "environmental", "Environmental", "AMLENV", PaletteColors.FromHex("#2B7547", "#3A7E52", "#2E5C40"), order: 20, viewingGroup: 45020);

    /// <summary>The four families of the handoff, bottom of the plane first.</summary>
    public static IReadOnlyList<AmlFamily> All { get; } = [Seabed, Environmental, Operational, Air];
}
