using EncDotNet.Iso8211;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Pins how <see cref="S101DocumentReader"/> reads a repeating field whose last repetition is
/// cut short by the end of the field data: it still reads that repetition, defaulting the
/// optional subfields it lacks and throwing when a required one is missing.
/// </summary>
public class S101DocumentReaderTruncatedFieldTests
{
    [Fact]
    public void Read_AttributeRepetitionEndingBeforeValue_KeepsAttributeWithEmptyValue()
    {
        // Feature 50's second attribute is (100, 1, "10.5"); drop its ATVL and terminator.
        var document = ReadWithFeatureField("ATTR", data => data[..^("10.5".Length + 1)]);

        var feature = document.Features.Single(f => f.RecordId == 50);
        Assert.Equal(
            [(200, 1, "Boston Harbor"), (100, 1, "")],
            feature.Attributes.Select(a => ((int)a.NumericCode, (int)a.Index, a.Value)));
    }

    [Fact]
    public void Read_SpatialAssociationEndingBeforeOrientation_Throws()
    {
        // SPAS starts RRNM (b11), RRID (b14), ORNT (b11); keep only RRNM and RRID.
        var exception = Assert.Throws<KeyNotFoundException>(() => ReadWithFeatureField("SPAS", data => data[..5]));

        Assert.Contains("ORNT", exception.Message);
    }

    // Writes the sample document, replaces a field of its first feature record with a
    // shortened copy, and reads the result back.
    private static S101Document ReadWithFeatureField(string tag, Func<byte[], byte[]> shorten)
    {
        var iso = Iso8211DocumentReader.Read(S101DocumentWriter.Write(S101DocumentWriterTests.BuildSampleDocument()));
        var feature = iso.Records.First(r => r.Fields.Count > 0 && r.Fields[0].Tag == "FRID");
        var fields = feature.Fields
            .Select(f => f.Tag == tag ? f with { Data = shorten(f.Data.ToArray()) } : f)
            .ToList();
        var records = iso.Records.Select(r => r == feature ? r with { Fields = fields } : r).ToList();

        using var stream = new MemoryStream(Iso8211DocumentWriter.Write(new Iso8211Document { Records = records }));
        return S101DocumentReader.ReadFromStream(stream);
    }
}
