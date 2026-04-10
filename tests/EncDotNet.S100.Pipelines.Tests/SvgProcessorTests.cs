using System.Xml.Linq;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Pipelines.Tests;

public class SvgProcessorTests
{
    private static readonly ColorPalette DayPalette = new("Day", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CHBLK"] = "#000000",
        ["CHYLW"] = "#FFFF00",
        ["OUTLW"] = "#464646",
        ["DEPVS"] = "#A0FFA0",
        ["NODTA"] = "#93AEBB",
    });

    // ── Processing instruction removal ────────────────────────────────

    [Fact]
    public void Process_RemovesXmlStylesheetPI()
    {
        var svg = """
            <?xml-stylesheet href="daySvgStyle.css" type="text/css"?>
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="4mm" viewBox="0 0 4 4">
              <circle cx="2" cy="2" r="1"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);

        Assert.DoesNotContain("xml-stylesheet", result);
        Assert.Contains("<circle", result);
    }

    // ── Layout element removal ────────────────────────────────────────

    [Fact]
    public void Process_RemovesLayoutElements()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <rect class="symbolBox layout" x="0" y="0" width="4" height="6"/>
              <circle class="pivotPoint layout" cx="2" cy="3" r="0.1"/>
              <path d="M0,0 L4,6"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);

        Assert.DoesNotContain("symbolBox", result);
        Assert.DoesNotContain("pivotPoint", result);
        Assert.Contains("<path", result);
    }

    // ── Fill token resolution ─────────────────────────────────────────

    [Fact]
    public void Process_ResolvesFillTokenClass()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <path d="M0,0 L4,6" class="fCHYLW"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);
        var doc = XDocument.Parse(result);
        var path = doc.Root!.Descendants().First(e => e.Name.LocalName == "path");

        Assert.Equal("#FFFF00", path.Attribute("fill")?.Value);
        Assert.Null(path.Attribute("class"));
    }

    // ── Stroke token resolution ───────────────────────────────────────

    [Fact]
    public void Process_ResolvesStrokeTokenClass()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <path d="M0,0 L4,6" class="sOUTLW"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);
        var doc = XDocument.Parse(result);
        var path = doc.Root!.Descendants().First(e => e.Name.LocalName == "path");

        Assert.Equal("#464646", path.Attribute("stroke")?.Value);
    }

    // ── f0 (fill none) ───────────────────────────────────────────────

    [Fact]
    public void Process_ResolvesF0ToFillNone()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <path d="M0,0 L4,6" class="f0"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);
        var doc = XDocument.Parse(result);
        var path = doc.Root!.Descendants().First(e => e.Name.LocalName == "path");

        Assert.Equal("none", path.Attribute("fill")?.Value);
    }

    // ── sl (stroke-linecap/linejoin round) ────────────────────────────

    [Fact]
    public void Process_ResolvesSlToRoundCapsAndJoins()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <path d="M0,0 L4,6" class="sl"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);
        var doc = XDocument.Parse(result);
        var path = doc.Root!.Descendants().First(e => e.Name.LocalName == "path");

        Assert.Equal("round", path.Attribute("stroke-linecap")?.Value);
        Assert.Equal("round", path.Attribute("stroke-linejoin")?.Value);
    }

    // ── Multiple classes combined ─────────────────────────────────────

    [Fact]
    public void Process_ResolvesMultipleClassesCombined()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <path d="M0,0 L4,6" class="sl f0 sOUTLW" stroke-width="0.32"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);
        var doc = XDocument.Parse(result);
        var path = doc.Root!.Descendants().First(e => e.Name.LocalName == "path");

        Assert.Equal("none", path.Attribute("fill")?.Value);
        Assert.Equal("#464646", path.Attribute("stroke")?.Value);
        Assert.Equal("round", path.Attribute("stroke-linecap")?.Value);
        Assert.Equal("round", path.Attribute("stroke-linejoin")?.Value);
        Assert.Equal("0.32", path.Attribute("stroke-width")?.Value);
    }

    // ── Existing inline attributes preserved ──────────────────────────

    [Fact]
    public void Process_PreservesExistingInlineAttributes()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <path d="M0,0 L4,6" fill="#FF0000" class="fCHYLW"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);
        var doc = XDocument.Parse(result);
        var path = doc.Root!.Descendants().First(e => e.Name.LocalName == "path");

        // Existing inline fill is preserved; class-based fill does not override
        Assert.Equal("#FF0000", path.Attribute("fill")?.Value);
    }

    // ── Metadata removal ──────────────────────────────────────────────

    [Fact]
    public void Process_RemovesMetadataTitleDesc()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <metadata>some metadata</metadata>
              <title>Symbol Title</title>
              <desc>Symbol Description</desc>
              <circle cx="2" cy="3" r="1"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);

        Assert.DoesNotContain("<metadata", result);
        Assert.DoesNotContain("<title", result);
        Assert.DoesNotContain("<desc", result);
        Assert.Contains("<circle", result);
    }

    // ── Item 2: CHBLK token resolves correctly with palette ───────────

    [Fact]
    public void Process_ResolvesChblkTokenCorrectly()
    {
        // CHBLK maps to #000000 in the Day palette.
        // The old code had a sentinel bug where #000000 was treated as "not found"
        // unless the token was specifically CHBLK.
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <path d="M0,0 L4,6" class="fCHBLK"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);
        var doc = XDocument.Parse(result);
        var path = doc.Root!.Descendants().First(e => e.Name.LocalName == "path");

        Assert.Equal("#000000", path.Attribute("fill")?.Value);
    }

    [Fact]
    public void TryResolve_KnownTokenReturnsHex()
    {
        Assert.True(DayPalette.TryResolve("CHYLW", out var hex));
        Assert.Equal("#FFFF00", hex);
    }

    [Fact]
    public void TryResolve_UnknownTokenReturnsFalse()
    {
        Assert.False(DayPalette.TryResolve("NOTREAL", out _));
    }

    [Fact]
    public void TryResolve_ChblkReturnsBlack()
    {
        // Validates that CHBLK (#000000) is resolvable via TryResolve
        Assert.True(DayPalette.TryResolve("CHBLK", out var hex));
        Assert.Equal("#000000", hex);
    }

    // ── Item 3: Stroke-width scaling ──────────────────────────────────

    [Fact]
    public void Process_ScalesStrokeWidthWhenScaleProvided()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <path d="M0,0 L4,6" stroke-width="0.32"/>
            </svg>
            """;

        // 0.32 mm * 3.78 px/mm ≈ 1.2096
        var result = SvgProcessor.Process(svg, DayPalette, strokeWidthScale: 3.78);
        var doc = XDocument.Parse(result);
        var path = doc.Root!.Descendants().First(e => e.Name.LocalName == "path");

        var sw = double.Parse(path.Attribute("stroke-width")!.Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(sw, 1.20, 1.22);
    }

    [Fact]
    public void Process_LeavesStrokeWidthUnscaledByDefault()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <path d="M0,0 L4,6" stroke-width="0.32"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);
        var doc = XDocument.Parse(result);
        var path = doc.Root!.Descendants().First(e => e.Name.LocalName == "path");

        Assert.Equal("0.32", path.Attribute("stroke-width")?.Value);
    }

    // ── Item 4: Unrecognized CSS classes preserved ────────────────────

    [Fact]
    public void Process_PreservesUnrecognizedClasses()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <path d="M0,0 L4,6" class="fCHYLW custom-class"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);
        var doc = XDocument.Parse(result);
        var path = doc.Root!.Descendants().First(e => e.Name.LocalName == "path");

        Assert.Equal("#FFFF00", path.Attribute("fill")?.Value);
        Assert.Equal("custom-class", path.Attribute("class")?.Value);
    }

    [Fact]
    public void Process_RemovesClassAttributeWhenAllClassesRecognized()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <path d="M0,0 L4,6" class="fCHYLW sl"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);
        var doc = XDocument.Parse(result);
        var path = doc.Root!.Descendants().First(e => e.Name.LocalName == "path");

        Assert.Null(path.Attribute("class"));
    }

    // ── Unknown token in class leaves no attribute ────────────────────

    [Fact]
    public void Process_UnknownFillTokenDoesNotSetFillAttribute()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <path d="M0,0 L4,6" class="fNOTREAL"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);
        var doc = XDocument.Parse(result);
        var path = doc.Root!.Descendants().First(e => e.Name.LocalName == "path");

        // Unknown token with palette → no fill set (item 2 fix)
        Assert.Null(path.Attribute("fill"));
    }

    // ── Full realistic SVG (BOYCAR01-style) ───────────────────────────

    [Fact]
    public void Process_FullRealisticSvg()
    {
        var svg = """
            <?xml-stylesheet href="daySvgStyle.css" type="text/css"?>
            <svg xmlns="http://www.w3.org/2000/svg" width="4.48mm" height="6.48mm" viewBox="-2.82 -3.29 4.48 6.48">
              <rect class="symbolBox layout" x="-2.82" y="-3.29" width="4.48" height="6.48"/>
              <metadata>buoy metadata</metadata>
              <title>BOYCAR01</title>
              <path d="M-0.5,0 L0.5,0" class="fCHYLW"/>
              <path d="M-1,-1 L1,1" class="sl f0 sOUTLW" stroke-width="0.32"/>
              <circle class="pivotPoint layout" cx="0" cy="0" r="0.15"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, DayPalette);

        // PIs removed
        Assert.DoesNotContain("xml-stylesheet", result);
        // Layout elements removed
        Assert.DoesNotContain("symbolBox", result);
        Assert.DoesNotContain("pivotPoint", result);
        // Metadata removed
        Assert.DoesNotContain("<metadata", result);
        Assert.DoesNotContain("<title", result);
        // Colors resolved
        Assert.Contains("fill=\"#FFFF00\"", result);
        Assert.Contains("stroke=\"#464646\"", result);
        Assert.Contains("fill=\"none\"", result);
        Assert.Contains("stroke-linecap=\"round\"", result);
        // Stroke-width preserved (no scale)
        Assert.Contains("stroke-width=\"0.32\"", result);
        // No class attributes remain
        Assert.DoesNotContain("class=", result);
    }

    // ── Null palette ───────────────────────────────────────────────────

    [Fact]
    public void Process_NullPaletteDoesNotResolveTokens()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="6mm" viewBox="0 0 4 6">
              <path d="M0,0 L4,6" class="fCHBLK sOUTLW"/>
            </svg>
            """;

        var result = SvgProcessor.Process(svg, null);
        var doc = XDocument.Parse(result);
        var path = doc.Root!.Descendants().First(e => e.Name.LocalName == "path");

        // Without a palette, token classes cannot be resolved — no attributes set
        Assert.Null(path.Attribute("fill"));
        Assert.Null(path.Attribute("stroke"));
    }
}
