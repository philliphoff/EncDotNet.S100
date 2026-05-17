using System.Globalization;
using System.Xml.Linq;

namespace EncDotNet.S100.Datasets.S111;

/// <summary>
/// Parses the surface-current speed-band table from the bundled
/// <c>Rules/select_arrow.xsl</c> transform shipped with the S-111
/// portrayal catalogue (Ed 2.0.0, S-100 Part 9 §11).
/// </summary>
/// <remarks>
/// <para>
/// The XSLT contains one <c>&lt;lookup&gt;</c> element per speed band
/// (<c>SurfaceCurrentSpeedBand1</c> .. <c>SurfaceCurrentSpeedBand9</c>)
/// whose <c>&lt;range&gt;</c> child carries <c>lower</c>, <c>upper</c>
/// and <c>closure</c> attributes. Three <c>&lt;xsl:variable&gt;</c>
/// declarations (<c>scaleFloor</c>, <c>scaleCeiling</c>,
/// <c>scaleFactorIntermediate</c>) hold the symbol-scaling constants
/// that bands 1-3 / 4-8 / 9 reference respectively.
/// </para>
/// <para>
/// This reader extracts the table only; the rule logic (how bands map
/// to colour tokens / symbol references / which scale constant applies)
/// stays in <see cref="S111PortrayalCatalogue"/>.
/// </para>
/// </remarks>
public static class S111SpeedBandReader
{
    /// <summary>
    /// A single speed band parsed from the XSLT lookup table.
    /// </summary>
    /// <param name="Min">Inclusive lower bound (knots). <c>0.0</c> for an open-low band.</param>
    /// <param name="Max">Exclusive upper bound (knots). <see cref="float.MaxValue"/> for an open-high band.</param>
    /// <param name="ColorToken">S-100 colour token (e.g. <c>SCBN1</c>).</param>
    /// <param name="SymbolRef">SVG symbol reference (e.g. <c>SCAROW01</c>).</param>
    /// <param name="ScaleByValue">Whether the symbol scales with the speed value.</param>
    /// <param name="ScaleFactor">Scale factor; meaning depends on <paramref name="ScaleByValue"/>.</param>
    /// <param name="Label">Human-readable label, e.g. <c>"0–0.5 kn"</c> (em-dash).</param>
    public sealed record SpeedBand(
        float Min, float Max,
        string ColorToken, string SymbolRef,
        bool ScaleByValue, float ScaleFactor,
        string Label);

    private const string LabelPrefix = "SurfaceCurrentSpeedBand";

