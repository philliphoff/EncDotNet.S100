using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Datasets.TropicalCyclones.Tests;

/// <summary>Issue #1294: NHC's file names, and finding an advisory's zips beside each other or in a folder.</summary>
public sealed class NhcAdvisoryFilesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tc-files-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("al092022_5day_020.zip", NhcFileKind.Forecast, "AL092022", "020")]
    [InlineData("AL092022_5DAY_003A.ZIP", NhcFileKind.Forecast, "AL092022", "003A")]
    [InlineData("ep012023_fcst_014.zip", NhcFileKind.WindRadii, "EP012023", "014")]
    [InlineData("al092022_5day_latest.zip", NhcFileKind.Forecast, "AL092022", "latest")]
    [InlineData("al092022_best_track.zip", NhcFileKind.BestTrack, "AL092022", "")]
    public void Parses_NHC_file_names(string name, NhcFileKind kind, string atcf, string advisory)
    {
        Assert.True(NhcAdvisoryFiles.TryParseFileName(name, out var k, out var id, out var adv));
        Assert.Equal(kind, k);
        Assert.Equal(atcf, id);
        Assert.Equal(advisory, adv);
    }

    [Theory]
    [InlineData("al092022_5day_020.kmz")]
    [InlineData("al092022-020_5day_pts.shp")]
    [InlineData("gfs.t00z.pgrb2.0p25.f003")]
    [InlineData("")]
    public void Other_names_are_not_NHC_zips(string name) =>
        Assert.False(NhcAdvisoryFiles.TryParseFileName(name, out _, out _, out _));

    [Fact]
    public void Names_the_siblings_as_NHC_does()
    {
        Assert.Equal("al092022_5day_020.zip", NhcAdvisoryFiles.ForecastZipName("AL092022", "020"));
        Assert.Equal("al092022_fcst_020.zip", NhcAdvisoryFiles.WindRadiiZipName("AL092022", "020"));
        Assert.Equal("al092022_best_track.zip", NhcAdvisoryFiles.BestTrackZipName("AL092022"));
    }

    [Fact]
    public void Finds_the_siblings_of_a_forecast_zip()
    {
        var forecast = TropicalCycloneFixtures.CopyIan(_dir);

        var files = NhcAdvisoryFiles.Find(forecast)!;

        Assert.Equal(forecast, files.ForecastZip);
        Assert.Equal("al092022_fcst_020.zip", Path.GetFileName(files.WindRadiiZip));
        Assert.Equal("al092022_best_track.zip", Path.GetFileName(files.BestTrackZip));
    }

    [Fact]
    public void A_folder_gives_its_latest_advisory()
    {
        foreach (var name in new[] { "al092022_5day_019.zip", "al092022_5day_020.zip", "al092022_5day_020A.zip", "al092022_fcst_020A.zip", "al092022_best_track.zip", "notes.txt" })
            File.WriteAllBytes(Path.Combine(_dir, name), []);

        Assert.True(NhcAdvisoryFiles.IsAdvisoryFolder(_dir));
        var files = NhcAdvisoryFiles.Find(_dir)!;
        Assert.Equal("al092022_5day_020A.zip", Path.GetFileName(files.ForecastZip));
        Assert.Equal("al092022_fcst_020A.zip", Path.GetFileName(files.WindRadiiZip));
        Assert.NotNull(files.BestTrackZip);
    }

    [Fact]
    public void A_folder_of_two_storms_or_none_is_not_an_advisory()
    {
        Assert.False(NhcAdvisoryFiles.IsAdvisoryFolder(_dir));

        File.WriteAllBytes(Path.Combine(_dir, "al092022_5day_020.zip"), []);
        File.WriteAllBytes(Path.Combine(_dir, "al102022_5day_001.zip"), []);
        Assert.False(NhcAdvisoryFiles.IsAdvisoryFolder(_dir));
        Assert.Null(NhcAdvisoryFiles.Find(_dir));
    }

    [Fact]
    public void Advisories_order_by_number_then_suffix()
    {
        string[] advisories = ["020A", "003", "latest", "020", "019"];
        Assert.Equal(
            ["003", "019", "020", "020A", "latest"],
            advisories.OrderBy(a => NhcAdvisoryFiles.AdvisoryOrder(a).Number).ThenBy(a => NhcAdvisoryFiles.AdvisoryOrder(a).Suffix, StringComparer.Ordinal));
    }
}
