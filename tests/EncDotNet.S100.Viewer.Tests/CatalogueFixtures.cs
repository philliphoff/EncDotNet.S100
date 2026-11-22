using System.IO.Compression;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Custom catalogues on disk for the registry and detection tests, made from
/// copies of the bundled S-102 catalogues with a changed version or product.
/// Deletes them on dispose.
/// </summary>
internal sealed class CatalogueFixtures : IDisposable
{
    private static readonly string Source = Path.Combine(AppContext.BaseDirectory, "TestData", "Catalogues", "S102");

    /// <summary>The bundled S-102 feature and portrayal catalogue version.</summary>
    public const string BuiltInVersion = "3.0.0";

    public CatalogueFixtures()
    {
        Root = Path.Combine(Path.GetTempPath(), "encdotnet-catalogues-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>The folder the fixtures are written under.</summary>
    public string Root { get; }

    /// <summary>Writes an S-102 portrayal catalogue folder declaring <paramref name="version"/>.</summary>
    public string PortrayalFolder(string version, string product = "S-102", string? name = null)
    {
        var folder = Path.Combine(Root, name ?? $"pc-{product}-{version}");
        CopyDirectory(Path.Combine(Source, "pc"), folder);
        var xml = Path.Combine(folder, "portrayal_catalogue.xml");
        File.WriteAllText(xml, File.ReadAllText(xml)
            .Replace($"version=\"{BuiltInVersion}\"", $"version=\"{version}\"", StringComparison.Ordinal)
            .Replace("productId=\"S-102\"", $"productId=\"{product}\"", StringComparison.Ordinal));
        return folder;
    }

    /// <summary>Writes a ZIP package of a portrayal catalogue whose files sit under a top folder.</summary>
    public string PortrayalPackage(string version)
    {
        var folder = PortrayalFolder(version, name: $"pkg-src-{version}");
        var zip = Path.Combine(Root, $"pc-{version}.zip");
        ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Fastest, includeBaseDirectory: true);
        return zip;
    }

    /// <summary>Writes an S-102 feature catalogue declaring <paramref name="version"/> and <paramref name="product"/>.</summary>
    public string FeatureFile(string version, string product = "S-102", bool withProductId = true)
    {
        var text = File.ReadAllText(Path.Combine(Source, "fc", "FeatureCatalogue.xml"))
            .Replace($"<S100FC:versionNumber>{BuiltInVersion}</S100FC:versionNumber>", $"<S100FC:versionNumber>{version}</S100FC:versionNumber>", StringComparison.Ordinal)
            .Replace("<S100FC:productId>S-102</S100FC:productId>", withProductId ? $"<S100FC:productId>{product}</S100FC:productId>" : "", StringComparison.Ordinal)
            .Replace("<S100FC:name>S-102</S100FC:name>", $"<S100FC:name>{(withProductId ? product : "Draft bathymetry")}</S100FC:name>", StringComparison.Ordinal);
        var path = Path.Combine(Root, $"fc-{product}-{version}-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Writes a file with <paramref name="content"/>.</summary>
    public string File_(string name, string content)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
