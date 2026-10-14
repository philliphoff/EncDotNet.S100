using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S125;
using EncDotNet.S100.Datasets.S127;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S131;
using EncDotNet.S100.Datasets.S201;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Every GML reader runs the shared dataset-level axis-order check (issue
/// #650). For one sample per product, the test reads the sample as is, then
/// a copy with every <c>gml:pos</c> / <c>gml:posList</c> rewritten longitude
/// first (anchored by a latitude-first <c>gml:Envelope</c>, as the UK S-122
/// trial is), and expects the same geometry back.
/// </summary>
public sealed class GmlAxisOrderRoundTripTests
{
    private static readonly XNamespace Gml = "http://www.opengis.net/gml/3.2";

    public static TheoryData<string, string> Samples() => new()
    {
        { "S122", "122TESTDATASET.gml" },
        { "S124", "navwarn_mixed.gml" },
        { "S125", "aton_surface.gml" },
        { "S127", "marine_mixed.gml" },
        { "S128", "S128_TDS_sample.gml" },
        { "S129", "12900MCTDS200TS.gml" },
        { "S131", "harbour_surface.gml" },
        { "S201", "aton_surface.gml" },
        { "S411", "cis_seaice_synthetic.gml" },
        { "S421", "RTE-TEST-GBASIC.s421.gml" },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Sample_rewritten_longitude_first_reads_back_the_same_geometry(string product, string file)
    {
        var original = XDocument.Load(SamplePath(product, file));
        var (expected, _) = Read(product, original);
        Assert.NotEmpty(expected);

        var anchored = WithEnvelope(original, expected);
        var (asEncoded, verified) = Read(product, anchored);
        Assert.Equal(AxisOrderOutcome.AsEncoded, verified!.Outcome);
        AssertSamePositions(expected, asEncoded);

        var swapped = SwapAllPositions(anchored);
        var (normalised, assessment) = Read(product, swapped);
        Assert.Equal(AxisOrderOutcome.Swapped, assessment!.Outcome);
        Assert.True(assessment.Normalized);
        AssertSamePositions(expected, normalised);
    }

    private static (IReadOnlyList<GeoPosition> Positions, AxisOrderAssessment? AxisOrder) Read(string product, XDocument doc)
    {
        using var stream = new MemoryStream();
        doc.Save(stream);
        stream.Position = 0;

        (IEnumerable<IS100Feature> Features, AxisOrderAssessment? AxisOrder) read = product switch
        {
            "S122" => From(S122Dataset.Open(stream), d => d.Features, d => d.AxisOrder),
            "S124" => From(S124Dataset.Open(stream), d => d.Features, d => d.AxisOrder),
            "S125" => From(S125Dataset.Open(stream), d => d.Features, d => d.AxisOrder),
            "S127" => From(S127Dataset.Open(stream), d => d.Features, d => d.AxisOrder),
            "S128" => From(S128Dataset.Open(stream), d => d.Features, d => d.AxisOrder),
            "S129" => From(S129Dataset.Open(stream), d => d.Features, d => d.AxisOrder),
            "S131" => From(S131Dataset.Open(stream), d => d.Features, d => d.AxisOrder),
            "S201" => From(S201Dataset.Open(stream), d => d.Features, d => d.AxisOrder),
            "S411" => From(S411Dataset.Open(stream), d => d.Features, d => d.AxisOrder),
            "S421" => From(S421Dataset.Open(stream), d => d.Features, d => d.AxisOrder),
            _ => throw new ArgumentOutOfRangeException(nameof(product)),
        };

        var positions = read.Features
            .SelectMany(f => f.Points
                .Concat(f.Curves.SelectMany(c => c))
                .Concat(f.ExteriorRing)
                .Concat(f.InteriorRings.SelectMany(r => r)))
            .ToList();
        return (positions, read.AxisOrder);
    }

    private static (IEnumerable<IS100Feature>, AxisOrderAssessment?) From<TDataset>(
        TDataset dataset,
        Func<TDataset, IEnumerable<IS100Feature>> features,
        Func<TDataset, AxisOrderAssessment?> axisOrder) =>
        (features(dataset), axisOrder(dataset));

    private static void AssertSamePositions(IReadOnlyList<GeoPosition> expected, IReadOnlyList<GeoPosition> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Latitude, actual[i].Latitude, 9);
            Assert.Equal(expected[i].Longitude, actual[i].Longitude, 9);
        }
    }

    /// <summary>
    /// Replaces the root envelope with one around <paramref name="positions"/>,
    /// written latitude first.
    /// </summary>
    private static XDocument WithEnvelope(XDocument source, IReadOnlyList<GeoPosition> positions)
    {
        var doc = new XDocument(source);
        var root = doc.Root!;
        var gml = root.GetNamespaceOfPrefix("gml") ?? Gml;
        root.Element(gml + "boundedBy")?.Remove();

        string Corner(double lat, double lon) =>
            string.Create(CultureInfo.InvariantCulture, $"{lat} {lon}");
        root.AddFirst(new XElement(gml + "boundedBy",
            new XElement(gml + "Envelope",
                new XAttribute("srsName", "EPSG:4326"),
                new XElement(gml + "lowerCorner", Corner(positions.Min(p => p.Latitude), positions.Min(p => p.Longitude))),
                new XElement(gml + "upperCorner", Corner(positions.Max(p => p.Latitude), positions.Max(p => p.Longitude))))));
        return doc;
    }

    /// <summary>
    /// Swaps every ordinate pair of every <c>gml:pos</c> and
    /// <c>gml:posList</c> outside the envelope.
    /// </summary>
    private static XDocument SwapAllPositions(XDocument source)
    {
        var doc = new XDocument(source);
        var gml = doc.Root!.GetNamespaceOfPrefix("gml") ?? Gml;
        foreach (var element in doc.Descendants().Where(e => e.Name == gml + "pos" || e.Name == gml + "posList"))
        {
            var parts = element.Value.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i += 2)
                (parts[i], parts[i + 1]) = (parts[i + 1], parts[i]);
            element.Value = string.Join(' ', parts);
        }

        return doc;
    }

    private static string SamplePath(string product, string file, [CallerFilePath] string callerFilePath = "") =>
        Path.Combine(Path.GetDirectoryName(callerFilePath)!, "..", "datasets", product, file);
}
