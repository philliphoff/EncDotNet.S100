using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EncDotNet.S100.Datasets.S411;

/// <summary>
/// The WMO ice colour tables the S-411 portrayal fills areas with, read from
/// the portrayal adapter (<c>Adapter/main.xsl</c>), whose inline tables mirror
/// the catalogue's <c>seaice_wmo_iceact.xsl</c> and
/// <c>seaice_wmo_icesod.xsl</c> (a parity test keeps them equal). Grouped by
/// colour for a legend.
/// </summary>
public static partial class S411IceColours
{
    private static readonly XNamespace Xsl = "http://www.w3.org/1999/XSL/Transform";
    private static readonly Lazy<XDocument> Adapter = new(LoadAdapter);

    /// <summary>
    /// One colour of a WMO table and the egg codes filled with it, in table order.
    /// </summary>
    /// <param name="Color">The fill colour, <c>#RRGGBB</c>.</param>
    /// <param name="Codes">The egg codes, e.g. <c>[10, 12, 13, 20, 23, 30]</c>.</param>
    public sealed record ColourClass(string Color, IReadOnlyList<int> Codes);

    /// <summary>The total concentration (<c>iceact</c>) colours, lowest concentration first.</summary>
    public static IReadOnlyList<ColourClass> Concentration { get; } = ReadTable("iceact-fill-hex");

    /// <summary>The stage of development (<c>icesod</c>) colours, in code order.</summary>
    public static IReadOnlyList<ColourClass> StageOfDevelopment { get; } = ReadTable("icesod-fill-hex");

    /// <summary>The transparency of the ice area fills, 0 (opaque) to 1.</summary>
    public static double FillTransparency { get; } = ReadFillTransparency();

    /// <summary>
    /// A legend label for a colour class from the feature catalogue's
    /// definitions of its codes: one definition as it is
    /// (<c>"Ice Free"</c>); concentrations in tenths as one range
    /// (<c>"1/10 to 3/10 ice"</c>); anything else joined with commas.
    /// Codes without a definition are left out.
    /// </summary>
    /// <param name="codes">The class's codes.</param>
    /// <param name="definition">The catalogue definition of a code, or <see langword="null"/> when it has none.</param>
    public static string Label(IReadOnlyList<int> codes, Func<int, string?> definition)
    {
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(definition);
        var definitions = codes
            .Select(definition)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (definitions.Count == 0)
            return string.Join(", ", codes.Select(c => c.ToString("00", CultureInfo.InvariantCulture)));
        if (definitions.Count == 1)
            return definitions[0];

        int low = int.MaxValue, high = int.MinValue;
        foreach (var d in definitions)
        {
            var match = TenthsPattern().Match(d);
            if (!match.Success)
                return string.Join(", ", definitions);
            int from = int.Parse(match.Groups["from"].Value, CultureInfo.InvariantCulture);
            int to = match.Groups["to"].Success ? int.Parse(match.Groups["to"].Value, CultureInfo.InvariantCulture) : from;
            low = Math.Min(low, from);
            high = Math.Max(high, to);
        }

        return string.Create(CultureInfo.InvariantCulture, $"{low}/10 to {high}/10 ice");
    }

    [GeneratedRegex(@"^(?<from>\d+)/10(?: to (?<to>\d+)/10)? ice$")]
    private static partial Regex TenthsPattern();

    private static XDocument LoadAdapter()
    {
        var asm = typeof(S411IceColours).Assembly;
        const string resourceName = "EncDotNet.S100.Datasets.S411.Adapter.main.xsl";
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found in {asm.GetName().Name}.");
        return XDocument.Load(stream);
    }

    private static IReadOnlyList<ColourClass> ReadTable(string templateName)
    {
        var template = Adapter.Value.Descendants(Xsl + "template")
            .FirstOrDefault(t => (string?)t.Attribute("name") == templateName)
            ?? throw new InvalidOperationException($"The S-411 adapter has no '{templateName}' template.");

        var classes = new List<(string Color, List<int> Codes)>();
        foreach (var when in template.Descendants(Xsl + "when"))
        {
            var test = (string?)when.Attribute("test") ?? "";
            var equals = test.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0 || !int.TryParse(test[(equals + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
                continue;
            var color = when.Value.Trim().ToUpperInvariant();
            var existing = classes.FindIndex(c => c.Color == color);
            if (existing < 0)
                classes.Add((color, [code]));
            else
                classes[existing].Codes.Add(code);
        }

        return [.. classes.Select(c => new ColourClass(c.Color, c.Codes))];
    }

    private static double ReadFillTransparency()
    {
        var template = Adapter.Value.Descendants(Xsl + "template")
            .First(t => (string?)t.Attribute("name") == "emit-ice-area");
        var value = template.Descendants("colorFill").Elements("transparency").First().Value;
        return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