    /// <summary>
    /// Parses <c>select_arrow.xsl</c> and returns the speed-band table
    /// in lookup-definition order.
    /// </summary>
    /// <param name="xsltStream">Stream over the XSLT source. The caller owns the stream.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the XSLT is missing one of the three scale-factor
    /// variables (<c>scaleFloor</c>, <c>scaleCeiling</c>,
    /// <c>scaleFactorIntermediate</c>), or if a <c>&lt;range&gt;</c>
    /// uses a closure value other than <c>geLtInterval</c> or
    /// <c>geSemiInterval</c>, or if a lookup label is not a recognised
    /// <c>SurfaceCurrentSpeedBand{N}</c>.
    /// </exception>
    public static IReadOnlyList<SpeedBand> Read(Stream xsltStream)
    {
        ArgumentNullException.ThrowIfNull(xsltStream);

        var doc = XDocument.Load(xsltStream);
        var ns = doc.Root?.GetNamespaceOfPrefix("xsl")
                 ?? XNamespace.Get("http://www.w3.org/1999/XSL/Transform");

        var scaleFloor = ReadScaleVariable(doc, ns, "scaleFloor");
        var scaleCeiling = ReadScaleVariable(doc, ns, "scaleCeiling");
        var scaleIntermediate = ReadScaleVariable(doc, ns, "scaleFactorIntermediate");

        var lookups = doc.Descendants()
            .Where(e => e.Name.LocalName == "lookup")
            .ToList();

        var bands = new List<SpeedBand>(lookups.Count);
        foreach (var lookup in lookups)
        {
            var label = lookup.Elements().FirstOrDefault(e => e.Name.LocalName == "label")?.Value?.Trim();
            if (string.IsNullOrEmpty(label) || !label.StartsWith(LabelPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"S-111 select_arrow.xsl: lookup label '{label}' is not a recognised SurfaceCurrentSpeedBand{{N}}.");
            }

            var bandSuffix = label[LabelPrefix.Length..];
            if (!int.TryParse(bandSuffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bandIndex)
                || bandIndex < 1 || bandIndex > 9)
            {
                throw new InvalidOperationException(
                    $"S-111 select_arrow.xsl: lookup label '{label}' does not end in a band index 1-9.");
            }

            var range = lookup.Elements().FirstOrDefault(e => e.Name.LocalName == "range")
                ?? throw new InvalidOperationException(
                    $"S-111 select_arrow.xsl: lookup '{label}' has no <range>.");

            // The bundled XSLT emits range attributes via
            // <xsl:attribute name="...">value</xsl:attribute> children
            // rather than literal XML attributes (because the XSLT is
            // a transform, not a runtime artefact). Prefer literal
            // attributes when present (for forward compatibility / for
            // test fixtures that use them) and fall back to the
            // xsl:attribute child form.
            var closure = ReadRangeAttribute(range, "closure");
            if (closure is not "geLtInterval" and not "geSemiInterval")
            {
                throw new InvalidOperationException(
                    $"S-111 select_arrow.xsl: lookup '{label}' has unsupported closure '{closure}'. " +
                    "Only geLtInterval and geSemiInterval (open-ended top band) are supported.");
            }

            var lowerAttr = ReadRangeAttribute(range, "lower");
            var upperAttr = ReadRangeAttribute(range, "upper");
            float min = string.IsNullOrEmpty(lowerAttr)
                ? 0.0f
                : float.Parse(lowerAttr, NumberStyles.Float, CultureInfo.InvariantCulture);
            float max = string.IsNullOrEmpty(upperAttr)
                ? float.MaxValue
                : float.Parse(upperAttr, NumberStyles.Float, CultureInfo.InvariantCulture);

            var colorToken = $"SCBN{bandIndex}";
            var symbolRef = $"SCAROW{bandIndex:D2}";

            // S-100 Part 9 §11 scaling rule encoded in the XSLT:
            // bands 1-3 use the scaleFloor constant with ScaleByValue=false;
            // bands 4-8 use scaleFactorIntermediate with ScaleByValue=true
            // (the symbol's size grows with the speed value);
            // band 9 uses the scaleCeiling constant with ScaleByValue=false.
            var (scaleByValue, scaleFactor) = bandIndex switch
            {
                >= 1 and <= 3 => (false, scaleFloor),
                >= 4 and <= 8 => (true, scaleIntermediate),
                9 => (false, scaleCeiling),
                _ => throw new InvalidOperationException($"Unexpected band index {bandIndex}."),
            };

            bands.Add(new SpeedBand(min, max, colorToken, symbolRef, scaleByValue, scaleFactor,
                FormatLabel(min, max)));
        }

        return bands;
    }

    private static float ReadScaleVariable(XDocument doc, XNamespace ns, string variableName)
    {
        var variable = doc.Descendants(ns + "variable")
            .FirstOrDefault(v => (string?)v.Attribute("name") == variableName)
            ?? throw new InvalidOperationException(
                $"S-111 select_arrow.xsl: required <xsl:variable name=\"{variableName}\"> is missing.");

        var valueOf = variable.Elements(ns + "value-of").FirstOrDefault();
        var rawValue = valueOf?.Attribute("select")?.Value ?? variable.Value.Trim();
        if (string.IsNullOrEmpty(rawValue))
        {
            throw new InvalidOperationException(
                $"S-111 select_arrow.xsl: <xsl:variable name=\"{variableName}\"> has no value.");
        }

        return float.Parse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads a logical range attribute (<c>lower</c>, <c>upper</c> or
    /// <c>closure</c>) honouring both literal XML attributes and the
    /// <c>&lt;xsl:attribute name="..."&gt;value&lt;/xsl:attribute&gt;</c>
    /// child-element form used by the bundled S-111 XSLT.
    /// </summary>
    private static string ReadRangeAttribute(XElement range, string name)
    {
        var literal = range.Attribute(name)?.Value;
        if (!string.IsNullOrEmpty(literal))
        {
            return literal;
        }

        var attrChild = range.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "attribute"
                                 && (string?)e.Attribute("name") == name);
        return attrChild?.Value?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Formats a band label using the em-dash convention that matches
    /// the legacy hand-coded table (e.g. <c>"0–0.5 kn"</c>,
    /// <c>"5–7 kn"</c>, <c>"&gt; 13 kn"</c>).
    /// </summary>
    private static string FormatLabel(float min, float max)
    {
        if (max == float.MaxValue)
        {
            return $"> {FormatNumber(min)} kn";
        }
        return $"{FormatNumber(min)}\u2013{FormatNumber(max)} kn";
    }

    private static string FormatNumber(float value)
    {
        // Match the legacy hand-coded labels exactly: integer values
        // render with no decimals ("5", "13"), fractional ones with the
        // minimum necessary precision ("0.5").
        if (value == MathF.Floor(value))
        {
            return ((int)value).ToString(CultureInfo.InvariantCulture);
        }
        return value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
