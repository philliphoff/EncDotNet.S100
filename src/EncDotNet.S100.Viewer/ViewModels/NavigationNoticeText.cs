using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The first line of a pick report for a product that isn't an S-100 product
/// (<see cref="ProductRegistration.IsS100"/> is <see langword="false"/>), such
/// as a weather model grid: the registration's own
/// <see cref="ProductRegistration.PickReportNotice"/> when it supplies one,
/// otherwise the localized "Weather model data, not for navigation." (design
/// note <c>docs/design/weather-sources.md</c> §3.1).
/// </summary>
internal static class NavigationNoticeText
{
    /// <summary>
    /// Returns the notice for <paramref name="productSpec"/>, or
    /// <see langword="null"/> for an S-100 product that supplies none.
    /// </summary>
    public static string? For(string? productSpec)
    {
        if (string.IsNullOrWhiteSpace(productSpec))
            return null;

        var registry = ProductRegistry.Default;
        if (registry.TryResolve(productSpec, out var registration)
            && registration.PickReportNotice is { Length: > 0 } notice)
        {
            return notice;
        }

        return registry.IsS100(productSpec) ? null : Strings.Pick_NotS100Notice;
    }
}
