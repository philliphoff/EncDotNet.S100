using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Drift guard for the S-411 WMO colour tables (issue #416).
/// </summary>
/// <remarks>
/// <para>
/// The concentration (<c>iceact</c>) and stage-of-development (<c>icesod</c>)
/// colours are held <em>inline</em> as <c>#RRGGBB</c> literals in the S-411
/// portrayal adapter (<c>Adapter/main.xsl</c>) so the render path needs no
/// runtime <c>document()</c> plumbing. The bundled upstream stylesheets
/// (<c>pc/Rules/seaice_wmo_iceact.xsl</c>, <c>seaice_wmo_icesod.xsl</c>) remain
/// the canonical source of the values and stay byte-identical to upstream.
/// </para>
/// <para>
/// These tests parse the upstream <c>number($iceX)=N -&gt; colorToken</c>
/// entries, convert each "R G B" decimal triple (sanitising the single
/// malformed dash-separated entry) to <c>#RRGGBB</c>, and assert exact equality
/// with the adapter's inline tables. If the upstream tables are ever refreshed
/// and a colour changes, this fails LOUDLY at test time instead of silently
/// diverging at render time — the same single-source guarantee the runtime
/// <c>document()</c> approach gave, without its cost.
/// </para>
/// </remarks>
public class S411WmoColourParityTests
{
    private static readonly XNamespace Xsl = "http://www.w3.org/1999/XSL/Transform";

    [Fact]
    public async Task IceactInlineTable_MatchesUpstream()
    {
        var upstream = await ReadUpstreamTableAsync("Rules/seaice_wmo_iceact.xsl", "iceact");
        var inline = ReadAdapterInlineTable("iceact-fill-hex");

        Assert.NotEmpty(upstream);
        AssertTablesEqual(upstream, inline);
    }

    [Fact]
    public async Task IcesodInlineTable_MatchesUpstream()
    {
        var upstream = await ReadUpstreamTableAsync("Rules/seaice_wmo_icesod.xsl", "icesod");
        var inline = ReadAdapterInlineTable("icesod-fill-hex");

        Assert.NotEmpty(upstream);
        AssertTablesEqual(upstream, inline);
    }

    private static void AssertTablesEqual(
        IReadOnlyDictionary<int, string> upstream,
        IReadOnlyDictionary<int, string> inline)
    {
        // Every upstream code must be present inline with the identical colour…
        foreach (var (code, hex) in upstream)
        {
            Assert.True(
                inline.TryGetValue(code, out var inlineHex),
                $"Adapter inline table is missing egg code {code} (upstream = {hex}).");
            Assert.True(
                string.Equals(hex, inlineHex, StringComparison.OrdinalIgnoreCase),
                $"Egg code {code}: upstream {hex} != inline {inlineHex}.");
        }

        // …and the inline table must not invent codes the upstream lacks.
        foreach (var code in inline.Keys)
        {
            Assert.True(
                upstream.ContainsKey(code),
                $"Adapter inline table has egg code {code} not present upstream.");
        }
    }

    /// <summary>
    /// Reads the upstream WMO table as <c>code -&gt; #RRGGBB</c>, converting the
    /// "R G B" (or malformed dash-separated) <c>colorToken</c> literals.
    /// </summary>
    private static async Task<IReadOnlyDictionary<int, string>> ReadUpstreamTableAsync(
        string relativePath, string attribute)
    {
        using var source = Specification.CreatePortrayalCatalogueSource("S-411");
        await using var stream = await source.OpenAsync(relativePath);
        var doc = XDocument.Load(stream);

        var testPrefix = $"number(${attribute})=";
        var table = new Dictionary<int, string>();

        foreach (var when in doc.Descendants(Xsl + "when"))
        {
            var test = (string?)when.Attribute("test");
            if (test is null || !test.StartsWith(testPrefix, StringComparison.Ordinal))
                continue;

            if (!int.TryParse(test.AsSpan(testPrefix.Length), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var code))
                continue;

            var select = when
                .Descendants(Xsl + "with-param")
                .FirstOrDefault(p => (string?)p.Attribute("name") == "colorToken")
                ?.Attribute("select")?.Value;

            if (string.IsNullOrEmpty(select))
                continue;

            // @select is a quoted XPath string literal, e.g. "'000 100 255'".
            var raw = select.Trim().Trim('\'', '"');
            table[code] = TokenToHex(raw);
        }

        return table;
    }

    /// <summary>
    /// Reads the adapter's inline <c>code -&gt; #RRGGBB</c> table from the named
    /// template (e.g. <c>iceact-fill-hex</c>) embedded in the S-411 assembly.
    /// </summary>
    private static IReadOnlyDictionary<int, string> ReadAdapterInlineTable(string templateName)
    {
        var asm = typeof(S411PortrayalCatalogue).Assembly;
        const string resourceName = "EncDotNet.S100.Datasets.S411.Adapter.main.xsl";
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");

        var doc = XDocument.Load(stream);

        var template = doc.Descendants(Xsl + "template")
            .FirstOrDefault(t => (string?)t.Attribute("name") == templateName)
            ?? throw new InvalidOperationException($"Adapter template '{templateName}' not found.");

        var table = new Dictionary<int, string>();
        foreach (var when in template.Descendants(Xsl + "when"))
        {
            var test = (string?)when.Attribute("test");
            var match = test is null ? null : Regex.Match(test, @"^\$code=(\d+)$");
            if (match is null || !match.Success)
                continue;

            var code = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            table[code] = when.Value.Trim();
        }

        return table;
    }

    /// <summary>
    /// Converts an upstream "R G B" decimal colour token (space- or, for the
    /// single malformed '255-125-007' entry, dash-separated) into #RRGGBB.
    /// </summary>
    private static string TokenToHex(string token)
    {
        var parts = token.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, parts.Length);
        var r = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var g = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var b = int.Parse(parts[2], CultureInfo.InvariantCulture);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
