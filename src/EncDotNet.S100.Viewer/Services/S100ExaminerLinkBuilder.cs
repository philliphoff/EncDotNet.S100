using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Builds deep-links into the S-100 Feature Catalogue eXaminer
/// (https://s100examiner.com/) for a product specification, feature type,
/// and attribute (issue #442). Abstracted so view-models can be unit-tested
/// without a live URL.
/// </summary>
/// <remarks>
/// The examiner resolves its query parameters as follows (reverse-engineered
/// from the live site):
/// <list type="bullet">
///   <item><c>?catalog=</c> matches the Feature Catalogue <c>productId</c>
///   (e.g. <c>S-101</c>) case-insensitively, so the viewer's product-spec
///   string maps directly.</item>
///   <item><c>?feature=</c> / <c>?attribute=</c> are resolved tolerantly
///   (exact code → case-insensitive code → name → S-57 alias), so passing
///   the Feature Catalogue camel-case <c>code</c> — which is exactly what
///   <c>FeatureType</c> and <c>PickAttribute.Code</c> already carry — is the
///   canonical form.</item>
/// </list>
/// Only the product specifications the examiner actually hosts (see
/// <see cref="SupportsSpec"/>) produce a link; everything else returns
/// <c>null</c> so the UI can hide the affordance rather than link to a
/// "not found" page.
/// </remarks>
internal interface IS100ExaminerLinkBuilder
{
    /// <summary>
    /// True when examiner links are enabled and the base URL is usable.
    /// When false, every <c>Build…Url</c> method returns <c>null</c>.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// True when the examiner hosts a Feature Catalogue for
    /// <paramref name="productSpec"/> (e.g. <c>"S-101"</c>) and links are
    /// enabled.
    /// </summary>
    bool SupportsSpec(string? productSpec);

    /// <summary>
    /// Builds a catalogue-level deep-link (<c>?catalog=</c>), or <c>null</c>
    /// when links are disabled or the spec is unsupported.
    /// </summary>
    string? BuildCatalogueUrl(string? productSpec);

    /// <summary>
    /// Builds a feature-level deep-link (<c>?catalog=&amp;feature=</c>), or
    /// <c>null</c> when links are disabled, the spec is unsupported, or the
    /// feature code is empty.
    /// </summary>
    string? BuildFeatureUrl(string? productSpec, string? featureCode);

    /// <summary>
    /// Builds an attribute-level deep-link
    /// (<c>?catalog=&amp;feature=&amp;attribute=</c>). The feature code is
    /// optional — the examiner can resolve an attribute without it. Returns
    /// <c>null</c> when links are disabled, the spec is unsupported, or the
    /// attribute code is empty.
    /// </summary>
    string? BuildAttributeUrl(string? productSpec, string? featureCode, string? attributeCode);
}

/// <summary>
/// Default <see cref="IS100ExaminerLinkBuilder"/> that reads its enabled
/// state and base URL from <see cref="ViewerSettings"/> on each call, so a
/// change in the Settings panel takes effect without re-wiring.
/// </summary>
internal sealed class S100ExaminerLinkBuilder : IS100ExaminerLinkBuilder
{
    private readonly ViewerSettings _settings;

    /// <summary>
    /// Product specifications hosted by the examiner, from its
    /// <c>uploads/catalogs.json</c> manifest (checked 2026-07). Compared
    /// case-insensitively against the viewer's product-spec string.
    /// S-411 and S-421 are intentionally absent because the examiner does
    /// not host their Feature Catalogues.
    /// </summary>
    private static readonly IReadOnlySet<string> SupportedSpecs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "S-101", "S-102", "S-104", "S-111", "S-121", "S-122", "S-123",
            "S-124", "S-125", "S-127", "S-128", "S-129", "S-131", "S-201",
            "S-401",
        };

    public S100ExaminerLinkBuilder(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <inheritdoc />
    public bool IsEnabled => _settings.S100ExaminerLinksEnabled && TryGetBaseUri(out _);

    /// <inheritdoc />
    public bool SupportsSpec(string? productSpec) =>
        IsEnabled && !string.IsNullOrWhiteSpace(productSpec) && SupportedSpecs.Contains(productSpec);

    /// <inheritdoc />
    public string? BuildCatalogueUrl(string? productSpec) =>
        Build(productSpec, featureCode: null, attributeCode: null);

    /// <inheritdoc />
    public string? BuildFeatureUrl(string? productSpec, string? featureCode) =>
        string.IsNullOrWhiteSpace(featureCode)
            ? null
            : Build(productSpec, featureCode, attributeCode: null);

    /// <inheritdoc />
    public string? BuildAttributeUrl(string? productSpec, string? featureCode, string? attributeCode) =>
        string.IsNullOrWhiteSpace(attributeCode)
            ? null
            : Build(productSpec, featureCode, attributeCode);

    private string? Build(string? productSpec, string? featureCode, string? attributeCode)
    {
        if (!SupportsSpec(productSpec) || !TryGetBaseUri(out var baseUri))
            return null;

        var query = new List<string>(3)
        {
            "catalog=" + Uri.EscapeDataString(productSpec!),
        };
        if (!string.IsNullOrWhiteSpace(featureCode))
            query.Add("feature=" + Uri.EscapeDataString(featureCode));
        if (!string.IsNullOrWhiteSpace(attributeCode))
            query.Add("attribute=" + Uri.EscapeDataString(attributeCode));

        // Build the URL directly rather than via UriBuilder.Query, which
        // round-trips (and thereby unescapes) the percent-encoded query.
        // Preserve any base path (e.g. a self-hosted mirror under a subpath)
        // while discarding any query/fragment the configured value carried.
        var basePart = baseUri.GetLeftPart(UriPartial.Path);
        return basePart + "?" + string.Join("&", query);
    }

    private bool TryGetBaseUri(out Uri baseUri)
    {
        baseUri = null!;
        var value = _settings.S100ExaminerBaseUrl;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;
        baseUri = uri;
        return true;
    }
}
