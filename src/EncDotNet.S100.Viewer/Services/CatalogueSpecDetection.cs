using System.IO;
using System.Text.RegularExpressions;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Helpers for detecting the S-100 product spec from a feature- or
/// portrayal-catalogue path, and for reading version metadata from the
/// bundled built-in catalogues.
/// </summary>
internal static class CatalogueSpecDetection
{
    private static readonly Regex SpecPattern = new(@"S-\d+", RegexOptions.Compiled);

    /// <summary>
    /// Reads <paramref name="folderPath"/>/portrayal_catalogue.xml and returns
    /// the catalogue's <c>ProductId</c>, or null if the file is missing or
    /// unreadable.
    /// </summary>
    public static string? DetectPortrayalCatalogueSpec(string folderPath)
    {
        try
        {
            var cataloguePath = Path.Combine(folderPath, "portrayal_catalogue.xml");
            if (!File.Exists(cataloguePath)) return null;

            using var stream = File.OpenRead(cataloguePath);
            var catalogue = PortrayalCatalogueReader.Read(stream);
            return string.IsNullOrEmpty(catalogue.ProductId) ? null : catalogue.ProductId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads <paramref name="filePath"/> as a feature catalogue and returns the
    /// first <c>S-NNN</c> token in the catalogue name, or null on parse failure.
    /// </summary>
    public static string? DetectFeatureCatalogueSpec(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var catalogue = FeatureCatalogueReader.Read(stream);
            var match = SpecPattern.Match(catalogue.Name);
            return match.Success ? match.Value : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the version string of the bundled feature catalogue for
    /// <paramref name="spec"/>, or null if not bundled / unreadable.
    /// </summary>
    public static string? ReadBuiltInFeatureCatalogueVersion(string spec)
    {
        try
        {
            using var stream = Specifications.Specification.TryOpenFeatureCatalogue(spec);
            if (stream is null) return null;
            var catalogue = FeatureCatalogueReader.Read(stream);
            return string.IsNullOrEmpty(catalogue.VersionNumber) ? null : catalogue.VersionNumber;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the version string of the bundled portrayal catalogue for
    /// <paramref name="spec"/>, or null if not bundled / unreadable.
    /// </summary>
    public static string? ReadBuiltInPortrayalCatalogueVersion(string spec)
    {
        try
        {
            using var source = Specifications.Specification.CreatePortrayalCatalogueSource(spec);
            using var stream = source.OpenAsync("portrayal_catalogue.xml").GetAwaiter().GetResult();
            var catalogue = PortrayalCatalogueReader.Read(stream);
            return string.IsNullOrEmpty(catalogue.Version) ? null : catalogue.Version;
        }
        catch
        {
            return null;
        }
    }
}
