using System.Text.RegularExpressions;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Guards the deliberately partial <c>HostSpatialRelate</c> host function
/// (<see cref="S101SpatialRelate"/>): every call in a bundled portrayal
/// catalogue must pass a literal DE-9IM pattern the host supports. A catalogue
/// refresh that starts using a new pattern fails here, rather than as Default
/// (question mark) symbology on a chart.
/// </summary>
public sealed partial class HostSpatialRelateCatalogueTests
{
    [Fact]
    public void BundledCatalogues_UseOnlySupportedPatterns()
    {
        var assembly = typeof(Specification).Assembly;
        var calls = new List<(string Resource, string Arguments)>();

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                continue;

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var source = LineComment().Replace(reader.ReadToEnd(), string.Empty);

            foreach (Match call in RelateCall().Matches(source))
                calls.Add((resource, call.Groups["args"].Value));
        }

        // The S-401 MARSYS01 rule is the known caller; an empty list means the
        // scan itself broke.
        Assert.NotEmpty(calls);

        foreach (var (resource, arguments) in calls)
        {
            var pattern = PatternArgument().Match(arguments);
            Assert.True(pattern.Success,
                $"{resource}: HostSpatialRelate({arguments}) doesn't pass a literal pattern; " +
                "check it against S101SpatialRelate.SupportedPatterns by hand.");
            Assert.True(
                S101SpatialRelate.SupportedPatterns.Contains(pattern.Groups["pattern"].Value.ToUpperInvariant()),
                $"{resource}: HostSpatialRelate pattern '{pattern.Groups["pattern"].Value}' isn't supported " +
                "by S101SpatialRelate; extend it (or switch to a full DE-9IM implementation).");
        }
    }

    [GeneratedRegex(@"--[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"\bHostSpatialRelate\s*\((?<args>[^)]*)\)")]
    private static partial Regex RelateCall();

    [GeneratedRegex(@",\s*['""](?<pattern>[^'""]*)['""]\s*$")]
    private static partial Regex PatternArgument();
}
