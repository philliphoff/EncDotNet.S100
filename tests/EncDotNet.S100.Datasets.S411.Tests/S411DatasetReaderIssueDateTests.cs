using System;
using System.IO;
using EncDotNet.S100.Datasets.S411;

namespace EncDotNet.S100.Datasets.S411.Tests;

public class S411DatasetReaderIssueDateTests
{
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    [SkippableFact]
    public void IhoSampleDataset_exposes_dataset_reference_date_as_IssueDate()
    {
        var path = Path.Combine(TestDataDir, "iho_4112C00TDS001.gml");
        Skip.IfNot(File.Exists(path), $"Fixture missing: {path}");

        using var s = File.OpenRead(path);
        var ds = S411Dataset.Open(s);

        Assert.NotNull(ds);
        // The IHO 1.2.1 sample shape carries
        // <S100:datasetReferenceDate>2001-04-22</S100:datasetReferenceDate>
        // (S-100 Part 17 / Part 10b §C.4); reader must expose it as
        // S411Dataset.IssueDate so the global time slider can register
        // the dataset on the timeline.
        Assert.NotNull(ds.IssueDate);
        Assert.Equal(2001, ds.IssueDate!.Value.Year);
        Assert.Equal(4, ds.IssueDate.Value.Month);
        Assert.Equal(22, ds.IssueDate.Value.Day);
    }

    [Fact]
    public void Synthetic_dataset_with_no_timestamp_has_null_IssueDate()
    {
        // JCOMM/CIS shape with no observation/issue date — IssueDate
        // must be null so the dataset does not pollute the global
        // timeline.
        var gml = """
            <ice:IceDataSet xmlns:ice="http://www.jcomm.info/ice"
                            xmlns:gml="http://www.opengis.net/gml/3.2"
                            gml:id="d1">
              <ice:IceFeatureMember/>
            </ice:IceDataSet>
            """;
        using var s = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gml));
        var ds = S411Dataset.Open(s);
        Assert.Null(ds.IssueDate);
    }
}
