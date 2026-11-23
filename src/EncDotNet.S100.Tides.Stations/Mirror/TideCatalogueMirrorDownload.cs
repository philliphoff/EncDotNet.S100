namespace EncDotNet.S100.Tides.Stations.Mirror;

/// <summary>A catalogue downloaded from a mirror, with the manifest it was checked against.</summary>
/// <param name="Catalogue">The catalogue.</param>
/// <param name="Manifest">The manifest whose size and SHA-256 the file matched.</param>
public sealed record TideCatalogueMirrorDownload(TideStationCatalogue Catalogue, TideCatalogueManifest Manifest);
