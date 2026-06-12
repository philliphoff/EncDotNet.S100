using EncDotNet.S100.Viewer.Services;
using Xunit;

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
    public void Same_source_and_identical_file_name_in_different_folders_is_duplicate()
    {
        var source = new object();

        Assert.True(DuplicateCoverageDetector.IsSameCoverage(
            source, "S111-neap 0-5/S100_ROOT/.../111NL00_ROTTERDAM_DCF2_20250322_2300.h5",
            source, "S111-neap 0-15/S100_ROOT/.../111NL00_ROTTERDAM_DCF2_20250322_2300.h5"));
    }

    [Fact]
    public void Different_file_names_are_not_duplicates()
    {
        var source = new object();

        Assert.False(DuplicateCoverageDetector.IsSameCoverage(
            source, "a/111NL00_ROTTERDAM_DCF2_20250322_2300.h5",
            source, "b/111NL00_ROTTERDAM_DCF2_20240723_2300.h5"));
    }

    [Fact]
    public void Different_sources_are_never_duplicates()
    {
        Assert.False(DuplicateCoverageDetector.IsSameCoverage(
            new object(), "x/cell.h5",
            new object(), "x/cell.h5"));
    }

    [Fact]
    public void Null_or_empty_paths_are_not_duplicates()
    {
        var source = new object();

        Assert.False(DuplicateCoverageDetector.IsSameCoverage(source, null, source, null));
        Assert.False(DuplicateCoverageDetector.IsSameCoverage(source, "", source, ""));
    }

    [Fact]
    public void File_name_match_is_case_insensitive()
    {
        var source = new object();

        Assert.True(DuplicateCoverageDetector.IsSameCoverage(
            source, "neap/Cell.H5",
            source, "spring/cell.h5"));
    }
}
