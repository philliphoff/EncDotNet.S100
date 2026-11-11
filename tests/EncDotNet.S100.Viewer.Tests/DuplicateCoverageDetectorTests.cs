using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class DuplicateCoverageDetectorTests
{
    [Theory]
    [InlineData("S-111", true)]
    [InlineData("S-104", true)]
    [InlineData("S-101", false)]
    [InlineData("S-102", false)]
    [InlineData("S-124", false)]
    [InlineData(null, false)]
    public void IsCollapsibleSpec_only_matches_coverage_specs_that_bundle_variants(string? spec, bool expected)
    {
        Assert.Equal(expected, DuplicateCoverageDetector.IsCollapsibleSpec(spec));
    }

    [Fact]
    public void Identical_file_name_in_different_product_folders_is_duplicate()
    {
        // The NL set ships these as separate exchange sets, so only the
        // identical dataset name links them.
        Assert.True(DuplicateCoverageDetector.IsSameCoverage(
            "S111-neap 0-5/S100_ROOT/.../111NL00_ROTTERDAM_DCF2_20250322_2300.h5",
            "S111-neap 0-15/S100_ROOT/.../111NL00_ROTTERDAM_DCF2_20250322_2300.h5"));
    }

    [Fact]
    public void Different_file_names_are_not_duplicates()
    {
        Assert.False(DuplicateCoverageDetector.IsSameCoverage(
            "a/111NL00_ROTTERDAM_DCF2_20250322_2300.h5",
            "b/111NL00_ROTTERDAM_DCF2_20240723_2300.h5"));
    }

    [Fact]
    public void Null_or_empty_paths_are_not_duplicates()
    {
        Assert.False(DuplicateCoverageDetector.IsSameCoverage(null, null));
        Assert.False(DuplicateCoverageDetector.IsSameCoverage("", ""));
        Assert.False(DuplicateCoverageDetector.IsSameCoverage("x/cell.h5", null));
    }

    [Fact]
    public void File_name_match_is_case_insensitive()
    {
        Assert.True(DuplicateCoverageDetector.IsSameCoverage(
            "neap/Cell.H5",
            "spring/cell.h5"));
    }
}
