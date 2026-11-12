namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Opens S-100 Feature Catalogue eXaminer pages (issue #442): which product
/// specifications it hosts, and the catalogue, feature and attribute pages,
/// opened in the browser. Shared by the Feature Catalogues tab and Object
/// Information (#1401).
/// </summary>
internal sealed class ExaminerLinks
{
    private readonly IS100ExaminerLinkBuilder _links;
    private readonly IUrlOpener _opener;

    /// <summary>Creates the links.</summary>
    /// <param name="links">Builds the eXaminer URLs from the current settings.</param>
    /// <param name="opener">Opens a URL in the browser.</param>
    public ExaminerLinks(IS100ExaminerLinkBuilder links, IUrlOpener opener)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(opener);
        _links = links;
        _opener = opener;
    }

    /// <summary>Whether the eXaminer is enabled and hosts a catalogue for <paramref name="productSpec"/>.</summary>
    /// <param name="productSpec">A product specification, such as <c>"S-101"</c>.</param>
    /// <returns><see langword="true"/> when its pages can be opened.</returns>
    public bool SupportsSpec(string? productSpec) => _links.SupportsSpec(productSpec);

    /// <summary>Opens the catalogue page for <paramref name="productSpec"/>, when there is one.</summary>
    /// <param name="productSpec">The product specification.</param>
    public void OpenCatalogue(string? productSpec) =>
        Open(_links.BuildCatalogueUrl(productSpec));

    /// <summary>Opens the page of a feature type, when there is one.</summary>
    /// <param name="productSpec">The product specification.</param>
    /// <param name="featureCode">The feature type's code.</param>
    public void OpenFeature(string? productSpec, string? featureCode) =>
        Open(_links.BuildFeatureUrl(productSpec, featureCode));

    /// <summary>Opens the page of a feature type's attribute, when there is one.</summary>
    /// <param name="productSpec">The product specification.</param>
    /// <param name="featureCode">The feature type's code.</param>
    /// <param name="attributeCode">The attribute's code.</param>
    public void OpenAttribute(string? productSpec, string? featureCode, string? attributeCode) =>
        Open(_links.BuildAttributeUrl(productSpec, featureCode, attributeCode));

    private void Open(string? url)
    {
        if (!string.IsNullOrEmpty(url))
            _opener.Open(url);
    }
}
