using System.Xml.Linq;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.S201;

/// <summary>
/// Builds and caches a label→code lookup for enumeration and code-list
/// attributes from the bundled S-201 Edition 2.0.0 Feature Catalogue.
/// </summary>
/// <remarks>
/// <para>Producers of real-world S-201 datasets routinely emit listed-value
/// attributes using the Feature Catalogue <em>label</em>
/// (e.g. <c>&lt;categoryOfLateralMark&gt;Port-Hand Lateral Mark&lt;/categoryOfLateralMark&gt;</c>)
/// rather than the numeric <em>code</em>
/// (e.g. <c>&lt;categoryOfLateralMark&gt;1&lt;/categoryOfLateralMark&gt;</c>).
/// The upstream S-201 portrayal catalogue&apos;s XSLT predicates universally
/// match against numeric codes (S-201 Edition 2.0.0 §11, e.g.
/// <c>LateralBuoy[@primitive='Point' and buoyShape=1]</c>). Without this
/// translation, every label-encoded feature falls through to the generic
/// default symbol (e.g. <c>BOYGEN03</c>).</para>
/// <para>The table is parsed lazily on first access from the bundled FC
/// (<c>S100FC</c> namespace) and cached for the lifetime of the process.
/// Unknown attribute codes or unknown values pass through unchanged.</para>
/// </remarks>
internal static class S201ListedValueIndex
{
    private const string FcNamespace = "http://www.iho.int/S100FC/5.0";

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> _index =
        new(BuildIndex);

    /// <summary>
    /// Returns the numeric code for the given <paramref name="value"/> if it
    /// matches a listed-value label for the given attribute
    /// <paramref name="code"/>; otherwise returns the original value
    /// unchanged.
    /// </summary>
    public static string Normalize(string code, string value)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(value))
            return value;

        if (_index.Value.TryGetValue(code, out var labelToCode) &&
            labelToCode.TryGetValue(value, out var numeric))
        {
            return numeric;
        }

        return value;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BuildIndex()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        using var stream = Specification.TryOpenFeatureCatalogue("S-201");
        if (stream is null)
            return result;

        XDocument doc;
        try
        {
            doc = XDocument.Load(stream);
        }
        catch
        {
            return result;
        }

        XName attrName = XName.Get("S100_FC_SimpleAttribute", FcNamespace);
        XName codeName = XName.Get("code", FcNamespace);
        XName listedValuesName = XName.Get("listedValues", FcNamespace);
        XName listedValueName = XName.Get("listedValue", FcNamespace);
        XName labelName = XName.Get("label", FcNamespace);

        foreach (var attr in doc.Descendants(attrName))
        {
            var attrCode = attr.Element(codeName)?.Value;
            if (string.IsNullOrEmpty(attrCode))
                continue;

            var listed = attr.Element(listedValuesName);
            if (listed is null)
                continue;

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var lv in listed.Elements(listedValueName))
            {
                var label = lv.Element(labelName)?.Value;
                var numeric = lv.Element(codeName)?.Value;
                if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(numeric))
                    continue;

                map[label] = numeric;
            }

            if (map.Count > 0)
                result[attrCode] = map;
        }

        return result;
    }
}
