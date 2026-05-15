using System.IO;
using System.Reflection;
using System.Text;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests for <see cref="EcdisLabelOverrideProvider"/> and the
/// fallback chain in <see cref="EcdisViewingGroupViewModel"/>.
/// </summary>
public class EcdisLabelOverrideProviderTests
{
    [Fact]
    public void TryGetLabel_ReturnsCuratedLabel_FromShippedS101Resource()
    {
        // The S101 labels file is embedded as part of the viewer
        // assembly; this asserts both that it loads and that a
        // known entry is present (guards against the file being
        // accidentally renamed or excluded from EmbeddedResource).
        var provider = new EcdisLabelOverrideProvider();

        Assert.True(provider.TryGetLabel("S-101", 12010, out var label));
        Assert.Equal("Land area", label);
    }

    [Fact]
    public void TryGetLabel_NormalisesSpecCode()
    {
        var provider = new EcdisLabelOverrideProvider();

        Assert.True(provider.TryGetLabel("S101", 12010, out var withoutDash));
        Assert.Equal("Land area", withoutDash);

        Assert.True(provider.TryGetLabel("s-101", 12010, out var lower));
        Assert.Equal("Land area", lower);
    }

    [Fact]
    public void TryGetLabel_ReturnsFalse_WhenSpecHasNoOverrides()
    {
        var provider = new EcdisLabelOverrideProvider();

        Assert.False(provider.TryGetLabel("S-999", 12010, out var label));
        Assert.Equal(string.Empty, label);
    }

    [Fact]
    public void TryGetLabel_ReturnsFalse_WhenIdNotPresent()
    {
        var provider = new EcdisLabelOverrideProvider();

        Assert.False(provider.TryGetLabel("S-101", 999999, out var label));
        Assert.Equal(string.Empty, label);
    }

    [Fact]
    public void Provider_ToleratesMalformedJson()
    {
        var assembly = BuildAssemblyWithResource(
            "S100.labels.json",
            "{ this is not valid json");

        var provider = new EcdisLabelOverrideProvider(assembly);

        Assert.False(provider.TryGetLabel("S-100", 1, out _));
    }

    [Fact]
    public void Provider_ToleratesMissingLabel()
    {
        // An entry with an empty label is treated as no override.
        var assembly = BuildAssemblyWithResource(
            "S100.labels.json",
            "{ \"specCode\": \"S-100\", \"groups\": { \"42\": { \"label\": \"   \" } } }");

        var provider = new EcdisLabelOverrideProvider(assembly);

        Assert.False(provider.TryGetLabel("S-100", 42, out _));
    }

    [Fact]
    public void Provider_TrimsCuratedLabel()
    {
        var assembly = BuildAssemblyWithResource(
            "S100.labels.json",
            "{ \"specCode\": \"S-100\", \"groups\": { \"42\": { \"label\": \"  Curated  \" } } }");

        var provider = new EcdisLabelOverrideProvider(assembly);

        Assert.True(provider.TryGetLabel("S-100", 42, out var label));
        Assert.Equal("Curated", label);
    }

    [Fact]
    public void ViewingGroup_PrefersOverrideLabel_OverPcName()
    {
        var vm = new EcdisViewingGroupViewModel(
            state: new EcdisDisplayState(),
            specCode: "S-101",
            viewingGroupId: 12010,
            name: "land area (LANDARE)",
            description: "Base: C, D, E, F - Topography and Infrastructure",
            overrideLabel: "Land area");

        Assert.Equal("Land area", vm.DisplayLabel);
    }

    [Fact]
    public void ViewingGroup_FallsBackToPcName_WhenNoOverride()
    {
        var vm = new EcdisViewingGroupViewModel(
            state: new EcdisDisplayState(),
            specCode: "S-101",
            viewingGroupId: 12010,
            name: "land area (LANDARE)",
            description: "Base: C, D, E, F - Topography and Infrastructure",
            overrideLabel: null);

        Assert.Equal("land area (LANDARE)", vm.DisplayLabel);
    }

    [Fact]
    public void ViewingGroup_DetectsNumericName_AndFallsBackToDescription()
    {
        // S-127 / S-421 PCs use the numeric id as the name. The
        // fallback chain should treat that as "no useful name"
        // and surface the description instead.
        var vm = new EcdisViewingGroupViewModel(
            state: new EcdisDisplayState(),
            specCode: "S-421",
            viewingGroupId: 1,
            name: "52000",
            description: "Monitored route",
            overrideLabel: null);

        Assert.Equal("Monitored route", vm.DisplayLabel);
    }

    [Fact]
    public void ViewingGroup_SynthesizesLabel_WhenNothingUsable()
    {
        var vm = new EcdisViewingGroupViewModel(
            state: new EcdisDisplayState(),
            specCode: "S-999",
            viewingGroupId: 42,
            name: "42",
            description: null,
            overrideLabel: null);

        // Format is "Viewing group {0}" — synthesized fallback.
        Assert.Contains("42", vm.DisplayLabel);
    }

    [Fact]
    public void ViewingGroup_TooltipIncludesIdNameAndDescription()
    {
        var vm = new EcdisViewingGroupViewModel(
            state: new EcdisDisplayState(),
            specCode: "S-101",
            viewingGroupId: 12010,
            name: "land area (LANDARE)",
            description: "Base: C, D, E, F - Topography and Infrastructure",
            overrideLabel: "Land area");

        Assert.Contains("#12010", vm.Tooltip);
        Assert.Contains("land area (LANDARE)", vm.Tooltip);
        Assert.Contains("Topography and Infrastructure", vm.Tooltip);
    }

    [Fact]
    public void ViewingGroup_TooltipOmitsNumericName()
    {
        // When the PC "name" is just the numeric id, don't repeat
        // it in the tooltip after "#<id>".
        var vm = new EcdisViewingGroupViewModel(
            state: new EcdisDisplayState(),
            specCode: "S-421",
            viewingGroupId: 1,
            name: "52000",
            description: "Monitored route",
            overrideLabel: "Monitored route");

        Assert.StartsWith("#1", vm.Tooltip);
        Assert.DoesNotContain("— 52000", vm.Tooltip);
        Assert.Contains("Monitored route", vm.Tooltip);
    }

    /// <summary>
    /// Builds an in-memory assembly carrying a single embedded
    /// resource so the provider can be tested in isolation without
    /// touching the shipping JSON files.
    /// </summary>
    private static Assembly BuildAssemblyWithResource(string resourceName, string content)
    {
        return new InMemoryResourceAssembly(resourceName, content);
    }

    /// <summary>
    /// Minimal Assembly stand-in that exposes a single manifest
    /// resource. Only the subset of members the provider uses is
    /// implemented.
    /// </summary>
    private sealed class InMemoryResourceAssembly : Assembly
    {
        private readonly string _resourceName;
        private readonly byte[] _content;

        public InMemoryResourceAssembly(string resourceName, string content)
        {
            // Match the manifest-resource name pattern the provider
            // looks for: "<anything>.Resources.EcdisLabels.<spec>.labels.json"
            _resourceName = $"InMemoryTest.Resources.EcdisLabels.{resourceName}";
            _content = Encoding.UTF8.GetBytes(content);
        }

        public override string[] GetManifestResourceNames() => new[] { _resourceName };

        public override Stream? GetManifestResourceStream(string name)
        {
            if (name == _resourceName)
            {
                return new MemoryStream(_content, writable: false);
            }
            return null;
        }
    }
}
